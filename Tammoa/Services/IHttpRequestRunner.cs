namespace Tammoa.Services;

public record HttpRequestResult(int Status, bool Ok, IReadOnlyDictionary<string, string> Headers, string Text, bool Truncated);

/// <summary>Sends one HTTP request. Behind an interface so cards are tested without a network.</summary>
public interface IHttpRequestRunner
{
    /// <exception cref="InvalidOperationException">Network failure or timeout (with a readable reason).</exception>
    /// <exception cref="OperationCanceledException">The run was cancelled.</exception>
    Task<HttpRequestResult> SendAsync(string method, Uri url, IReadOnlyDictionary<string, string> headers, string? body, TimeSpan timeout, CancellationToken ct);
}
