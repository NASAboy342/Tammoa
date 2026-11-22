using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Helper;
using Tammoa.Models;
using Tammoa.Services.Tools;

namespace Tammoa.Services.Cards;

/// <summary>One chat turn with an Ollama model; with tools selected it becomes a loop: the model may call the chosen tools (inside the card's rules) until it answers.</summary>
public class OllamaCard : ICardType
{
    public const string TypeId = "ai.ollama";
    private readonly ITemplateEngine _templates;
    private readonly IConnectionService _connections;
    private readonly IOllamaClient _ollama;
    private readonly IToolLoop _loop;

    public OllamaCard(ITemplateEngine templates, IConnectionService connections, IOllamaClient ollama, IToolLoop loop)
    {
        _loop = loop;
        _templates = templates;
        _connections = connections;
        _ollama = ollama;
    }

    public CardTypeInfo Info { get; } = new(
        TypeId, "Ollama",
        "Asks an Ollama model (local or on a server) and shows its thinking and answer live while it writes. Output: { text, thinking?, model, promptTokens, responseTokens, totalDurationMs, json? }.",
        CardCategory.Ai, "bot",
        [
            new CardParamInfo("connection", "Connection", ParamKind.Connection, Required: true, Provider: ConnectionProvider.Ollama, Help: "Create one under Connections."),
            new CardParamInfo("model", "Model", ParamKind.Select, Required: true, OptionsSource: "ollama.models", OptionsDependsOn: "connection", Help: "Models the connection's server has."),
            new CardParamInfo("prompt", "Prompt", ParamKind.Code, Required: true, CodeLanguage: "text", Placeholder: "Summarise: {{input.stdout}}", Help: "Templates work: {{input.…}}, {{run.input}}."),
            new CardParamInfo("system", "System prompt (optional)", ParamKind.Code, CodeLanguage: "text"),
            new CardParamInfo("temperature", "Temperature (optional)", ParamKind.Number, Help: "Empty = the model's default."),
            new CardParamInfo("maxTokens", "Max tokens (optional)", ParamKind.Number, Help: "Empty = no limit."),
            new CardParamInfo("jsonMode", "Answer as JSON", ParamKind.Bool, DefaultValue: false, Help: "Asks the model for valid JSON; it is also returned as { json }."),
            new CardParamInfo("think", "Reasoning (think)", ParamKind.Select, DefaultValue: "auto",
                Options: [new("auto", "Auto (model default)"), new("on", "On"), new("off", "Off")], Help: "For reasoning models such as qwen3. Off is faster; auto sends nothing."),
            new CardParamInfo("tools", "Tools the model may use", ParamKind.MultiSelect, Options: ToolIds.Options,
                Help: "None by default: the model only answers. Tick a tool to let it act. A model that supports tools is needed (qwen3, llama3.1…)."),
            new CardParamInfo("toolRoot", "Tools folder (root)", ParamKind.Text, Placeholder: "/path/to/a/work/folder",
                Help: "Required for the file and script tools. They can only touch this folder (and below); anything else is refused. Absolute path, the folder must exist."),
            new CardParamInfo("allowedHosts", "Allowed hosts (HTTP tool)", ParamKind.Text, Placeholder: "api.example.com, *.example.org",
                Help: "Comma separated. The HTTP tool only talks to these hosts; empty = the HTTP tool refuses everything."),
            new CardParamInfo("maxSteps", "Max steps", ParamKind.Number, DefaultValue: 8, Help: "1 to 30 model calls. A step is one question to the model; it stops the loop when the model keeps asking for tools."),
            new CardParamInfo("approverModel", "Approver model (optional)", ParamKind.Select, OptionsSource: "ollama.models", OptionsDependsOn: "connection",
                Help: "A small model that must approve every tool call first (it sees the task, the tool and its arguments). Empty = no approval. A refused call is told to the main model, which can try something else."),
            new CardParamInfo("timeoutSeconds", "Stop if silent for (seconds)", ParamKind.Number, DefaultValue: 120, Help: "1 to 1800. The card stops when the model sends nothing for this long (a long answer that keeps coming is never cut off). The first call can be slow while the model loads."),
        ])
    {
        Guide = new CardGuide(
            "Asks an Ollama model a question and gives you its answer. Choose a connection (your Ollama address) and a model, write a prompt.",
            "Receives the previous card's output as input. Put {{input.field}} in the prompt to include it; {{run.input}} is the Run input.",
            new JObject { ["text"] = "Two files were found.", ["model"] = "qwen3:14b", ["done"] = true, ["doneReason"] = "stop", ["promptTokens"] = 23, ["responseTokens"] = 5, ["totalDurationMs"] = 1200.5 },
            [
                new GuideExample("Summarise the previous card's output", "The prompt can mix instructions and data.",
                    new JObject { ["prompt"] = "Summarise this in two sentences:\n{{input.stdout}}", ["think"] = "off" }, "Summarise this in two sentences:\n{{input.stdout}}", "text",
                    new JObject { ["stdout"] = "a.txt\nb.txt\n" }, "The answer is in {{input.text}} for the next card."),
                new GuideExample("Get structured data back", "Switch on \"Answer as JSON\" and describe the shape you want.",
                    new JObject { ["prompt"] = "Return JSON like {\"sentiment\": \"positive\" or \"negative\"} for this review:\n{{run.input}}", ["jsonMode"] = true, ["temperature"] = 0, ["think"] = "off" },
                    "Return JSON like {\"sentiment\": \"positive\" or \"negative\"} for this review:\n{{run.input}}", "text", RunInput: "Great product, fast delivery",
                    Result: "The answer is also parsed: the next card reads {{input.json.sentiment}} (e.g. use an IF card on it)."),
                new GuideExample("Reuse a style guide stored in a Data card", "Keep the instructions in ONE Data card (name it Style) and use it in every AI card: change the text once, every card follows.",
                    new JObject { ["system"] = "{{data.Style}}", ["prompt"] = "{{input.stdout}}", ["think"] = "off" },
                    "System prompt: {{data.Style}}\nPrompt: {{input.stdout}}", "text", new JObject { ["stdout"] = "Release notes for version 2" }, SampleData: new JObject { ["Style"] = "Be brief and friendly." },
                    Result: "The model gets \"Be brief and friendly.\" as its system prompt. No wiring to the Data card is needed."),
                new GuideExample("Give the model a role", "A system prompt sets the style for every answer.",
                    new JObject { ["system"] = "You are a terse assistant. Answer in one sentence.", ["prompt"] = "{{run.input}}", ["think"] = "off" },
                    "System: You are a terse assistant. Answer in one sentence.\nPrompt: {{run.input}}", "text", RunInput: "Why is the sky blue?", Result: "A one-sentence answer in {{input.text}}."),
            ],
            ["Output fields: text (the answer), json (JSON mode), thinking (reasoning models), model, promptTokens, responseTokens, totalDurationMs.", "Small models work best with short, direct prompts. Reasoning Off is much faster for models like qwen3.", "The first call loads the model into memory and can take a while; the timeout is 120 s by default.", "Tools: tick what the model may do (read file, write file, HTTP request, run script). File and script tools only work inside the Tools folder; the HTTP tool only talks to the Allowed hosts. A refused call is told to the model, it is not an error. Output then also has toolCalls (what it called, and ran / rejected / failed) and steps.", "An Approver model (a small model) can be asked about every tool call first; if it does not clearly say yes, the call is refused. Run script is only a best-effort guard (paths in the text are checked, it is not a sandbox): keep it off unless you need it, and use the approver."]),
    };

    public async Task<CardResult> ExecuteAsync(CardExecutionContext context, CancellationToken ct)
    {
        var ctx = context.TemplateContext;
        var connectionId = ParamReader.GetString(context.Params, "connection").Trim();
        if (connectionId.Length == 0) throw new ArgumentException("Choose a connection.");
        Connection connection;
        try { connection = _connections.GetForUse(connectionId); }
        catch (KeyNotFoundException) { throw new ArgumentException("The connection of this card no longer exists. Choose another."); }
        if (connection.Provider != ConnectionProvider.Ollama) throw new ArgumentException($"'{connection.Name}' is not an Ollama connection.");

        var model = ParamReader.GetString(context.Params, "model").Trim();
        if (model.Length == 0) throw new ArgumentException("Choose a model.");
        var prompt = _templates.Render(ParamReader.GetString(context.Params, "prompt"), ctx);
        if (prompt.Trim().Length == 0) throw new ArgumentException("The prompt is empty.");
        var systemText = ParamReader.GetString(context.Params, "system");
        var system = systemText.Trim().Length > 0 ? _templates.Render(systemText, ctx) : null;

        var temperature = ParamReader.GetOptionalDouble(context.Params, "temperature");
        if (temperature is < 0 or > 2) throw new ArgumentException("Temperature must be between 0 and 2.");
        var maxTokens = ParamReader.GetOptionalDouble(context.Params, "maxTokens");
        if (maxTokens is < 1 or > 1_000_000) throw new ArgumentException("Max tokens must be at least 1.");
        var seconds = ParamReader.GetInt(context.Params, "timeoutSeconds", 120);
        if (seconds < 1 || seconds > 1800) throw new ArgumentException("Timeout must be between 1 and 1800 seconds.");
        var jsonMode = ParamReader.GetBool(context.Params, "jsonMode", false);
        bool? think = ParamReader.GetString(context.Params, "think") switch { "on" => true, "off" => false, "" or "auto" => null, var other => throw new ArgumentException($"Unknown think setting '{other}'.") };

        var toolIds = ParamReader.GetStringList(context.Params, "tools");
        if (toolIds.Count > 0)
        {
            if (jsonMode) throw new ArgumentException("\"Answer as JSON\" cannot be combined with tools. Switch one of them off.");
            var maxSteps = ParamReader.GetInt(context.Params, "maxSteps", 8);
            if (maxSteps < 1 || maxSteps > 30) throw new ArgumentException("Max steps must be between 1 and 30.");
            var unknown = toolIds.FirstOrDefault(id => ToolIds.Options.All(o => o.Value != id));
            if (unknown is not null) throw new ArgumentException($"Unknown tool '{unknown}'.");
            string? root = null;
            if (toolIds.Any(id => id is ToolIds.ReadFile or ToolIds.WriteFile or ToolIds.RunScript))
            {
                var rootText = _templates.Render(ParamReader.GetString(context.Params, "toolRoot"), ctx).Trim();
                if (rootText.Length == 0) throw new ArgumentException("Set the tools folder (root): the file and script tools only work inside it.");
                root = FilePaths.RequireAbsolute(rootText);
                if (!Directory.Exists(root)) throw new ArgumentException($"The tools folder does not exist: {root}");
            }
            var hosts = ParamReader.GetStringList(new JObject { ["h"] = _templates.Render(ParamReader.GetString(context.Params, "allowedHosts"), ctx) }, "h");
            if (toolIds.Contains(ToolIds.HttpRequest) && hosts.Count == 0) throw new ArgumentException("Set at least one allowed host for the HTTP tool.");
            var approver = ParamReader.GetString(context.Params, "approverModel").Trim();

            return (await _loop.RunAsync(new ToolLoopRequest(connection, model, system, prompt, temperature, maxTokens is { } mt ? (int)mt : null, think, TimeSpan.FromSeconds(seconds),
                toolIds, new ToolContext(root, hosts), approver.Length > 0 ? approver : null, maxSteps), context.Progress, ct));
        }

        var result = await _ollama.ChatAsync(connection,
            new OllamaChatRequest(model, system, prompt, temperature, maxTokens is { } m ? (int)m : null, jsonMode, think),
            TimeSpan.FromSeconds(seconds), ct,
            (thinkingSoFar, textSoFar) =>
            {
                // what the model has written so far: the engine shows it live and replaces it with the final output
                var live = new JObject { ["text"] = textSoFar };
                if (thinkingSoFar.Length > 0) live["thinking"] = thinkingSoFar;
                context.Progress?.Invoke(live);
            });

        var output = new JObject { ["text"] = result.Text, ["model"] = result.Model, ["done"] = result.Done };
        if (result.Thinking is not null) output["thinking"] = result.Thinking;
        if (result.DoneReason is not null) output["doneReason"] = result.DoneReason;
        if (result.PromptTokens is { } pt) output["promptTokens"] = pt;
        if (result.ResponseTokens is { } rt) output["responseTokens"] = rt;
        if (result.TotalDurationMs is { } ms) output["totalDurationMs"] = ms;
        if (jsonMode)
        {
            try { output["json"] = JToken.Parse(result.Text); }
            catch (JsonReaderException) { throw new InvalidOperationException("The model did not return valid JSON. Try again or lower the temperature."); }
        }
        return output;
    }
}
