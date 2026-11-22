using System.Diagnostics;
using Newtonsoft.Json.Linq;
using Tammoa.Models;
using Tammoa.Services.Tools;

namespace Tammoa.Services;

public class ToolLoop : IToolLoop
{
    public const int ResultCap = 20_000;
    private static readonly TimeSpan ApproverTimeout = TimeSpan.FromSeconds(60);
    private readonly IOllamaClient _ollama;
    private readonly IToolRegistry _tools;

    public ToolLoop(IOllamaClient ollama, IToolRegistry tools)
    {
        _ollama = ollama;
        _tools = tools;
    }

    public async Task<JObject> RunAsync(ToolLoopRequest r, Action<JToken>? progress, CancellationToken ct)
    {
        var selected = new Dictionary<string, ITool>();
        foreach (var id in r.ToolIds) selected[id] = _tools.Find(id) ?? throw new ArgumentException($"Unknown tool '{id}'.");
        var schemas = new JArray(selected.Values.Select(t => new JObject
        {
            ["type"] = "function",
            ["function"] = new JObject { ["name"] = t.Definition.Name, ["description"] = t.Definition.Description, ["parameters"] = t.Definition.Parameters.DeepClone() },
        }));

        var messages = new JArray();
        if (!string.IsNullOrWhiteSpace(r.System)) messages.Add(new JObject { ["role"] = "system", ["content"] = r.System });
        messages.Add(new JObject { ["role"] = "user", ["content"] = r.Prompt });

        var records = new JArray();
        var thinking = new System.Text.StringBuilder();
        int? promptTokens = null, responseTokens = null;
        double? durationMs = null;
        void Report(string text) // the state so far, shown live on the card
        {
            var live = new JObject { ["text"] = text, ["toolCalls"] = records.DeepClone() };
            if (thinking.Length > 0) live["thinking"] = thinking.ToString();
            progress?.Invoke(live);
        }

        for (var step = 1; step <= r.MaxSteps; step++)
        {
            var before = thinking.Length;
            var turn = await _ollama.ChatTurnAsync(r.Connection, new OllamaTurnRequest(r.Model, messages, schemas, r.Temperature, r.MaxTokens, r.Think), r.Timeout, ct,
                (thinkingSoFar, textSoFar) =>
                {
                    var live = new JObject { ["text"] = textSoFar, ["toolCalls"] = records.DeepClone() };
                    var all = thinking.ToString(0, before) + thinkingSoFar;
                    if (all.Length > 0) live["thinking"] = all;
                    progress?.Invoke(live);
                });
            var chat = turn.Chat;
            if (chat.Thinking is not null) thinking.Append(chat.Thinking);
            promptTokens = Sum(promptTokens, chat.PromptTokens);
            responseTokens = Sum(responseTokens, chat.ResponseTokens);
            if (chat.TotalDurationMs is { } ms) durationMs = (durationMs ?? 0) + ms;

            if (turn.ToolCalls.Count == 0)
            {
                var output = new JObject { ["text"] = chat.Text, ["model"] = chat.Model, ["done"] = chat.Done };
                if (thinking.Length > 0) output["thinking"] = thinking.ToString();
                if (chat.DoneReason is not null) output["doneReason"] = chat.DoneReason;
                if (promptTokens is { } pt) output["promptTokens"] = pt;
                if (responseTokens is { } rt) output["responseTokens"] = rt;
                if (durationMs is { } d) output["totalDurationMs"] = Math.Round(d, 1);
                output["toolCalls"] = records;
                output["steps"] = step;
                return output;
            }

            if (step == r.MaxSteps)
                throw new InvalidOperationException($"The model still wanted to use tools after {r.MaxSteps} steps, so it was stopped. Tools it used: {Used(records)}. Raise \"Max steps\" or make the prompt more direct.");

            messages.Add(new JObject
            {
                ["role"] = "assistant",
                ["content"] = chat.Text,
                ["tool_calls"] = new JArray(turn.ToolCalls.Select(c => new JObject { ["function"] = new JObject { ["name"] = c.Name, ["arguments"] = c.Arguments.DeepClone() } })),
            });
            foreach (var (name, arguments) in turn.ToolCalls)
            {
                var record = await RunToolAsync(r, selected, step, name, arguments, ct);
                records.Add(record);
                messages.Add(new JObject { ["role"] = "tool", ["tool_name"] = name, ["content"] = ModelFacing(record) });
                Report(chat.Text);
            }
        }
        throw new InvalidOperationException("The tool loop ended without an answer."); // not reachable: the last step returns or throws
    }

    private async Task<JObject> RunToolAsync(ToolLoopRequest r, Dictionary<string, ITool> selected, int step, string name, JObject arguments, CancellationToken ct)
    {
        var record = new JObject { ["step"] = step, ["name"] = name, ["arguments"] = arguments.DeepClone() };
        var clock = Stopwatch.StartNew();
        try
        {
            if (!selected.TryGetValue(name, out var tool))
                throw new ArgumentException($"There is no tool '{name}'. Available tools: {string.Join(", ", selected.Keys)}.");
            tool.Check(arguments, r.Context); // the rules first: a call that breaks them never reaches the approver
            if (r.ApproverModel is { Length: > 0 } approver && await AskApproverAsync(r, approver, name, arguments, ct) is { } refusal)
                throw new ToolRejectedException(refusal);
            record["result"] = ToolArgs.Limit(await tool.RunAsync(arguments, r.Context, ct), ResultCap);
            record["decision"] = "ran";
        }
        catch (ToolRejectedException ex)
        {
            record["decision"] = "rejected";
            record["reason"] = ex.Message;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            record["decision"] = "failed";
            record["reason"] = ex.Message;
        }
        record["durationMs"] = Math.Round(clock.Elapsed.TotalMilliseconds, 1);
        return record;
    }

    /// <summary>Asks the small model. Returns null when approved, else the reason. Anything unclear counts as "no".</summary>
    private async Task<string?> AskApproverAsync(ToolLoopRequest r, string model, string name, JObject arguments, CancellationToken ct)
    {
        const string system = "You approve or reject ONE tool call an AI assistant wants to make. Reply with JSON only: {\"approve\": true or false, \"reason\": \"one short sentence\"}. " +
                              "Approve only a call that is safe, fits the task and stays inside the allowed folder. Reject anything destructive, unrelated to the task or outside the allowed folder.";
        var args = ToolArgs.Limit(arguments.ToString(Newtonsoft.Json.Formatting.None), 4000);
        var prompt = $"Task given to the assistant:\n{ToolArgs.Limit(r.Prompt, 2000)}\n\nAllowed folder: {r.Context.Root ?? "(none)"}\nAllowed hosts: {(r.Context.AllowedHosts.Count == 0 ? "(none)" : string.Join(", ", r.Context.AllowedHosts))}\n\nTool: {name}\nArguments: {args}";
        try
        {
            var answer = await _ollama.ChatAsync(r.Connection, new OllamaChatRequest(model, system, prompt, 0, 200, true, false), ApproverTimeout, ct);
            var json = JObject.Parse(answer.Text);
            if (json["approve"] is { Type: JTokenType.Boolean } ok && (bool)ok) return null;
            var reason = (string?)json["reason"];
            return "Not approved: " + (string.IsNullOrWhiteSpace(reason) ? "the approver said no." : reason.Trim());
        }
        catch (Newtonsoft.Json.JsonReaderException)
        {
            return "Not approved: the approver's answer could not be read.";
        }
        catch (InvalidOperationException ex)
        {
            return $"Not approved: the approver could not be asked ({ex.Message}).";
        }
    }

    private static string ModelFacing(JObject record) => (string?)record["decision"] switch
    {
        "ran" => (string)record["result"]!,
        "rejected" => "rejected: " + (string?)record["reason"],
        _ => "error: " + (string?)record["reason"],
    };

    private static string Used(JArray records) => records.Count == 0 ? "none" : string.Join(", ", records.Select(x => (string)x["name"]!).Distinct());
    private static int? Sum(int? a, int? b) => a is null && b is null ? null : (a ?? 0) + (b ?? 0);
}
