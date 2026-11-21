using Newtonsoft.Json.Linq;
using NSubstitute;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Services;
using Tammoa.Services.Cards;

namespace Tammoa.Test.Services.Cards;

public class MoreCardsTest
{
    private static readonly TemplateEngine T = new();
    private static CardExecutionContext Ctx(string p, JToken? input = null, JToken? run = null) => new(JObject.Parse(p), input, run);

    // ---- IF
    private static Task<CardResult> If(string p, JToken? input = null) => new IfCard(T).ExecuteAsync(Ctx(p, input), default);

    [TestCase("equals", "a", "a", "true")]
    [TestCase("equals", "a", "b", "false")]
    [TestCase("notEquals", "a", "b", "true")]
    [TestCase("contains", "hello world", "lo w", "true")]
    [TestCase("notContains", "hello", "x", "true")]
    [TestCase("startsWith", "hello", "he", "true")]
    [TestCase("endsWith", "hello", "lo", "true")]
    [TestCase("greaterThan", "10", "9", "true")]
    [TestCase("greaterThan", "9", "10", "false")]
    [TestCase("lessThan", "1.5", "2", "true")]
    [TestCase("isEmpty", "  ", "", "true")]
    [TestCase("isEmpty", "x", "", "false")]
    [TestCase("isNotEmpty", "x", "", "true")]
    [TestCase("matches", "order-42", "^order-\\d+$", "true")]
    [TestCase("matches", "order-x", "^order-\\d+$", "false")]
    public async Task If_Operators(string op, string left, string right, string branch)
    {
        var r = await If(JObject.FromObject(new { value1 = left, @operator = op, value2 = right }).ToString());
        Assert.That(r.Branch, Is.EqualTo(branch));
    }

    [Test]
    public async Task If_UsesTemplatesAndPassesInputThrough()
    {
        var input = JObject.Parse("{\"json\":{\"status\":\"ok\"}}");
        var r = await If("{\"value1\":\"{{input.json.status}}\",\"operator\":\"equals\",\"value2\":\"ok\"}", input);
        Assert.That(r.Branch, Is.EqualTo("true"));
        Assert.That(JToken.DeepEquals(r.Output, input), Is.True);
        Assert.That(ReferenceEquals(r.Output, input), Is.False);
    }

    [Test]
    public async Task If_DefaultOperatorIsEquals()
    {
        var r = await If("{\"value1\":\"a\",\"value2\":\"a\"}");
        Assert.That(r.Branch, Is.EqualTo("true"));
    }

    [TestCase("{\"value1\":\"abc\",\"operator\":\"greaterThan\",\"value2\":\"1\"}")]
    [TestCase("{\"value1\":\"1\",\"operator\":\"lessThan\",\"value2\":\"x\"}")]
    [TestCase("{\"value1\":\"a\",\"operator\":\"matches\",\"value2\":\"(\"}")]
    [TestCase("{\"value1\":\"a\",\"operator\":\"nope\",\"value2\":\"a\"}")]
    public void If_BadConfig_ThrowsArgumentException(string p) => Assert.ThrowsAsync<ArgumentException>(() => If(p));

    [Test]
    public void If_HasTrueAndFalseOutputs() =>
        Assert.That(new IfCard(T).Info.Outputs.Select(o => o.Key), Is.EqualTo(new[] { "true", "false" }));

    // ---- Wait
    [Test]
    public async Task Wait_PausesThenPassesInputThrough()
    {
        var input = JObject.Parse("{\"a\":1}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = await new WaitCard().ExecuteAsync(Ctx("{\"seconds\":0.15}", input), default);
        Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(120));
        Assert.That(JToken.DeepEquals(r.Output, input), Is.True);
    }

    [Test]
    public void Wait_Cancel_StopsTheWait()
    {
        using var cts = new CancellationTokenSource(100);
        Assert.CatchAsync<OperationCanceledException>(() => new WaitCard().ExecuteAsync(Ctx("{\"seconds\":30}"), cts.Token));
    }

    [TestCase("{\"seconds\":-1}")]
    [TestCase("{\"seconds\":3601}")]
    public void Wait_OutOfRange_Throws(string p) => Assert.ThrowsAsync<ArgumentException>(() => new WaitCard().ExecuteAsync(Ctx(p), default));

    // ---- JSON
    [Test]
    public async Task Json_Parse_ObjectStaysObject_OthersAreWrapped()
    {
        var card = new JsonCard(T);
        var obj = await card.ExecuteAsync(Ctx("{\"mode\":\"parse\",\"source\":\"{{input.stdout}}\"}", JObject.Parse("{\"stdout\":\"{\\\"a\\\":1}\"}")), default);
        Assert.That((int)obj.Output["a"]!, Is.EqualTo(1));
        var arr = await card.ExecuteAsync(Ctx("{\"source\":\"[1,2]\"}"), default);
        Assert.That(arr.Output["value"], Is.InstanceOf<JArray>());
    }

    [Test]
    public void Json_Parse_Invalid_ThrowsArgumentException() =>
        Assert.ThrowsAsync<ArgumentException>(() => new JsonCard(T).ExecuteAsync(Ctx("{\"mode\":\"parse\",\"source\":\"{nope\"}"), default));

    [Test]
    public async Task Json_Stringify_KeepsTypesAndCanPrettyPrint()
    {
        var card = new JsonCard(T);
        var input = JObject.Parse("{\"a\":{\"b\":[1,2]}}");
        var compact = await card.ExecuteAsync(Ctx("{\"mode\":\"stringify\",\"source\":\"{{input.a}}\"}", input), default);
        Assert.That((string?)compact.Output["text"], Is.EqualTo("{\"b\":[1,2]}"));
        var pretty = await card.ExecuteAsync(Ctx("{\"mode\":\"stringify\",\"source\":\"{{input.a}}\",\"pretty\":true}", input), default);
        Assert.That((string?)pretty.Output["text"], Does.Contain("\n"));
    }

    [TestCase("{\"mode\":\"parse\",\"source\":\"\"}")]
    [TestCase("{\"mode\":\"zip\",\"source\":\"x\"}")]
    public void Json_BadConfig_Throws(string p) => Assert.ThrowsAsync<ArgumentException>(() => new JsonCard(T).ExecuteAsync(Ctx(p), default));

    // ---- Merge
    [Test]
    public async Task Merge_CombinesObjects_LaterWins()
    {
        var input = JArray.Parse("[{\"a\":1,\"b\":1},{\"b\":2,\"c\":3}]");
        var r = (await new MergeCard().ExecuteAsync(Ctx("{}", input), default)).Output;
        Assert.That(r.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("{\"a\":1,\"b\":2,\"c\":3}"));
    }

    [Test]
    public async Task Merge_SingleObject_PassesThrough()
    {
        var r = (await new MergeCard().ExecuteAsync(Ctx("{}", JObject.Parse("{\"a\":1}")), default)).Output;
        Assert.That((int)r["a"]!, Is.EqualTo(1));
    }

    [TestCase("[1,2]")]
    [TestCase("\"text\"")]
    public void Merge_NotObjects_Throws(string input) =>
        Assert.ThrowsAsync<ArgumentException>(() => new MergeCard().ExecuteAsync(Ctx("{}", JToken.Parse(input)), default));

    [Test]
    public void Merge_NoInput_Throws() => Assert.ThrowsAsync<ArgumentException>(() => new MergeCard().ExecuteAsync(Ctx("{}"), default));

    // ---- files
    private string _dir = null!;
    [SetUp] public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "tammoa-files-" + Guid.NewGuid().ToString("N"));
    [TearDown] public void TearDown() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static string Json(object o) => JObject.FromObject(o).ToString();

    [Test]
    public async Task WriteThenRead_RoundTripsAndCreatesFolders()
    {
        var path = Path.Combine(_dir, "deep", "x.txt");
        var w = (await new WriteFileCard(T).ExecuteAsync(Ctx(Json(new { path, content = "héllo {{input.n}}" }), JObject.Parse("{\"n\":5}")), default)).Output;
        Assert.That((string?)w["path"], Is.EqualTo(path));
        Assert.That((int)w["bytes"]!, Is.EqualTo(System.Text.Encoding.UTF8.GetByteCount("héllo 5")));
        var r = (await new ReadFileCard(T).ExecuteAsync(Ctx(Json(new { path })), default)).Output;
        Assert.That((string?)r["text"], Is.EqualTo("héllo 5"));
        Assert.That((long)r["size"]!, Is.GreaterThan(0));
    }

    [Test]
    public async Task Write_Append_AddsToTheEnd()
    {
        var path = Path.Combine(_dir, "log.txt");
        var card = new WriteFileCard(T);
        await card.ExecuteAsync(Ctx(Json(new { path, content = "a" })), default);
        await card.ExecuteAsync(Ctx(Json(new { path, content = "b", mode = "append" })), default);
        Assert.That(File.ReadAllText(path), Is.EqualTo("ab"));
    }

    [Test]
    public void Write_MissingFolderAndNoCreate_Throws() =>
        Assert.ThrowsAsync<InvalidOperationException>(() => new WriteFileCard(T).ExecuteAsync(Ctx(Json(new { path = Path.Combine(_dir, "no", "x.txt"), content = "a", createDirectories = false })), default));

    [TestCase("relative/path.txt")]
    [TestCase("")]
    public void FileCards_RelativeOrEmptyPath_ThrowArgumentException(string path)
    {
        Assert.ThrowsAsync<ArgumentException>(() => new ReadFileCard(T).ExecuteAsync(Ctx(Json(new { path })), default));
        Assert.ThrowsAsync<ArgumentException>(() => new WriteFileCard(T).ExecuteAsync(Ctx(Json(new { path, content = "x" })), default));
    }

    [Test]
    public async Task Read_MissingFile_GivesEmptyText_AndCreatesNothing_ByDefault()
    {
        var path = Path.Combine(_dir, "deep", "nope.txt");
        var r = (await new ReadFileCard(T).ExecuteAsync(Ctx(Json(new { path })), default)).Output;
        Assert.That((string?)r["text"], Is.EqualTo(""));
        Assert.That((long)r["size"]!, Is.EqualTo(0));
        Assert.That((bool)r["exists"]!, Is.False);
        Assert.That((string?)r["path"], Is.EqualTo(path));
        Assert.That(Directory.Exists(Path.Combine(_dir, "deep")), Is.False);
    }

    [Test]
    public async Task Read_MissingFile_WithTheSwitchOff_IsTheSameAsDefault()
    {
        var path = Path.Combine(_dir, "off.txt");
        var r = (await new ReadFileCard(T).ExecuteAsync(Ctx(Json(new { path, createIfMissing = false })), default)).Output;
        Assert.That((string?)r["text"], Is.EqualTo(""));
        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public async Task Read_MissingFile_WithTheSwitchOn_CreatesTheEmptyFileAndFolders()
    {
        var path = Path.Combine(_dir, "a", "b", "made.txt");
        var card = new ReadFileCard(T);
        var r = (await card.ExecuteAsync(Ctx(Json(new { path, createIfMissing = true })), default)).Output;
        Assert.That(File.Exists(path), Is.True);
        Assert.That(new FileInfo(path).Length, Is.EqualTo(0));
        Assert.That((string?)r["text"], Is.EqualTo(""));
        Assert.That((bool)r["exists"]!, Is.False); // it did not exist when read
        File.WriteAllText(path, "later");
        var again = (await card.ExecuteAsync(Ctx(Json(new { path, createIfMissing = true })), default)).Output;
        Assert.That((string?)again["text"], Is.EqualTo("later")); // an existing file is never touched
        Assert.That((bool)again["exists"]!, Is.True);
    }

    [Test]
    public async Task Read_ExistingFile_ReportsItExists()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "here.txt");
        File.WriteAllText(path, "hi");
        var r = (await new ReadFileCard(T).ExecuteAsync(Ctx(Json(new { path })), default)).Output;
        Assert.That((bool)r["exists"]!, Is.True);
    }

    [Test]
    public void Read_APathThatIsAFolder_Throws()
    {
        Directory.CreateDirectory(_dir);
        Assert.ThrowsAsync<InvalidOperationException>(() => new ReadFileCard(T).ExecuteAsync(Ctx(Json(new { path = _dir, createIfMissing = true })), default));
    }

    [Test]
    public void Read_TemplatedMissingPath_StillNeedsAnAbsolutePath() =>
        Assert.ThrowsAsync<ArgumentException>(() => new ReadFileCard(T).ExecuteAsync(Ctx(Json(new { path = "nope.txt", createIfMissing = true })), default));

    [Test]
    public void Read_OverTheSizeLimit_Throws()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "big.txt");
        File.WriteAllText(path, new string('x', 3000));
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => new ReadFileCard(T).ExecuteAsync(Ctx(Json(new { path, maxKilobytes = 1 })), default));
        Assert.That(ex!.Message, Does.Contain("limit"));
    }

    [Test]
    public void Write_UnknownMode_Throws() =>
        Assert.ThrowsAsync<ArgumentException>(() => new WriteFileCard(T).ExecuteAsync(Ctx(Json(new { path = Path.Combine(_dir, "x"), content = "a", mode = "zip" })), default));

    // ---- HTTP request card (runner substituted)
    private static (HttpRequestCard Card, IHttpRequestRunner Http) Http(HttpRequestResult? result = null)
    {
        var http = Substitute.For<IHttpRequestRunner>();
        http.SendAsync(default!, default!, default!, default, default, default)
            .ReturnsForAnyArgs(Task.FromResult(result ?? new HttpRequestResult(200, true, new Dictionary<string, string> { ["content-type"] = "application/json" }, "{\"ok\":true}", false)));
        return (new HttpRequestCard(T, http), http);
    }

    [Test]
    public async Task Http_RendersTemplatesAndParsesJsonBody()
    {
        var (card, http) = Http();
        var input = JObject.Parse("{\"id\":7,\"tok\":\"s3\"}");
        var r = (await card.ExecuteAsync(Ctx(Json(new
        {
            method = "post", url = "https://api.test/items/{{input.id}}",
            headers = "{ \"X-Token\": \"{{input.tok}}\" }", body = "{\"n\":{{input.id}}}", timeoutSeconds = 5,
        }), input), default)).Output;
        await http.Received(1).SendAsync("POST", new Uri("https://api.test/items/7"),
            Arg.Is<IReadOnlyDictionary<string, string>>(h => h["X-Token"] == "s3"), "{\"n\":7}", TimeSpan.FromSeconds(5), Arg.Any<CancellationToken>());
        Assert.That((int)r["status"]!, Is.EqualTo(200));
        Assert.That((bool?)r["json"]!["ok"], Is.True);
    }

    [Test]
    public void Http_ErrorStatus_FailsByDefault_ButNotWhenToldTo()
    {
        var (card, _) = Http(new HttpRequestResult(404, false, new Dictionary<string, string>(), "missing", false));
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => card.ExecuteAsync(Ctx("{\"url\":\"https://x.test\"}"), default));
        Assert.That(ex!.Message, Does.Contain("404").And.Contain("missing"));
        Assert.DoesNotThrowAsync(() => card.ExecuteAsync(Ctx("{\"url\":\"https://x.test\",\"failOnError\":false}"), default));
    }

    [Test]
    public async Task Http_PlainTextBody_HasNoJsonField()
    {
        var (card, _) = Http(new HttpRequestResult(200, true, new Dictionary<string, string>(), "hello", false));
        var r = (await card.ExecuteAsync(Ctx("{\"url\":\"https://x.test\"}"), default)).Output;
        Assert.That(r["json"], Is.Null);
        Assert.That((string?)r["text"], Is.EqualTo("hello"));
    }

    [TestCase("{\"url\":\"ftp://x.test\"}")]
    [TestCase("{\"url\":\"not a url\"}")]
    [TestCase("{\"url\":\"\"}")]
    [TestCase("{\"url\":\"https://x.test\",\"method\":\"TRACE\"}")]
    [TestCase("{\"url\":\"https://x.test\",\"headers\":\"[1]\"}")]
    [TestCase("{\"url\":\"https://x.test\",\"headers\":\"{bad\"}")]
    [TestCase("{\"url\":\"https://x.test\",\"timeoutSeconds\":0}")]
    public void Http_BadConfig_ThrowsArgumentExceptionAndSendsNothing(string p)
    {
        var (card, http) = Http();
        Assert.ThrowsAsync<ArgumentException>(() => card.ExecuteAsync(Ctx(p), default));
        http.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!, default, default, default);
    }

    // ---- catalogue
    [Test]
    public void EveryCardHasUniqueTypeAndOutputs()
    {
        var conn = Substitute.For<IConnectionService>();
        var ollama = Substitute.For<IOllamaClient>();
        ICardType[] cards =
        [
            new ManualTriggerCard(), new SetValueCard(T), new ScriptCard(T, Substitute.For<IProcessRunner>()), new IfCard(T), new WaitCard(),
            new HttpRequestCard(T, Substitute.For<IHttpRequestRunner>()), new JsonCard(T), new MergeCard(), new ReadFileCard(T), new WriteFileCard(T),
            new OllamaCard(T, conn, ollama),
        ];
        Assert.That(cards.Select(c => c.Info.Type), Is.Unique);
        foreach (var c in cards)
        {
            Assert.That(c.Info.Outputs, Is.Not.Empty, c.Info.Type);
            Assert.That(c.Info.Params.Select(p => p.Key), Is.Unique, c.Info.Type);
            foreach (var p in c.Info.Params.Where(p => p.Kind == ParamKind.Select && p.OptionsSource is null))
                Assert.That(p.Options, Is.Not.Null.And.Not.Empty, $"{c.Info.Type}.{p.Key} needs options");
            foreach (var p in c.Info.Params.Where(p => p.OptionsDependsOn is not null))
                Assert.That(c.Info.Params.Any(q => q.Key == p.OptionsDependsOn), Is.True, $"{c.Info.Type}.{p.Key} depends on a param that exists");
        }
    }
}
