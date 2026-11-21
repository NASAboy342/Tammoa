namespace Tammoa.Test;

/// <summary>An HttpMessageHandler that answers from a delegate, so HTTP clients are tested without a network.</summary>
public sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Seen { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        lock (Seen) Seen.Add((request, body));
        return await respond(request, ct);
    }

    public static StubHandler Json(string json, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") }));
}
