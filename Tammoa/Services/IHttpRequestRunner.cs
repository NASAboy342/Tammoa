namespace Tammoa.Services;

public record HttpRequestResult(int Status, bool Ok, IReadOnlyDictionary<string, string> Headers, string Text, bool Truncated);

/// <summary>Sends one HTTP request. Behind an interface so cards are tested without a network.</summary>
public interface IHttpRequestRunner
{
    /// <summary>`followRedirects` false returns a 3xx answer as it is (its Location header tells where to), so a caller can check every host itself.</summary>
    /// <exception cref="InvalidOperationException">Network failure or timeout (with a readable reason).</exception>
    /// <exception cref="OperationCanceledException">The run was cancelled.</exception>
    Task<HttpRequestResult> SendAsync(string method, Uri url, IReadOnlyDictionary<string, string> headers, string? body, TimeSpan timeout, CancellationToken ct, bool followRedirects = true);
}
