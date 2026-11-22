using System.Net.Http.Headers;
using System.Text;

namespace Tammoa.Services;

public class HttpRequestRunner : IHttpRequestRunner
{
    public const int ResponseCap = 1024 * 1024;
    private static readonly HttpClient NoRedirectClient = new(new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient _client;

    public HttpRequestRunner(HttpClient client) => _client = client;

    public async Task<HttpRequestResult> SendAsync(string method, Uri url, IReadOnlyDictionary<string, string> headers, string? body, TimeSpan timeout, CancellationToken ct, bool followRedirects = true)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        var contentType = headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).Value;
        foreach (var (k, v) in headers)
        {
            if (k.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            if (!request.Headers.TryAddWithoutValidation(k, v)) throw new ArgumentException($"Invalid header '{k}'.");
        }
        if (!string.IsNullOrEmpty(body) && method is not ("GET" or "HEAD"))
        {
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var mt)
                ? mt
                : new MediaTypeHeaderValue(body.TrimStart().StartsWith('{') || body.TrimStart().StartsWith('[') ? "application/json" : "text/plain") { CharSet = "utf-8" };
        }

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            using var response = await (followRedirects ? _client : NoRedirectClient).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            var responseHeaders = response.Headers.Concat(response.Content.Headers)
                .ToDictionary(h => h.Key.ToLowerInvariant(), h => string.Join(", ", h.Value));
            var (text, truncated) = await ReadCappedAsync(response, linked.Token);
            return new HttpRequestResult((int)response.StatusCode, response.IsSuccessStatusCode, responseHeaders, text, truncated);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException($"The request timed out after {timeout.TotalSeconds:0} s.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"The request failed: {ex.Message}");
        }
    }

    private static async Task<(string Text, bool Truncated)> ReadCappedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        var truncated = false;
        int n;
        while ((n = await stream.ReadAsync(buffer, ct)) > 0)
        {
            var room = ResponseCap - (int)ms.Length;
            if (room <= 0) { truncated = true; break; }
            ms.Write(buffer, 0, Math.Min(n, room));
            if (n > room) { truncated = true; break; }
        }
        return (Encoding.UTF8.GetString(ms.ToArray()), truncated);
    }
}
