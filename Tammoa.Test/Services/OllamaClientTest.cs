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

    private static string Lines(params object[] objects) => string.Join("\n", objects.Select(o => JObject.FromObject(o).ToString(Newtonsoft.Json.Formatting.None))) + "\n";

    private static StubHandler Stream(string ndjson) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ndjson, System.Text.Encoding.UTF8, "application/x-ndjson") }));

    [Test]
    public async Task Chat_StreamsMessagesOptionsFormatAndThink_AndAssemblesTheAnswer()
    {
        var stub = Stream(Lines(
            new { model = "qwen3:14b", message = new { role = "assistant", content = "", thinking = "hm" }, done = false },
            new { model = "qwen3:14b", message = new { role = "assistant", content = "Hi", thinking = "m" }, done = false },
            new { model = "qwen3:14b", message = new { role = "assistant", content = "!" }, done = false },
            new { model = "qwen3:14b", message = new { role = "assistant", content = "" }, done = true, done_reason = "stop", total_duration = 2500000000L, prompt_eval_count = 12, eval_count = 3 }));
        var r = await new OllamaClient(new HttpClient(stub)).ChatAsync(Conn(),
            new OllamaChatRequest("qwen3:14b", "be brief", "hello", 0.2, 64, true, false), TimeSpan.FromSeconds(30), default);

        var sent = JObject.Parse(stub.Seen[0].Body);
        Assert.That(stub.Seen[0].Request.RequestUri!.AbsolutePath, Is.EqualTo("/api/chat"));
        Assert.That((bool)sent["stream"]!, Is.True, "the answer is streamed");
        Assert.That(sent["messages"]!.Select(m => (string?)m["role"]), Is.EqualTo(new[] { "system", "user" }));
        Assert.That((string?)sent["messages"]![1]!["content"], Is.EqualTo("hello"));
        Assert.That((double)sent["options"]!["temperature"]!, Is.EqualTo(0.2));
        Assert.That((int)sent["options"]!["num_predict"]!, Is.EqualTo(64));
        Assert.That((string?)sent["format"], Is.EqualTo("json"));
        Assert.That((bool)sent["think"]!, Is.False);

        Assert.That(r.Text, Is.EqualTo("Hi!"));
        Assert.That(r.Thinking, Is.EqualTo("hmm"));
        Assert.That(r.Done, Is.True);
        Assert.That(r.DoneReason, Is.EqualTo("stop"));
        Assert.That(r.PromptTokens, Is.EqualTo(12));
        Assert.That(r.ResponseTokens, Is.EqualTo(3));
        Assert.That(r.TotalDurationMs, Is.EqualTo(2500));
    }

    [Test]
    public async Task Chat_CallsOnDeltaWithTheGrowingTexts()
    {
        var stub = Stream(Lines(
            new { message = new { content = "", thinking = "Think" }, done = false },
            new { message = new { content = "", thinking = "ing" }, done = false },
            new { message = new { content = "Ans" }, done = false },
            new { message = new { content = "wer" }, done = false },
            new { message = new { content = "" }, done = true }));
        var seen = new List<(string Thinking, string Text)>();
        await new OllamaClient(new HttpClient(stub)).ChatAsync(Conn(), new OllamaChatRequest("m", null, "p", null, null, false, null), TimeSpan.FromSeconds(5), default, (t, a) => seen.Add((t, a)));
        Assert.That(seen, Is.EqualTo(new[] { ("Think", ""), ("Thinking", ""), ("Thinking", "Ans"), ("Thinking", "Answer"), ("Thinking", "Answer") }));
    }

    [Test]
    public async Task Chat_DeliversEachPieceAsItArrives_NotAfterTheWholeAnswer()
    {
        var pipe = new System.IO.Pipelines.Pipe();
        var stub = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(pipe.Reader.AsStream()) }));
        var firstSeen = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = new OllamaClient(new HttpClient(stub)).ChatAsync(Conn(), new OllamaChatRequest("m", null, "p", null, null, false, null), TimeSpan.FromSeconds(10), default,
            (_, text) => firstSeen.TrySetResult(text));

        async Task Send(string line) { await pipe.Writer.WriteAsync(System.Text.Encoding.UTF8.GetBytes(line + "\n")); }
        await Send("{\"message\":{\"content\":\"Hel\"},\"done\":false}");
        var early = await firstSeen.Task.WaitAsync(TimeSpan.FromSeconds(5)); // seen while the server has NOT finished the answer
        Assert.That(chat.IsCompleted, Is.False, "the call is still running while the first piece is already delivered");
        Assert.That(early, Is.EqualTo("Hel"));

        await Send("{\"message\":{\"content\":\"lo\"},\"done\":false}");
        await Send("{\"message\":{\"content\":\"\"},\"done\":true,\"done_reason\":\"stop\"}");
        await pipe.Writer.CompleteAsync();
        Assert.That((await chat).Text, Is.EqualTo("Hello"));
    }

    [Test]
    public void Chat_AnErrorSentInTheStream_IsReported()
    {
        var stub = Stream(Lines(new { message = new { content = "Par" }, done = false }, new { error = "model runner crashed" }));
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => new OllamaClient(new HttpClient(stub)).ChatAsync(Conn(), new OllamaChatRequest("m", null, "p", null, null, false, null), TimeSpan.FromSeconds(5), default));
        Assert.That(ex!.Message, Does.Contain("model runner crashed"));
    }

    [Test]
    public async Task Chat_AStreamThatEndsWithoutDone_ReturnsWhatArrivedAndSaysItIsNotDone()
    {
        var stub = Stream(Lines(new { message = new { content = "cut o" }, done = false }));
        var r = await new OllamaClient(new HttpClient(stub)).ChatAsync(Conn(), new OllamaChatRequest("m", null, "p", null, null, false, null), TimeSpan.FromSeconds(5), default);
        Assert.That(r.Text, Is.EqualTo("cut o"));
        Assert.That(r.Done, Is.False);
    }

    [Test]
    public async Task Chat_TheTimeoutIsSilence_ALongAnswerThatKeepsComingIsNotCutOff()
    {
        var pipe = new System.IO.Pipelines.Pipe();
        var stub = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(pipe.Reader.AsStream()) }));
        var chat = new OllamaClient(new HttpClient(stub)).ChatAsync(Conn(), new OllamaChatRequest("m", null, "p", null, null, false, null), TimeSpan.FromMilliseconds(400), default);
        for (var i = 0; i < 6; i++) // 6 x 150 ms = 900 ms in total, more than the 400 ms timeout, but never silent for 400 ms
        {
            await pipe.Writer.WriteAsync(System.Text.Encoding.UTF8.GetBytes("{\"message\":{\"content\":\"x\"},\"done\":false}\n"));
            await Task.Delay(150);
        }
        await pipe.Writer.WriteAsync(System.Text.Encoding.UTF8.GetBytes("{\"message\":{\"content\":\"\"},\"done\":true}\n"));
        await pipe.Writer.CompleteAsync();
        Assert.That((await chat).Text, Is.EqualTo("xxxxxx"));
    }

    [Test]
    public void Chat_CancellingStopsTheStream()
    {
        var pipe = new System.IO.Pipelines.Pipe();
        var stub = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(pipe.Reader.AsStream()) }));
        using var cts = new CancellationTokenSource(150);
        Assert.CatchAsync<OperationCanceledException>(() => new OllamaClient(new HttpClient(stub)).ChatAsync(Conn(), new OllamaChatRequest("m", null, "p", null, null, false, null), TimeSpan.FromSeconds(30), cts.Token));
    }

    [Test]
    public async Task Chat_OmitsWhatWasNotSet()
    {
        var stub = Stream(Lines(new { message = new { content = "x" }, done = true }));
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
        Assert.That(ex!.Message, Does.Contain("was silent for"));
        using var cts = new CancellationTokenSource(100);
        Assert.CatchAsync<OperationCanceledException>(() => client.ChatAsync(Conn(), new OllamaChatRequest("m", null, "p", null, null, false, null), TimeSpan.FromSeconds(30), cts.Token));
    }

    [Test]
    public async Task ChatTurn_SendsTheToolsAndMessages_AndReadsTheToolCalls()
    {
        var stub = Stream(Lines(
            new { model = "m", message = new { role = "assistant", content = "", tool_calls = new[] { new { function = new { name = "read_file", arguments = new { path = "a.txt" } } } } }, done = false },
            new { model = "m", message = new { role = "assistant", content = "" }, done = true, done_reason = "stop", eval_count = 4 }));
        var messages = new JArray(new JObject { ["role"] = "user", ["content"] = "hi" }, new JObject { ["role"] = "tool", ["tool_name"] = "x", ["content"] = "r" });
        var tools = new JArray(new JObject { ["type"] = "function", ["function"] = new JObject { ["name"] = "read_file" } });
        var r = await new OllamaClient(new HttpClient(stub)).ChatTurnAsync(Conn(), new OllamaTurnRequest("m", messages, tools, 0.1, 50, null), TimeSpan.FromSeconds(30), default);

        var sent = JObject.Parse(stub.Seen[0].Body);
        Assert.That((string)sent["tools"]![0]!["function"]!["name"]!, Is.EqualTo("read_file"));
        Assert.That(sent["messages"]!.Count(), Is.EqualTo(2));
        Assert.That((string)sent["messages"]![1]!["role"]!, Is.EqualTo("tool"));
        Assert.That((bool)sent["stream"]!, Is.True);
        Assert.That(sent["think"], Is.Null);
        Assert.That(r.ToolCalls, Has.Count.EqualTo(1));
        Assert.That(r.ToolCalls[0].Name, Is.EqualTo("read_file"));
        Assert.That((string)r.ToolCalls[0].Arguments["path"]!, Is.EqualTo("a.txt"));
        Assert.That(r.Chat.ResponseTokens, Is.EqualTo(4));
    }

    [Test]
    public async Task ChatTurn_ArgumentsSentAsJsonText_AreParsed_AndNoToolsMeansNoToolsField()
    {
        var stub = Stream(Lines(
            new { model = "m", message = new { role = "assistant", content = "", tool_calls = new[] { new { function = new { name = "t", arguments = "{\"a\":1}" } } } }, done = true }));
        var r = await new OllamaClient(new HttpClient(stub)).ChatTurnAsync(Conn(), new OllamaTurnRequest("m", new JArray(), null, null, null, null), TimeSpan.FromSeconds(30), default);
        Assert.That((int)r.ToolCalls[0].Arguments["a"]!, Is.EqualTo(1));
        Assert.That(JObject.Parse(stub.Seen[0].Body)["tools"], Is.Null);
    }

    [Test]
    public void ChatTurn_AModelWithoutToolSupport_GetsAClearMessage()
    {
        var stub = StubHandler.Json("{\"error\":\"registry.ollama.ai/library/gemma:2b does not support tools\"}", HttpStatusCode.BadRequest);
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => new OllamaClient(new HttpClient(stub)).ChatTurnAsync(Conn(),
            new OllamaTurnRequest("gemma:2b", new JArray(), new JArray(new JObject()), null, null, null), TimeSpan.FromSeconds(30), default));
        Assert.That(ex!.Message, Does.Contain("does not support tools").And.Contain("untick the tools"));
    }
}
