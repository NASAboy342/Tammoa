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
        _connections.GetForUse("c1").Returns(Conn());
        _client.ChatAsync(default!, default!, default, default)
            .ReturnsForAnyArgs(Task.FromResult(new OllamaChatResult("Hello", null, "qwen3:14b", true, "stop", 5, 2, 120.5)));
        _sut = new OllamaCard(new TemplateEngine(), _connections, _client);
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
            TimeSpan.FromSeconds(9), Arg.Any<CancellationToken>());
        Assert.That((string?)r["text"], Is.EqualTo("Hello"));
        Assert.That((int)r["promptTokens"]!, Is.EqualTo(5));
        Assert.That((double)r["totalDurationMs"]!, Is.EqualTo(120.5));
        Assert.That(r["json"], Is.Null);
    }

    [TestCase("on", true)]
    [TestCase("off", false)]
    [TestCase("auto", null)]
    [TestCase("", null)]
    public async Task Think_MapsToNullableBool(string think, bool? expected)
    {
        await _sut.ExecuteAsync(Ctx(new { connection = "c1", model = "m", prompt = "p", think }), default);
        await _client.Received(1).ChatAsync(Arg.Any<Connection>(), Arg.Is<OllamaChatRequest>(q => q.Think == expected), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OptionalNumbersLeftEmpty_AreNotSent()
    {
        await _sut.ExecuteAsync(Ctx(new { connection = "c1", model = "m", prompt = "p", temperature = "", maxTokens = (string?)null }), default);
        await _client.Received(1).ChatAsync(Arg.Any<Connection>(), Arg.Is<OllamaChatRequest>(q => q.Temperature == null && q.MaxTokens == null), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
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
}
