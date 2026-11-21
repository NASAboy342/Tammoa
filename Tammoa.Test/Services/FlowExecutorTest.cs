using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Services;

namespace Tammoa.Test.Services;

public class FlowExecutorTest
{
    /// <summary>Scriptable card: records calls, returns its param "out" (or throws "fail"), can block until released.</summary>
    private sealed class FakeCard : ICardType
    {
        public FakeCard(string type, bool trigger = false) =>
            Info = new CardTypeInfo(type, type, "", trigger ? CardCategory.Trigger : CardCategory.Action, "zap", []);
        public CardTypeInfo Info { get; }
        public List<CardExecutionContext> Calls { get; } = new();
        public TaskCompletionSource? Gate { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<CardResult> ExecuteAsync(CardExecutionContext c, CancellationToken ct)
        {
            lock (Calls) Calls.Add(c);
            Started.TrySetResult();
            if (Gate is not null) await Gate.Task.WaitAsync(ct);
            if (c.Params["fail"] is not null) throw new InvalidOperationException((string)c.Params["fail"]!);
            return new CardResult(c.Params["out"]?.DeepClone() ?? new JObject { ["from"] = c.Params["name"] }, (string?)c.Params["branch"]);
        }
    }

    private sealed class Registry(params FakeCard[] cards) : ICardRegistry
    {
        public IReadOnlyList<CardTypeInfo> All() => cards.Select(c => c.Info).ToList();
        public ICardType? Find(string type) => cards.FirstOrDefault(c => c.Info.Type == type);
    }

    // NUnit shares one fixture instance across the tests of a class, so everything stateful is rebuilt in SetUp
    // (a gate left closed by one test once blocked the next).
    private FakeCard _trigger = null!;
    private FakeCard _act = null!;
    private FlowExecutor _sut = null!;
    private List<FlowRun> _snapshots = null!;

    [SetUp]
    public void SetUp()
    {
        _trigger = new FakeCard("trigger", trigger: true);
        _act = new FakeCard("act");
        _snapshots = new List<FlowRun>();
        _sut = new FlowExecutor(new Registry(_trigger, _act), TimeProvider.System);
    }

    private static JObject Def(string nodes, string edges) => JObject.Parse($"{{\"nodes\":{nodes},\"edges\":{edges}}}");

    private static string N(string id, string type = "act", string? p = null) => $"{{\"id\":\"{id}\",\"type\":\"{type}\",\"params\":{p ?? $"{{\"name\":\"{id}\"}}"}}}";
    private static string E(string s, string t) => $"{{\"source\":\"{s}\",\"target\":\"{t}\"}}";

    private async Task<FlowRun> Run(JObject def, JToken? runInput = null, CancellationToken ct = default)
    {
        var nodeIds = ((JArray)def["nodes"]!).Select(n => (string)n["id"]!);
        var initial = new FlowRun("r", "f", "Flow", RunStatus.Running, DateTimeOffset.UtcNow, null, runInput, null, nodeIds.Select(i => new NodeRun(i, NodeRunStatus.Pending)).ToList());
        lock (_snapshots) _snapshots.Clear();
        return await _sut.ExecuteAsync(initial, def, runInput, r => { lock (_snapshots) _snapshots.Add(r); }, ct);
    }

    private static NodeRunStatus S(FlowRun r, string id) => r.Nodes.Single(n => n.NodeId == id).Status;

    [Test]
    public async Task Linear_RunsInOrderAndPassesOutputAsInput()
    {
        var run = await Run(Def($"[{N("t", "trigger")},{N("a", p: "{\"out\":{\"v\":1}}")},{N("b")}]", $"[{E("t", "a")},{E("a", "b")}]"), "hi");
        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded));
        Assert.That(run.Nodes.All(n => n.Status == NodeRunStatus.Succeeded), Is.True);
        Assert.That(_trigger.Calls[0].RunInput!.ToString(), Is.EqualTo("hi"));
        Assert.That(_trigger.Calls[0].Input, Is.Null);
        Assert.That((int)_act.Calls.Single(c => (string?)c.Params["name"] == "b").Input!["v"]!, Is.EqualTo(1));
        Assert.That(run.FinishedAt, Is.Not.Null);
    }

    [Test]
    public async Task EmitsASnapshotForEveryStateChange_EndingWithTheFinishedRun()
    {
        var run = await Run(Def($"[{N("t", "trigger")},{N("a")}]", $"[{E("t", "a")}]"));
        lock (_snapshots)
        {
            Assert.That(_snapshots.Count, Is.GreaterThanOrEqualTo(5)); // t running, t done, a running, a done, final
            Assert.That(_snapshots.Any(s => S(s, "a") == NodeRunStatus.Running), Is.True);
            Assert.That(_snapshots.Last().Status, Is.EqualTo(RunStatus.Succeeded));
        }
        Assert.That(run, Is.SameAs(_snapshots.Last()));
    }

    [Test]
    public async Task EverySnapshotHasAHigherRevisionThanTheOneBefore()
    {
        var run = await Run(Def($"[{N("t", "trigger")},{N("a")},{N("b")}]", $"[{E("t", "a")},{E("t", "b")}]"));
        lock (_snapshots)
        {
            var revisions = _snapshots.Select(s => s.Revision).ToList();
            Assert.That(revisions, Is.Ordered.Ascending);
            Assert.That(revisions.Distinct().Count(), Is.EqualTo(revisions.Count), "no two snapshots share a revision");
            Assert.That(run.Revision, Is.EqualTo(revisions.Last()));
            Assert.That(revisions.First(), Is.GreaterThan(0));
        }
    }

    private static string B(string id, string branch) => N(id, "act", $"{{\"name\":\"{id}\",\"branch\":\"{branch}\"}}");
    private static string EH(string s, string handle, string t) => $"{{\"source\":\"{s}\",\"sourceHandle\":\"{handle}\",\"target\":\"{t}\"}}";

    [Test]
    public async Task Branching_OnlyTheFiredHandleRuns_TheOtherBranchIsSkipped()
    {
        var run = await Run(Def($"[{N("t", "trigger")},{B("if", "true")},{N("yes")},{N("no")}]", $"[{E("t", "if")},{EH("if", "true", "yes")},{EH("if", "false", "no")}]"));
        Assert.That(S(run, "yes"), Is.EqualTo(NodeRunStatus.Succeeded));
        Assert.That(S(run, "no"), Is.EqualTo(NodeRunStatus.Skipped));
        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded), "a skipped branch is not a failure");
    }

    [Test]
    public async Task Branching_OtherHandle_RunsTheOtherBranch()
    {
        var run = await Run(Def($"[{N("t", "trigger")},{B("if", "false")},{N("yes")},{N("no")}]", $"[{E("t", "if")},{EH("if", "true", "yes")},{EH("if", "false", "no")}]"));
        Assert.That(S(run, "yes"), Is.EqualTo(NodeRunStatus.Skipped));
        Assert.That(S(run, "no"), Is.EqualTo(NodeRunStatus.Succeeded));
    }

    [Test]
    public async Task Branching_BranchesMeetingAgain_RunWithTheSingleActiveInput()
    {
        var run = await Run(Def($"[{N("t", "trigger")},{B("if", "true")},{N("yes", p: "{\"name\":\"yes\",\"out\":{\"from\":\"yes\"}}")},{N("no")},{N("join")}]",
            $"[{E("t", "if")},{EH("if", "true", "yes")},{EH("if", "false", "no")},{E("yes", "join")},{E("no", "join")}]"));
        Assert.That(S(run, "join"), Is.EqualTo(NodeRunStatus.Succeeded));
        Assert.That(S(run, "no"), Is.EqualTo(NodeRunStatus.Skipped));
        var input = _act.Calls.Single(c => (string?)c.Params["name"] == "join").Input!;
        Assert.That(input, Is.InstanceOf<JObject>(), "one active parent: its output, not an array");
        Assert.That((string?)input["from"], Is.EqualTo("yes"));
    }

    [Test]
    public async Task Branching_AllConnectionsInactive_SkipsTheCard()
    {
        var run = await Run(Def($"[{N("t", "trigger")},{B("if", "false")},{N("yes")},{N("after")}]", $"[{E("t", "if")},{EH("if", "true", "yes")},{E("yes", "after")}]"));
        Assert.That(S(run, "yes"), Is.EqualTo(NodeRunStatus.Skipped));
        Assert.That(S(run, "after"), Is.EqualTo(NodeRunStatus.Skipped), "skip cascades");
    }

    [Test]
    public async Task Failure_MarksDownstreamSkipped_AndRunFailed()
    {
        var run = await Run(Def($"[{N("t", "trigger")},{N("a", p: "{\"fail\":\"boom\"}")},{N("b")}]", $"[{E("t", "a")},{E("a", "b")}]"));
        Assert.That(run.Status, Is.EqualTo(RunStatus.Failed));
        Assert.That(S(run, "a"), Is.EqualTo(NodeRunStatus.Failed));
        Assert.That(run.Nodes.Single(n => n.NodeId == "a").Error, Is.EqualTo("boom"));
        Assert.That(S(run, "b"), Is.EqualTo(NodeRunStatus.Skipped));
        Assert.That(run.Error, Is.EqualTo("boom"));
        Assert.That(_act.Calls.Any(c => (string?)c.Params["name"] == "b"), Is.False);
    }

    [Test]
    public async Task ParallelBranches_RunConcurrently_AndMergeInputsAsArray()
    {
        // a and b are released together only if both are running at the same time
        _act.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var runTask = Run(Def($"[{N("t", "trigger")},{N("a")},{N("b")},{N("d")}]", $"[{E("t", "a")},{E("t", "b")},{E("a", "d")},{E("b", "d")}]"));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline) { lock (_act.Calls) if (_act.Calls.Count == 2) break; await Task.Delay(10); }
        lock (_act.Calls) Assert.That(_act.Calls.Count, Is.EqualTo(2), "both branches started before either finished");
        _act.Gate.SetResult();
        var run = await runTask;
        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded));
        var merged = _act.Calls.Single(c => (string?)c.Params["name"] == "d").Input!;
        Assert.That(merged, Is.InstanceOf<JArray>());
        Assert.That(((JArray)merged).Count, Is.EqualTo(2));
    }

    [Test]
    public async Task CardsNotReachableFromATrigger_AreSkipped()
    {
        var run = await Run(Def($"[{N("t", "trigger")},{N("orphan")}]", "[]"));
        Assert.That(S(run, "orphan"), Is.EqualTo(NodeRunStatus.Skipped));
        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded));
    }

    [Test]
    public async Task UnknownCardType_FailsThatCard()
    {
        var run = await Run(Def($"[{N("t", "trigger")},{N("x", "ghost")}]", $"[{E("t", "x")}]"));
        Assert.That(S(run, "x"), Is.EqualTo(NodeRunStatus.Failed));
        Assert.That(run.Status, Is.EqualTo(RunStatus.Failed));
    }

    [Test]
    public async Task Cancel_StopsRunningCard_AndCancelsTheRest()
    {
        _act.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var runTask = Run(Def($"[{N("t", "trigger")},{N("a")},{N("b")}]", $"[{E("t", "a")},{E("a", "b")}]"), ct: cts.Token);
        await _act.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        var run = await runTask;
        Assert.That(run.Status, Is.EqualTo(RunStatus.Cancelled));
        Assert.That(S(run, "a"), Is.EqualTo(NodeRunStatus.Cancelled));
        Assert.That(S(run, "b"), Is.EqualTo(NodeRunStatus.Cancelled));
        Assert.That(S(run, "t"), Is.EqualTo(NodeRunStatus.Succeeded));
    }

    [Test]
    public async Task TwoRunsAtOnce_AreIndependent()
    {
        _act.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var def = Def($"[{N("t", "trigger")},{N("a")}]", $"[{E("t", "a")}]");
        var first = Run(def, "one");
        var second = _sut.ExecuteAsync(new FlowRun("r2", "f", "Flow", RunStatus.Running, DateTimeOffset.UtcNow, null, "two", null, [new NodeRun("t", NodeRunStatus.Pending), new NodeRun("a", NodeRunStatus.Pending)]), def, "two", _ => { }, default);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline) { lock (_act.Calls) if (_act.Calls.Count == 2) break; await Task.Delay(10); }
        lock (_act.Calls) Assert.That(_act.Calls.Count, Is.EqualTo(2));
        _act.Gate.SetResult();
        var runs = await Task.WhenAll(first, second);
        Assert.That(runs.All(r => r.Status == RunStatus.Succeeded), Is.True);
        Assert.That(runs.Select(r => r.Id), Is.EquivalentTo(new[] { "r", "r2" }));
    }
}
