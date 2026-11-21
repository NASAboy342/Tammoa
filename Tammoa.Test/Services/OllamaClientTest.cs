using System.Net;
using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Services;

namespace Tammoa.Test.Services;

public class OllamaClientTest
{
    private static Connection Conn(string? key = null, string url = "http://ollama.test:11434/") =>
        new("c1", "Test", ConnectionProvider.Ollama, url, key, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Test]
    public async Task Version_AndModels_AreReadAndSorted_AndTheKeyIsSentAsBearer()
    {
        var stub = new StubHandler((req, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(req.RequestUri!.AbsolutePath == "/api/version" ? "{\"version\":\"0.9.0\"}" : "{\"models\":[{\"name\":\"qwen3:14b\"},{\"name\":\"Llama3:8b\"}]}"),
        }));
        var client = new OllamaClient(new HttpClient(stub));
        Assert.That(await client.GetVersionAsync(Conn("sekret"), default), Is.EqualTo("0.9.0"));
        Assert.That(await client.ListModelsAsync(Conn("sekret"), default), Is.EqualTo(new[] { "Llama3:8b", "qwen3:14b" }));
        Assert.That(stub.Seen.All(s => s.Request.Headers.Authorization!.ToString() == "Bearer sekret"), Is.True);
        Assert.That(stub.Seen[0].Request.RequestUri!.ToString(), Is.EqualTo("http://ollama.test:11434/api/version"), "trailing slash is not doubled");
    }

    [Test]
    public async Task NoKey_SendsNoAuthorizationHeader()
    {
        var stub = StubHandler.Json("{\"models\":[]}");
        await new OllamaClient(new HttpClient(stub)).ListModelsAsync(Conn(), default);
        Assert.That(stub.Seen[0].Request.Headers.Authorization, Is.Null);
    }

    [Test]
    public async Task Chat_SendsMessagesOptionsFormatAndThink_AndReadsTheAnswer()
    {
        var stub = StubHandler.Json("""
            { "model": "qwen3:14b", "message": { "role": "assistant", "content": "Hi!", "thinking": "hmm" }, "done": true, "done_reason": "stop",
              "total_duration": 2500000000, "prompt_eval_count": 12, "eval_count": 3 }
            """);
        var r = await new OllamaClient(new HttpClient(stub)).ChatAsync(Conn(),
            new OllamaChatRequest("qwen3:14b", "be brief", "hello", 0.2, 64, true, false), TimeSpan.FromSeconds(30), default);

        var sent = JObject.Parse(stub.Seen[0].Body);
        Assert.That(stub.Seen[0].Request.RequestUri!.AbsolutePath, Is.EqualTo("/api/chat"));
        Assert.That((bool)sent["stream"]!, Is.False);
        Assert.That(sent["messages"]!.Select(m => (string?)m["role"]), Is.EqualTo(new[] { "system", "user" }));
        Assert.That((string?)sent["messages"]![1]!["content"], Is.EqualTo("hello"));
        Assert.That((double)sent["options"]!["temperature"]!, Is.EqualTo(0.2));
        Assert.That((int)sent["options"]!["num_predict"]!, Is.EqualTo(64));
        Assert.That((string?)sent["format"], Is.EqualTo("json"));
        Assert.That((bool)sent["think"]!, Is.False);

        Assert.That(r.Text, Is.EqualTo("Hi!"));
        Assert.That(r.Thinking, Is.EqualTo("hmm"));
        Assert.That(r.PromptTokens, Is.EqualTo(12));
        Assert.That(r.ResponseTokens, Is.EqualTo(3));
        Assert.That(r.TotalDurationMs, Is.EqualTo(2500));
    }

    [Test]
    public async Task Chat_OmitsWhatWasNotSet()
    {
        var stub = StubHandler.Json("{\"message\":{\"content\":\"x\"}}");
        await new OllamaClient(new HttpClient(stub)).ChatAsync(Conn(), new OllamaChatRequest("m", null, "p", null, null, false, null), TimeSpan.FromSeconds(5), default);
        var sent = JObject.Parse(stub.Seen[0].Body);
        Assert.That(sent["messages"]!.Count(), Is.EqualTo(1));
        Assert.That(sent["options"], Is.Null);
        Assert.That(sent["format"], Is.Null);
        Assert.That(sent["think"], Is.Null);
    }

    [TestCase(HttpStatusCode.Unauthorized, "API key")]
    [TestCase(HttpStatusCode.Forbidden, "API key")]
    public void AuthFailures_PointAtTheKey(HttpStatusCode status, string expected)
    {
        var client = new OllamaClient(new HttpClient(StubHandler.Json("{}", status)));
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => client.GetVersionAsync(Conn(), default));
        Assert.That(ex!.Message, Does.Contain(expected));
    }

    [Test]
    public void UnknownModel_ShowsOllamasOwnMessage()
    {
        var client = new OllamaClient(new HttpClient(StubHandler.Json("{\"error\":\"model 'nope' not found\"}", HttpStatusCode.NotFound)));
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => client.ChatAsync(Conn(), new OllamaChatRequest("nope", null, "p", null, null, false, null), TimeSpan.FromSeconds(5), default));
        Assert.That(ex!.Message, Does.Contain("model 'nope' not found"));
    }

    [Test]
    public void Unreachable_SaysWhichAddress()
    {
        var client = new OllamaClient(new HttpClient(new StubHandler((_, _) => throw new HttpRequestException("Connection refused"))));
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => client.ListModelsAsync(Conn(url: "http://nowhere:1"), default));
        Assert.That(ex!.Message, Does.Contain("http://nowhere:1").And.Contain("Connection refused"));
    }

    [Test]
    public void NotOllama_IsRecognised()
    {
        var client = new OllamaClient(new HttpClient(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>hi</html>") }))));
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => client.GetVersionAsync(Conn(), default));
        Assert.That(ex!.Message, Does.Contain("does not look like an Ollama server"));
    }

    [Test]
    public void Timeout_IsReported_AndCancellationIsNot()
    {
        var slow = new StubHandler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); });
        var client = new OllamaClient(new HttpClient(slow));
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => client.ChatAsync(Conn(), new OllamaChatRequest("m", null, "p", null, null, false, null), TimeSpan.FromMilliseconds(150), default));
        Assert.That(ex!.Message, Does.Contain("did not answer within"));
        using var cts = new CancellationTokenSource(100);
        Assert.CatchAsync<OperationCanceledException>(() => client.ChatAsync(Conn(), new OllamaChatRequest("m", null, "p", null, null, false, null), TimeSpan.FromSeconds(30), cts.Token));
    }
}
