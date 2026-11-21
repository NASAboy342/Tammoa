using Tammoa.Models;

namespace Tammoa.Services;

public record OllamaChatRequest(string Model, string? System, string Prompt, double? Temperature, int? MaxTokens, bool JsonMode, bool? Think);

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
}
