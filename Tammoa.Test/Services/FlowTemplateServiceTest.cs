using Newtonsoft.Json.Linq;
using NSubstitute;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Repositories;
using Tammoa.Services;
using Tammoa.Services.Cards;

namespace Tammoa.Test.Services;

public class FlowTemplateServiceTest
{
    private static readonly TemplateEngine T = new();
    private IFlowRepository _repo = null!;
    private IConnectionService _connections = null!;
    private FlowService _flows = null!;
    private FlowTemplateService _sut = null!;
    private string _dir = null!;
    private ICardRegistry _registry = null!;

    private static Connection Ollama() => new("c1", "Home", ConnectionProvider.Ollama, "http://x", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tammoa-tpl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        // the REAL cards and the REAL validator: a template that names a card or a setting that does not exist must fail here
        var data = new DataService(new DataRepository(_dir), Substitute.For<IFlowRepository>(), Substitute.For<IRunEventBus>());
        _connections = Substitute.For<IConnectionService>();
        _connections.GetForUse("c1").Returns(Ollama());
        _registry = new CardRegistry(
        [
            new ManualTriggerCard(), new SetValueCard(T), new ScriptCard(T, new ProcessRunner()), new IfCard(T), new WaitCard(),
            new HttpRequestCard(T, Substitute.For<IHttpRequestRunner>()), new JsonCard(T), new MergeCard(), new ReadFileCard(T), new WriteFileCard(T),
            new OllamaCard(T, _connections, Substitute.For<IOllamaClient>(), Substitute.For<IToolLoop>()), new DataStoreCard(T, data),
        ]);
        _repo = Substitute.For<IFlowRepository>();
        _flows = new FlowService(_repo, TimeProvider.System, new FlowDefinitionValidator(_registry), data);
        _sut = new FlowTemplateService(_flows, _connections);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, true);

    private static IEnumerable<string> Ids => FlowTemplateService.Templates.Select(t => t.Id);

    [Test]
    public void ThereAreAFewTemplates_OneWithoutAI_AndEachHasAnIcon()
    {
        var list = _sut.List();
        Assert.That(list.Count, Is.GreaterThanOrEqualTo(5));
        Assert.That(list.Select(t => t.Id), Is.Unique);
        Assert.That(list.Count(t => !t.NeedsAi), Is.GreaterThanOrEqualTo(3), "a new user can start without any AI");
        Assert.That(list.All(t => t.Icon.Length > 0 && t.Name.Length > 0 && t.Description.Length > 0 && t.CardCount >= 2), Is.True);
        Assert.That(list.Where(t => t.NeedsAi).All(t => t.Needs.Contains("Ollama")), Is.True, "what is needed is said up front");
    }

    [TestCaseSource(nameof(Ids))]
    public void EveryTemplate_MakesAValidFlow_ThatIsSavedDisabled(string id)
    {
        Flow? saved = null;
        _repo.When(r => r.Insert(Arg.Any<Flow>())).Do(ci => saved = ci.Arg<Flow>());
        var flow = _sut.Create(id, new CreateFromTemplateRequest(null, null));
        Assert.That(saved, Is.Not.Null);
        Assert.That(flow.Enabled, Is.False);
        Assert.That(flow.Name, Is.EqualTo(FlowTemplateService.Templates.Single(t => t.Id == id).Name));
    }

    [TestCaseSource(nameof(Ids))]
    public void EveryTemplate_UsesOnlyRealCardSettings(string id)
    {
        var definition = JObject.Parse(FlowTemplateService.Templates.Single(t => t.Id == id).Definition);
        foreach (var node in ((JArray)definition["nodes"]!).OfType<JObject>())
        {
            var info = _registry.Find((string)node["type"]!)!.Info;
            foreach (var key in ((JObject)node["params"]!).Properties().Select(p => p.Name))
                Assert.That(info.Params.Any(p => p.Key == key), Is.True, $"{id}: card '{node["id"]}' ({info.Type}) has no setting '{key}'");
        }
    }

    [TestCaseSource(nameof(Ids))]
    public void EveryTemplate_HasExactlyOneTrigger_AndCardsLeftToRight(string id)
    {
        var nodes = ((JArray)JObject.Parse(FlowTemplateService.Templates.Single(t => t.Id == id).Definition)["nodes"]!).OfType<JObject>().ToList();
        Assert.That(nodes.Count(n => (string?)n["type"] == "trigger.manual"), Is.EqualTo(1));
        Assert.That(nodes.Select(n => ((int)n["position"]!["x"]!, (int)n["position"]!["y"]!)), Is.Unique, "no two cards on top of each other");
    }

    [Test]
    public void AnAiTemplate_GetsTheChosenConnectionAndModel_OnEveryAiCard()
    {
        Flow? saved = null;
        _repo.When(r => r.Insert(Arg.Any<Flow>())).Do(ci => saved = ci.Arg<Flow>());
        _sut.Create("summarize-file", new CreateFromTemplateRequest("c1", " qwen3:14b "));
        var ai = ((JArray)saved!.Definition["nodes"]!).OfType<JObject>().Single(n => (string?)n["type"] == "ai.ollama");
        Assert.That(((string?)ai["params"]!["connection"], (string?)ai["params"]!["model"]), Is.EqualTo(("c1", "qwen3:14b")));
    }

    [Test]
    public void ATemplateWithoutAConnection_LeavesTheAiCardToBeFilledIn()
    {
        Flow? saved = null;
        _repo.When(r => r.Insert(Arg.Any<Flow>())).Do(ci => saved = ci.Arg<Flow>());
        _sut.Create("summarize-text", new CreateFromTemplateRequest(null, null));
        var ai = ((JArray)saved!.Definition["nodes"]!).OfType<JObject>().Single(n => (string?)n["type"] == "ai.ollama");
        Assert.That((string?)ai["params"]!["connection"], Is.Empty);
    }

    [Test]
    public void AConnectionGivenToATemplateWithoutAi_IsIgnored_AndNeverLookedUp()
    {
        _sut.Create("hello", new CreateFromTemplateRequest("whatever", "m"));
        _connections.DidNotReceiveWithAnyArgs().GetForUse(default!);
    }

    [Test]
    public void AConnectionThatIsGoneOrNotOllama_IsRefusedWithAReason()
    {
        _connections.GetForUse("gone").Returns(_ => throw new KeyNotFoundException());
        Assert.That(Assert.Throws<ArgumentException>(() => _sut.Create("summarize-text", new CreateFromTemplateRequest("gone", null)))!.Message, Does.Contain("no longer exists"));
        _connections.GetForUse("other").Returns(new Connection("other", "Other", (ConnectionProvider)99, "http://x", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        Assert.That(Assert.Throws<ArgumentException>(() => _sut.Create("summarize-text", new CreateFromTemplateRequest("other", null)))!.Message, Does.Contain("not an Ollama"));
        _repo.DidNotReceiveWithAnyArgs().Insert(default!);
    }

    [Test]
    public void AnUnknownTemplate_Is404()
        => Assert.Throws<KeyNotFoundException>(() => _sut.Create("nope", new CreateFromTemplateRequest(null, null)));

    [Test]
    public void TheTemplatesAreFreshCopies_ChangingOneFlowNeverChangesTheCatalog()
    {
        Flow? first = null;
        _repo.When(r => r.Insert(Arg.Any<Flow>())).Do(ci => first ??= ci.Arg<Flow>());
        _sut.Create("hello", new CreateFromTemplateRequest(null, null));
        first!.Definition["nodes"]![0]!["name"] = "Changed";
        Assert.That(JObject.Parse(FlowTemplateService.Templates.Single(t => t.Id == "hello").Definition)["nodes"]![0]!["name"]!.ToString(), Is.EqualTo("Start"));
    }

    // ---- the templates that need no AI really RUN and say what their description promises
    private async Task<FlowRun> RunIt(string id, JToken? input)
    {
        var def = JObject.Parse(FlowTemplateService.Templates.Single(t => t.Id == id).Definition);
        var data = Substitute.For<IDataService>();
        data.LoadContext(default!, default!).ReturnsForAnyArgs(new JObject());
        var executor = new FlowExecutor(_registry, TimeProvider.System, data);
        var nodes = ((JArray)def["nodes"]!).Select(n => new NodeRun((string)n["id"]!, NodeRunStatus.Pending)).ToList();
        return await executor.ExecuteAsync(new FlowRun("r", "f", id, RunStatus.Running, DateTimeOffset.UtcNow, null, input, null, nodes), def, input, _ => { }, default);
    }

    private static NodeRun N(FlowRun r, string id) => r.Nodes.Single(n => n.NodeId == id);

    [Test]
    public async Task Hello_GreetsTheRunInput()
    {
        if (!Found("node")) Assert.Ignore("node is not installed here");
        var run = await RunIt("hello", "Sam");
        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded), N(run, "greet").Error);
        Assert.That((string?)N(run, "greet").Output!["json"]!["message"], Is.EqualTo("Hello, Sam!"));
        Assert.That((string?)(await RunIt("hello", null)).Nodes.Single(n => n.NodeId == "greet").Output!["json"]!["message"], Is.EqualTo("Hello, world!"), "no run input: the example still works");
    }

    [TestCase("oh yes please", "yes", "no")]
    [TestCase("nope", "no", "yes")]
    public async Task Branch_TakesTheRightPath(string input, string taken, string skipped)
    {
        var run = await RunIt("branch", input);
        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded));
        Assert.That(N(run, taken).Status, Is.EqualTo(NodeRunStatus.Succeeded));
        Assert.That(N(run, skipped).Status, Is.EqualTo(NodeRunStatus.Skipped));
        if (taken == "no") Assert.That((string?)N(run, "no").Output!["answer"], Is.EqualTo("No yes found in: nope"));
    }

    private static bool Found(string exe)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/sh", ["-c", $"command -v {exe}"]) { RedirectStandardOutput = true, UseShellExecute = false })!;
        p.WaitForExit(3000);
        return p.ExitCode == 0;
    }
}
