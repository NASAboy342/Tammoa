using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Services.Cards;

/// <summary>One chat turn with an Ollama model. TODO(agent loop): tool calling and multi-turn loops are a separate card.</summary>
public class OllamaCard : ICardType
{
    public const string TypeId = "ai.ollama";
    private readonly ITemplateEngine _templates;
    private readonly IConnectionService _connections;
    private readonly IOllamaClient _ollama;

    public OllamaCard(ITemplateEngine templates, IConnectionService connections, IOllamaClient ollama)
    {
        _templates = templates;
        _connections = connections;
        _ollama = ollama;
    }

    public CardTypeInfo Info { get; } = new(
        TypeId, "Ollama",
        "Asks an Ollama model (local or on a server). Output: { text, thinking?, model, promptTokens, responseTokens, totalDurationMs, json? }.",
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
            new CardParamInfo("timeoutSeconds", "Timeout (seconds)", ParamKind.Number, DefaultValue: 120, Help: "1 to 1800. Small local models can be slow on the first call."),
        ]);

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

        var result = await _ollama.ChatAsync(connection,
            new OllamaChatRequest(model, system, prompt, temperature, maxTokens is { } m ? (int)m : null, jsonMode, think),
            TimeSpan.FromSeconds(seconds), ct);

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
