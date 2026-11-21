using Tammoa.Services;

namespace Tammoa.Test.Services;

public class HttpRequestRunnerTest
{
    private static readonly Dictionary<string, string> NoHeaders = new();

    [Test]
    public async Task Post_SendsBodyAsJson_AndReturnsStatusHeadersAndText()
    {
        var stub = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Created)
        {
            Content = new StringContent("{\"id\":1}", System.Text.Encoding.UTF8, "application/json"),
            Headers = { { "X-Thing", "a" } },
        }));
        var r = await new HttpRequestRunner(new HttpClient(stub)).SendAsync("POST", new Uri("https://api.test/x"), new Dictionary<string, string> { ["X-Token"] = "t" }, "{\"a\":1}", TimeSpan.FromSeconds(5), default);

        var (req, body) = stub.Seen.Single();
        Assert.That(req.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(req.Headers.GetValues("X-Token").Single(), Is.EqualTo("t"));
        Assert.That(req.Content!.Headers.ContentType!.MediaType, Is.EqualTo("application/json"));
        Assert.That(body, Is.EqualTo("{\"a\":1}"));
        Assert.That(r.Status, Is.EqualTo(201));
        Assert.That(r.Ok, Is.True);
        Assert.That(r.Headers["x-thing"], Is.EqualTo("a"));
        Assert.That(r.Text, Is.EqualTo("{\"id\":1}"));
    }

    [Test]
    public async Task Get_SendsNoBody_AndPlainTextIsSentAsText()
    {
        var stub = StubHandler.Json("{}");
        var runner = new HttpRequestRunner(new HttpClient(stub));
        await runner.SendAsync("GET", new Uri("https://api.test/"), NoHeaders, "ignored", TimeSpan.FromSeconds(5), default);
        Assert.That(stub.Seen[0].Request.Content, Is.Null);
        await runner.SendAsync("PUT", new Uri("https://api.test/"), NoHeaders, "hello", TimeSpan.FromSeconds(5), default);
        Assert.That(stub.Seen[1].Request.Content!.Headers.ContentType!.MediaType, Is.EqualTo("text/plain"));
    }

    [Test]
    public async Task ExplicitContentType_Wins()
    {
        var stub = StubHandler.Json("{}");
        await new HttpRequestRunner(new HttpClient(stub)).SendAsync("POST", new Uri("https://api.test/"), new Dictionary<string, string> { ["content-type"] = "application/xml" }, "<a/>", TimeSpan.FromSeconds(5), default);
        Assert.That(stub.Seen[0].Request.Content!.Headers.ContentType!.MediaType, Is.EqualTo("application/xml"));
    }

    [Test]
    public async Task ErrorStatus_IsReturned_NotThrown()
    {
        var r = await new HttpRequestRunner(new HttpClient(StubHandler.Json("nope", System.Net.HttpStatusCode.BadRequest))).SendAsync("GET", new Uri("https://api.test/"), NoHeaders, null, TimeSpan.FromSeconds(5), default);
        Assert.That(r.Status, Is.EqualTo(400));
        Assert.That(r.Ok, Is.False);
    }

    [Test]
    public async Task HugeResponse_IsCapped()
    {
        var stub = StubHandler.Json(new string('x', HttpRequestRunner.ResponseCap + 5000));
        var r = await new HttpRequestRunner(new HttpClient(stub)).SendAsync("GET", new Uri("https://api.test/"), NoHeaders, null, TimeSpan.FromSeconds(5), default);
        Assert.That(r.Text.Length, Is.EqualTo(HttpRequestRunner.ResponseCap));
        Assert.That(r.Truncated, Is.True);
    }

    [Test]
    public void NetworkFailure_BecomesInvalidOperation()
    {
        var stub = new StubHandler((_, _) => throw new HttpRequestException("connection refused"));
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => new HttpRequestRunner(new HttpClient(stub)).SendAsync("GET", new Uri("https://api.test/"), NoHeaders, null, TimeSpan.FromSeconds(5), default));
        Assert.That(ex!.Message, Does.Contain("connection refused"));
    }

    [Test]
    public void Timeout_BecomesInvalidOperation_ButCancellationStaysCancellation()
    {
        var slow = new StubHandler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); });
        var runner = new HttpRequestRunner(new HttpClient(slow));
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => runner.SendAsync("GET", new Uri("https://api.test/"), NoHeaders, null, TimeSpan.FromMilliseconds(150), default));
        Assert.That(ex!.Message, Does.Contain("timed out"));
        using var cts = new CancellationTokenSource(100);
        Assert.CatchAsync<OperationCanceledException>(() => runner.SendAsync("GET", new Uri("https://api.test/"), NoHeaders, null, TimeSpan.FromSeconds(30), cts.Token));
    }

    [Test]
    public void InvalidHeader_ThrowsArgumentException() =>
        Assert.ThrowsAsync<ArgumentException>(() => new HttpRequestRunner(new HttpClient(StubHandler.Json("{}"))).SendAsync("GET", new Uri("https://api.test/"), new Dictionary<string, string> { ["Bad Header"] = "x" }, null, TimeSpan.FromSeconds(5), default));
}
