using Newtonsoft.Json.Linq;
using Tammoa.Models;

namespace Tammoa.Services;

public record OllamaChatRequest(string Model, string? System, string Prompt, double? Temperature, int? MaxTokens, bool JsonMode, bool? Think);

/// <summary>One model call with a full message list (for tool loops). <see cref="Tools"/> are OpenAI-style function schemas ({ type:"function", function:{ name, description, parameters } }).</summary>
public record OllamaTurnRequest(string Model, JArray Messages, JArray? Tools, double? Temperature, int? MaxTokens, bool? Think);

/// <summary>What the model answered in one turn, plus the tool calls it asked for ([{ name, arguments }], arguments always an object).</summary>
public record OllamaTurnResult(OllamaChatResult Chat, IReadOnlyList<(string Name, JObject Arguments)> ToolCalls);

public record OllamaChatResult(string Text, string? Thinking, string Model, bool Done, string? DoneReason, int? PromptTokens, int? ResponseTokens, double? TotalDurationMs);

/// <summary>Talks to an Ollama server (local or remote). The connection's key, when set, is sent as a Bearer token (for servers behind an auth proxy).</summary>
public interface IOllamaClient
{
    /// <exception cref="InvalidOperationException">Unreachable, unauthorised or not an Ollama server (with a readable reason).</exception>
    Task<string> GetVersionAsync(Connection connection, CancellationToken ct);

    /// <exception cref="InvalidOperationException">As above.</exception>
    Task<IReadOnlyList<string>> ListModelsAsync(Connection connection, CancellationToken ct);

    /// <summary>One chat turn (optional system message + the prompt), STREAMED: <paramref name="onDelta"/> receives the thinking and the answer
    /// written so far (both complete texts, not pieces) every time the model sends something. <paramref name="timeout"/> is how long the
    /// model may stay SILENT (it restarts with every piece), so a long but healthy answer is never cut off.</summary>
    /// <exception cref="InvalidOperationException">As above, an unknown model, an error sent by the server, or silence for longer than the timeout.</exception>
    Task<OllamaChatResult> ChatAsync(Connection connection, OllamaChatRequest request, TimeSpan timeout, CancellationToken ct, Action<string, string>? onDelta = null);

    /// <summary>One streamed turn of a conversation that may use tools. Same streaming and silence rules as <see cref="ChatAsync"/>.</summary>
    /// <exception cref="InvalidOperationException">As above; also when the model does not support tools.</exception>
    Task<OllamaTurnResult> ChatTurnAsync(Connection connection, OllamaTurnRequest request, TimeSpan timeout, CancellationToken ct, Action<string, string>? onDelta = null);
}
