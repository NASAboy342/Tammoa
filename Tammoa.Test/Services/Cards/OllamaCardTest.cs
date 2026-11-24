using Newtonsoft.Json.Linq;
using NSubstitute;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Services;
using Tammoa.Services.Cards;

namespace Tammoa.Test.Services.Cards;

public class OllamaCardTest
{
    private IConnectionService _connections = null!;
    private IOllamaClient _client = null!;
    private IToolLoop _loop = null!;
    private OllamaCard _sut = null!;
    private static string Json(object o) => JObject.FromObject(o).ToString();
    private static CardExecutionContext Ctx(object p, JToken? input = null, JToken? run = null) => new(JObject.Parse(Json(p)), input, run);

    private static Connection Conn(ConnectionProvider provider = ConnectionProvider.Ollama) =>
        new("c1", "Home", provider, "http://x", "k", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [SetUp]
    public void SetUp()
    {
        _connections = Substitute.For<IConnectionService>();
        _client = Substitute.For<IOllamaClient>();
        _loop = Substitute.For<IToolLoop>();
        _connections.GetForUse("c1").Returns(Conn());
        _client.ChatAsync(default!, default!, default, default)
            .ReturnsForAnyArgs(Task.FromResult(new OllamaChatResult("Hello", null, "qwen3:14b", true, "stop", 5, 2, 120.5)));
        _sut = new OllamaCard(new TemplateEngine(), _connections, _client, _loop);
    }

    [Test]
    public async Task Run_RendersPromptAndSendsEverythingConfigured()
    {
        var r = (await _sut.ExecuteAsync(Ctx(new
        {
            connection = "c1", model = "qwen3:14b", prompt = "Summarise {{input.text}} for {{run.input}}", system = "Be {{input.tone}}",
            temperature = "0.3", maxTokens = 100, think = "off", timeoutSeconds = 9,
        }, JObject.Parse("{\"text\":\"the news\",\"tone\":\"brief\"}"), "me"), default)).Output;

        await _client.Received(1).ChatAsync(Arg.Is<Connection>(c => c.Id == "c1"),
            Arg.Is<OllamaChatRequest>(q => q.Model == "qwen3:14b" && q.Prompt == "Summarise the news for me" && q.System == "Be brief" && q.Temperature == 0.3 && q.MaxTokens == 100 && q.Think == false && !q.JsonMode),
            TimeSpan.FromSeconds(9), Arg.Any<CancellationToken>(), Arg.Any<Action<string, string>?>());
        Assert.That((string?)r["text"], Is.EqualTo("Hello"));
        Assert.That((int)r["promptTokens"]!, Is.EqualTo(5));
        Assert.That((double)r["totalDurationMs"]!, Is.EqualTo(120.5));
        Assert.That(r["json"], Is.Null);
    }

    [Test]
    public async Task NumCtx_IsPassedOn_AndEmptyMeansTheServerDefault()
    {
        await _sut.ExecuteAsync(Ctx(new { connection = "c1", model = "m", prompt = "p", numCtx = "16384" }), default);
        await _client.Received(1).ChatAsync(Arg.Any<Connection>(), Arg.Is<OllamaChatRequest>(q => q.NumCtx == 16384), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>(), Arg.Any<Action<string, string>?>());
        _client.ClearReceivedCalls();
        await _sut.ExecuteAsync(Ctx(new { connection = "c1", model = "m", prompt = "p", numCtx = "" }), default);
        await _client.Received(1).ChatAsync(Arg.Any<Connection>(), Arg.Is<OllamaChatRequest>(q => q.NumCtx == null), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>(), Arg.Any<Action<string, string>?>());
    }

    [TestCase(100)]
    [TestCase(2000000)]
    [TestCase(4096.5)]
    public void NumCtx_OutOfRangeOrFractional_IsRefusedWithAReason(double value)
    {
        var ex = Assert.ThrowsAsync<ArgumentException>(() => _sut.ExecuteAsync(Ctx(new { connection = "c1", model = "m", prompt = "p", numCtx = value }), default));
        Assert.That(ex!.Message, Does.Contain("Context size"));
    }

    [Test]
    public async Task NumCtx_ReachesTheToolLoop_Too()
    {
        var root = Path.GetTempPath();
        await _sut.ExecuteAsync(Ctx(new { connection = "c1", model = "m", prompt = "p", tools = new[] { "read_file" }, toolRoot = root, numCtx = 32768 }), default);
        await _loop.Received(1).RunAsync(Arg.Is<ToolLoopRequest>(q => q.NumCtx == 32768), Arg.Any<Action<JToken>?>(), Arg.Any<CancellationToken>());
    }

    [TestCase("on", true)]
    [TestCase("off", false)]
    [TestCase("auto", null)]
    [TestCase("", null)]
    public async Task Think_MapsToNullableBool(string think, bool? expected)
    {
        await _sut.ExecuteAsync(Ctx(new { connection = "c1", model = "m", prompt = "p", think }), default);
        await _client.Received(1).ChatAsync(Arg.Any<Connection>(), Arg.Is<OllamaChatRequest>(q => q.Think == expected), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>(), Arg.Any<Action<string, string>?>());
    }

    [Test]
    public async Task OptionalNumbersLeftEmpty_AreNotSent()
    {
        await _sut.ExecuteAsync(Ctx(new { connection = "c1", model = "m", prompt = "p", temperature = "", maxTokens = (string?)null }), default);
        await _client.Received(1).ChatAsync(Arg.Any<Connection>(), Arg.Is<OllamaChatRequest>(q => q.Temperature == null && q.MaxTokens == null), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>(), Arg.Any<Action<string, string>?>());
    }

    [Test]
    public async Task JsonMode_ParsesTheAnswer_AndFailsClearlyWhenItIsNotJson()
    {
        _client.ChatAsync(default!, default!, default, default).ReturnsForAnyArgs(Task.FromResult(new OllamaChatResult("{\"a\":1}", null, "m", true, null, null, null, null)));
        var r = (await _sut.ExecuteAsync(Ctx(new { connection = "c1", model = "m", prompt = "p", jsonMode = true }), default)).Output;
        Assert.That((int)r["json"]!["a"]!, Is.EqualTo(1));

        _client.ChatAsync(default!, default!, default, default).ReturnsForAnyArgs(Task.FromResult(new OllamaChatResult("sorry, no", null, "m", true, null, null, null, null)));
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ExecuteAsync(Ctx(new { connection = "c1", model = "m", prompt = "p", jsonMode = true }), default));
        Assert.That(ex!.Message, Does.Contain("valid JSON"));
    }

    [Test]
    public async Task Thinking_IsIncludedWhenTheModelReturnsIt()
    {
        _client.ChatAsync(default!, default!, default, default).ReturnsForAnyArgs(Task.FromResult(new OllamaChatResult("a", "step by step", "m", true, null, null, null, null)));
        var r = (await _sut.ExecuteAsync(Ctx(new { connection = "c1", model = "m", prompt = "p" }), default)).Output;
        Assert.That((string?)r["thinking"], Is.EqualTo("step by step"));
    }

    [Test]
    public void MissingConnection_IsAConfigErrorThatSaysSo()
    {
        _connections.GetForUse("gone").Returns(_ => throw new KeyNotFoundException());
        var ex = Assert.ThrowsAsync<ArgumentException>(() => _sut.ExecuteAsync(Ctx(new { connection = "gone", model = "m", prompt = "p" }), default));
        Assert.That(ex!.Message, Does.Contain("no longer exists"));
    }

    [TestCase("{\"model\":\"m\",\"prompt\":\"p\"}")]                                               // no connection
    [TestCase("{\"connection\":\"c1\",\"prompt\":\"p\"}")]                                          // no model
    [TestCase("{\"connection\":\"c1\",\"model\":\"m\",\"prompt\":\"  \"}")]                    // empty prompt
    [TestCase("{\"connection\":\"c1\",\"model\":\"m\",\"prompt\":\"p\",\"temperature\":5}")]
    [TestCase("{\"connection\":\"c1\",\"model\":\"m\",\"prompt\":\"p\",\"temperature\":\"hot\"}")]
    [TestCase("{\"connection\":\"c1\",\"model\":\"m\",\"prompt\":\"p\",\"maxTokens\":0}")]
    [TestCase("{\"connection\":\"c1\",\"model\":\"m\",\"prompt\":\"p\",\"timeoutSeconds\":0}")]
    [TestCase("{\"connection\":\"c1\",\"model\":\"m\",\"prompt\":\"p\",\"think\":\"maybe\"}")]
    public void BadConfig_ThrowsArgumentException_AndCallsNothing(string p)
    {
        Assert.ThrowsAsync<ArgumentException>(() => _sut.ExecuteAsync(new CardExecutionContext(JObject.Parse(p), null, null), default));
        _client.DidNotReceiveWithAnyArgs().ChatAsync(default!, default!, default, default);
    }

    [Test]
    public void ServerFailure_PassesTheReadableMessageThrough()
    {
        _client.ChatAsync(default!, default!, default, default).ReturnsForAnyArgs<Task<OllamaChatResult>>(_ => throw new InvalidOperationException("Could not reach Ollama at http://x"));
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ExecuteAsync(Ctx(new { connection = "c1", model = "m", prompt = "p" }), default));
        Assert.That(ex!.Message, Does.Contain("Could not reach Ollama"));
    }

    [Test]
    public async Task WhileTheModelWrites_TheCardReportsWhatItHasSoFar_ForTheLiveView()
    {
        var reports = new List<JToken>();
        _client.ChatAsync(default!, default!, default, default, default).ReturnsForAnyArgs(ci =>
        {
            var onDelta = ci.Arg<Action<string, string>?>()!;
            onDelta("", "Hel");
            onDelta("Let me think", "Hello");
            onDelta("Let me think. Greet.", "Hello there");
            return Task.FromResult(new OllamaChatResult("Hello there", "Let me think. Greet.", "m", true, "stop", 1, 2, 3));
        });
        var context = new CardExecutionContext(JObject.Parse(Json(new { connection = "c1", model = "m", prompt = "p" })), null, null, Progress: reports.Add);

        var result = (await _sut.ExecuteAsync(context, default)).Output;

        Assert.That(reports.Select(r => (string?)r["text"]), Is.EqualTo(new[] { "Hel", "Hello", "Hello there" }));
        Assert.That(reports[0]["thinking"], Is.Null, "no thinking field until the model starts thinking");
        Assert.That((string?)reports[1]["thinking"], Is.EqualTo("Let me think"));
        Assert.That((string?)result["thinking"], Is.EqualTo("Let me think. Greet."), "the final output keeps the thinking");
    }

    [Test]
    public async Task WithoutAProgressCallback_TheCardStillWorks()
    {
        _client.ChatAsync(default!, default!, default, default, default).ReturnsForAnyArgs(ci => { ci.Arg<Action<string, string>?>()?.Invoke("", "x"); return Task.FromResult(new OllamaChatResult("x", null, "m", true, null, null, null, null)); });
        var r = (await _sut.ExecuteAsync(Ctx(new { connection = "c1", model = "m", prompt = "p" }), default)).Output;
        Assert.That((string?)r["text"], Is.EqualTo("x"));
    }

    [Test]
    public void Info_DescribesAConnectionParamAndADynamicModelSelect()
    {
        var info = _sut.Info;
        Assert.That(info.Category, Is.EqualTo(CardCategory.Ai));
        var conn = info.Params.Single(p => p.Key == "connection");
        Assert.That(conn.Kind, Is.EqualTo(ParamKind.Connection));
        Assert.That(conn.Provider, Is.EqualTo(ConnectionProvider.Ollama));
        var model = info.Params.Single(p => p.Key == "model");
        Assert.That(model.OptionsSource, Is.EqualTo("ollama.models"));
        Assert.That(model.OptionsDependsOn, Is.EqualTo("connection"));
    }

    // ---- tools
    private string _tmp = null!;
    private string TmpRoot() => _tmp ??= Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tammoa-card-" + Guid.NewGuid().ToString("N"))).FullName;

    [TearDown]
    public void CleanTmp() { if (_tmp is not null && Directory.Exists(_tmp)) Directory.Delete(_tmp, true); _tmp = null!; }

    [Test]
    public async Task NoToolsSelected_BehavesAsBefore_TheLoopIsNotUsed()
    {
        await _sut.ExecuteAsync(Ctx(new { connection = "c1", model = "m", prompt = "p", tools = new string[0], toolRoot = "", maxSteps = 99 }), default);
        await _client.ReceivedWithAnyArgs(1).ChatAsync(default!, default!, default, default, default);
        await _loop.DidNotReceiveWithAnyArgs().RunAsync(default!, default, default);
    }

    [Test]
    public async Task ToolsSelected_GoThroughTheLoop_WithTheRulesFromTheParams()
    {
        _loop.RunAsync(default!, default, default).ReturnsForAnyArgs(new JObject { ["text"] = "done", ["toolCalls"] = new JArray() });
        var root = TmpRoot();
        var r = (await _sut.ExecuteAsync(Ctx(new
        {
            connection = "c1", model = "qwen3:14b", prompt = "go", tools = new[] { "read_file", "http_request" }, toolRoot = root,
            allowedHosts = "api.example.com, *.docs.test", maxSteps = 5, approverModel = "small", timeoutSeconds = 7, think = "off",
        }), default)).Output;
        Assert.That((string?)r["text"], Is.EqualTo("done"));
        await _client.DidNotReceiveWithAnyArgs().ChatAsync(default!, default!, default, default, default);
        await _loop.Received(1).RunAsync(Arg.Is<ToolLoopRequest>(q =>
            q.Model == "qwen3:14b" && q.Prompt == "go" && q.MaxSteps == 5 && q.ApproverModel == "small" && q.Think == false && q.Timeout == TimeSpan.FromSeconds(7)
            && q.ToolIds.SequenceEqual(new[] { "read_file", "http_request" }) && q.Context.AllowedHosts.SequenceEqual(new[] { "api.example.com", "*.docs.test" })
            && q.Context.Root != null && q.Context.Root.EndsWith(Path.GetFileName(root))), Arg.Any<Action<JToken>?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ApproverPrompt_IsPassedThrough_TrimmedAndEmptyMeansNull()
    {
        _loop.RunAsync(default!, default, default).ReturnsForAnyArgs(new JObject());
        var root = TmpRoot();
        await _sut.ExecuteAsync(Ctx(new { connection = "c1", model = "m", prompt = "p", tools = new[] { "read_file" }, toolRoot = root, approverModel = "small", approverPrompt = "  Only .txt.  " }), default);
        await _sut.ExecuteAsync(Ctx(new { connection = "c1", model = "m", prompt = "p", tools = new[] { "read_file" }, toolRoot = root, approverModel = "small", approverPrompt = "  " }), default);
        await _sut.ExecuteAsync(Ctx(new { connection = "c1", model = "m", prompt = "p", tools = new[] { "read_file" }, toolRoot = root }), default);
        var seen = _loop.ReceivedCalls().Select(c => ((ToolLoopRequest)c.GetArguments()[0]!).ApproverInstructions).ToList();
        Assert.That(seen, Is.EqualTo(new string?[] { "Only .txt.", null, null }));
    }

    [Test]
    public void ApproverPromptParam_IsAnOptionalTextarea_ShowingTheBuiltInTextAsItsPlaceholder()
    {
        var p = _sut.Info.Params.Single(x => x.Key == "approverPrompt");
        Assert.That(p.Kind, Is.EqualTo(ParamKind.Textarea));
        Assert.That(p.Required, Is.False);
        Assert.That(p.Placeholder, Is.EqualTo(ToolLoop.DefaultApproverInstructions));
        Assert.That(p.Help, Does.Contain("Empty"));
    }

    [Test]
    public async Task EmptyApprover_MeansNoApproval_AndTheRootIsNotNeededForHttpOnly()
    {
        _loop.RunAsync(default!, default, default).ReturnsForAnyArgs(new JObject());
        await _sut.ExecuteAsync(Ctx(new { connection = "c1", model = "m", prompt = "p", tools = new[] { "http_request" }, allowedHosts = "a.test", approverModel = " " }), default);
        await _loop.Received(1).RunAsync(Arg.Is<ToolLoopRequest>(q => q.ApproverModel == null && q.Context.Root == null && q.MaxSteps == 8), Arg.Any<Action<JToken>?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void ToolsConfig_IsValidated()
    {
        var root = TmpRoot();
        void Bad(object p, string contains)
        {
            var ex = Assert.ThrowsAsync<ArgumentException>(() => _sut.ExecuteAsync(Ctx(p), default));
            Assert.That(ex!.Message, Does.Contain(contains));
        }
        Bad(new { connection = "c1", model = "m", prompt = "p", tools = new[] { "write_file" } }, "tools folder");
        Bad(new { connection = "c1", model = "m", prompt = "p", tools = new[] { "run_script" }, toolRoot = "relative/dir" }, "absolute");
        Bad(new { connection = "c1", model = "m", prompt = "p", tools = new[] { "read_file" }, toolRoot = Path.Combine(root, "missing") }, "does not exist");
        Bad(new { connection = "c1", model = "m", prompt = "p", tools = new[] { "http_request" } }, "allowed host");
        Bad(new { connection = "c1", model = "m", prompt = "p", tools = new[] { "read_file" }, toolRoot = root, maxSteps = 0 }, "Max steps");
        Bad(new { connection = "c1", model = "m", prompt = "p", tools = new[] { "read_file" }, toolRoot = root, maxSteps = 31 }, "Max steps");
        Bad(new { connection = "c1", model = "m", prompt = "p", tools = new[] { "format_disk" }, toolRoot = root }, "Unknown tool");
        Bad(new { connection = "c1", model = "m", prompt = "p", tools = new[] { "read_file" }, toolRoot = root, jsonMode = true }, "JSON");
    }

    [Test]
    public void ToolsParam_IsAMultiSelect_WithTheFiveToolsAndNothingSelectedByDefault()
    {
        var p = _sut.Info.Params.Single(x => x.Key == "tools");
        Assert.That(p.Kind, Is.EqualTo(ParamKind.MultiSelect));
        Assert.That(p.Options!.Select(o => o.Value), Is.EqualTo(new[] { "read_file", "write_file", "http_request", "run_script", "check_page" }));
        Assert.That(p.DefaultValue, Is.Null);
        Assert.That(_sut.Info.Params.Single(x => x.Key == "approverModel").OptionsDependsOn, Is.EqualTo("connection"));
    }
}
