using Newtonsoft.Json.Linq;
using NSubstitute;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Services;

namespace Tammoa.Test.Services;

/// <summary>Loops: a connection that goes back to an earlier card runs that card (and its loop) again with the new input.</summary>
public class FlowExecutorLoopTest
{
    /// <summary>A card whose behaviour is a function, so a test can write "count up" or "decide again/done".</summary>
    private sealed class FnCard : ICardType
    {
        private readonly Func<CardExecutionContext, int, CardResult> _fn;
        private int _runs;
        public FnCard(string type, Func<CardExecutionContext, int, CardResult> fn, bool trigger = false, params string[] outputs)
        {
            _fn = fn;
            Info = new CardTypeInfo(type, type, "", trigger ? CardCategory.Trigger : CardCategory.Action, "zap", [])
            { Outputs = outputs.Length == 0 ? CardTypeInfo.MainOutput : outputs.Select(o => new CardOutputInfo(o, o)).ToList() };
        }
        public CardTypeInfo Info { get; }
        public TaskCompletionSource? Gate { get; set; }
        public List<JToken?> Inputs { get; } = new();
        public int Runs => _runs;
        public async Task<CardResult> ExecuteAsync(CardExecutionContext c, CancellationToken ct)
        {
            int n;
            lock (Inputs) { Inputs.Add(c.Input?.DeepClone()); n = ++_runs; }
            if (Gate is not null) await Gate.Task.WaitAsync(ct);
            return _fn(c, n);
        }
    }

    private sealed class Registry(params FnCard[] cards) : ICardRegistry
    {
        public IReadOnlyList<CardTypeInfo> All() => cards.Select(c => c.Info).ToList();
        public ICardType? Find(string type) => cards.FirstOrDefault(c => c.Info.Type == type);
    }

    private readonly List<FlowRun> _snapshots = new();
    private FnCard _trigger = null!;
    private FnCard _inc = null!;      // counts up: n = (input.n ?? 0) + 1
    private FnCard _check = null!;    // "again" while n < Max, then "done"
    private FnCard _after = null!;    // what follows the loop
    private int _max = 3;

    [SetUp]
    public void SetUp()
    {
        _snapshots.Clear();
        _max = 3;
        _trigger = new FnCard("trigger", (_, _) => new JObject { ["n"] = 0 }, trigger: true);
        _inc = new FnCard("inc", (c, _) => new JObject { ["n"] = ((int?)c.Input?["n"] ?? 0) + 1 });
        _check = new FnCard("check", (c, _) => new CardResult(c.Input!.DeepClone(), (int)c.Input!["n"]! < _max ? "again" : "done"), false, "again", "done");
        _after = new FnCard("after", (c, _) => new JObject { ["final"] = c.Input!["n"]!.DeepClone() });
    }

    private async Task<FlowRun> Run(string nodes, string edges, string settings = "", CancellationToken ct = default, params FnCard[] extra)
    {
        var def = JObject.Parse($"{{\"nodes\":{nodes},\"edges\":{edges}{(settings.Length > 0 ? ",\"settings\":" + settings : "")}}}");
        var ids = ((JArray)def["nodes"]!).Select(n => (string)n["id"]!).ToList();
        var initial = new FlowRun("r", "f", "Flow", RunStatus.Running, DateTimeOffset.UtcNow, null, null, null, ids.Select(i => new NodeRun(i, NodeRunStatus.Pending)).ToList());
        var data = Substitute.For<IDataService>();
        data.LoadContext(default!, default!).ReturnsForAnyArgs(new JObject());
        var sut = new FlowExecutor(new Registry([_trigger, _inc, _check, _after, .. extra]), TimeProvider.System, data);
        return await sut.ExecuteAsync(initial, def, null, r => { lock (_snapshots) _snapshots.Add(r); }, ct);
    }

    private static string N(string id, string type) => $"{{\"id\":\"{id}\",\"type\":\"{type}\"}}";
    private static string E(string s, string t, string handle = "main") => $"{{\"source\":\"{s}\",\"sourceHandle\":\"{handle}\",\"target\":\"{t}\"}}";
    private static NodeRun Node(FlowRun r, string id) => r.Nodes.Single(n => n.NodeId == id);

    // trigger -> inc -> check; check.again -> inc (loop-back); check.done -> after
    private const string Counter = "[{\"id\":\"t\",\"type\":\"trigger\"},{\"id\":\"inc\",\"type\":\"inc\"},{\"id\":\"check\",\"type\":\"check\"},{\"id\":\"after\",\"type\":\"after\"}]";
    private static readonly string CounterEdges = $"[{E("t", "inc")},{E("inc", "check")},{E("check", "inc", "again")},{E("check", "after", "done")}]";

    [Test]
    public async Task ALoopRepeatsUntilTheConditionSendsTheFlowElsewhere_AndTheCardsAfterItRunOnce()
    {
        var run = await Run(Counter, CounterEdges);
        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded), run.Error);
        Assert.That(_inc.Runs, Is.EqualTo(3));
        Assert.That(_check.Runs, Is.EqualTo(3));
        Assert.That(_after.Runs, Is.EqualTo(1), "what follows the loop runs once, when the loop is over");
        Assert.That((int)Node(run, "after").Output!["final"]!, Is.EqualTo(3));
        Assert.That(Node(run, "inc").Iteration, Is.EqualTo(3));
        Assert.That(Node(run, "after").Iteration, Is.EqualTo(1));
        Assert.That(Node(run, "t").Iteration, Is.EqualTo(1));
    }

    [Test]
    public async Task EachRoundGetsTheNewInput_TheFirstOneTheTriggersOutput()
    {
        await Run(Counter, CounterEdges);
        Assert.That(_inc.Inputs.Select(i => (int)i!["n"]!), Is.EqualTo(new[] { 0, 1, 2 }), "round 1: from the trigger; rounds 2 and 3: the output of the card that looped back");
    }

    [Test]
    public async Task EarlierRoundsAreKept_WithTheirInputAndOutput()
    {
        var run = await Run(Counter, CounterEdges);
        var inc = Node(run, "inc");
        Assert.That(inc.Previous!.Select(p => p.Iteration), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(inc.Previous!.Select(p => (int)p.Input!["n"]!), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(inc.Previous!.Select(p => (int)p.Output!["n"]!), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(inc.Previous!.All(p => p.Status == NodeRunStatus.Succeeded), Is.True);
        Assert.That((int)inc.Output!["n"]!, Is.EqualTo(3), "the current round is the card's own state");
        Assert.That(Node(run, "after").Previous, Is.Null, "a card that ran once has no earlier rounds");
        Assert.That(Node(run, "t").Previous, Is.Null);
    }

    [Test]
    public async Task TheCardAfterTheLoop_IsNeverMarkedSkippedWhileTheLoopCanStillRepeat()
    {
        await Run(Counter, CounterEdges);
        lock (_snapshots)
        {
            var statuses = _snapshots.Select(s => Node(s, "after").Status).ToList();
            Assert.That(statuses.Where(s => s == NodeRunStatus.Skipped), Is.Empty, "it waited; it was not skipped on the first round");
            Assert.That(statuses.First(s => s != NodeRunStatus.Pending), Is.EqualTo(NodeRunStatus.Running));
        }
    }

    [Test]
    public async Task OnlyTheLastRoundsConditionLeavesTheLoop()
    {
        _max = 1; // the first check already says "done": no repeat at all
        var run = await Run(Counter, CounterEdges);
        Assert.That(_inc.Runs, Is.EqualTo(1));
        Assert.That(Node(run, "inc").Iteration, Is.EqualTo(1));
        Assert.That(Node(run, "inc").Previous, Is.Null);
        Assert.That(_after.Runs, Is.EqualTo(1));
    }

    [Test]
    public async Task OnlyTheNewestTenEarlierRoundsAreKept()
    {
        _max = 15;
        var run = await Run(Counter, CounterEdges);
        var inc = Node(run, "inc");
        Assert.That(inc.Iteration, Is.EqualTo(15));
        Assert.That(inc.Previous, Has.Count.EqualTo(NodeIteration.Max));
        Assert.That(inc.Previous!.First().Iteration, Is.EqualTo(5), "rounds 1 to 4 were dropped");
        Assert.That(inc.Previous!.Last().Iteration, Is.EqualTo(14));
    }

    [Test]
    public async Task ALoopThatNeverEnds_StopsAtTheLoopLimit_WithAClearMessage_AndTheRunFails()
    {
        _max = int.MaxValue;
        var run = await Run(Counter, CounterEdges, "{\"loopLimit\":4}");
        Assert.That(run.Status, Is.EqualTo(RunStatus.Failed));
        Assert.That(_inc.Runs, Is.EqualTo(4), "exactly the limit, not one more");
        Assert.That(run.Error, Does.Contain("Loop limit reached").And.Contain("'inc'").And.Contain("4 times").And.Contain("IF"));
        Assert.That(Node(run, "inc").Status, Is.EqualTo(NodeRunStatus.Failed));
        Assert.That(Node(run, "after").Status, Is.EqualTo(NodeRunStatus.Skipped));
        Assert.That(run.Nodes.Any(n => n.Status is NodeRunStatus.Pending or NodeRunStatus.Running), Is.False, "nothing is left hanging");
    }

    [Test]
    public async Task WithoutASetting_TheLimitIsFifty()
    {
        _max = int.MaxValue;
        var run = await Run(Counter, CounterEdges);
        Assert.That(run.Status, Is.EqualTo(RunStatus.Failed));
        Assert.That(_inc.Runs, Is.EqualTo(FlowExecutor.DefaultLoopLimit));
        Assert.That(FlowExecutor.DefaultLoopLimit, Is.EqualTo(50));
    }

    [Test]
    public async Task TheLimitCountsRunsOfOneCard_AFlowWithoutLoopsIsNotAffected()
    {
        var run = await Run($"[{N("t", "trigger")},{N("a", "inc")},{N("b", "inc")},{N("c", "inc")}]", $"[{E("t", "a")},{E("a", "b")},{E("b", "c")}]", "{\"loopLimit\":1}");
        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded));
    }

    [Test]
    public async Task ALoopBackFromACardThatDoesNotBranch_RepeatsUntilTheLimit()
    {
        // inc -> inc2, inc2 -> inc (back): nothing decides, so the limit ends it
        var run = await Run($"[{N("t", "trigger")},{N("a", "inc")},{N("b", "inc")}]", $"[{E("t", "a")},{E("a", "b")},{E("b", "a")}]", "{\"loopLimit\":3}");
        Assert.That(run.Status, Is.EqualTo(RunStatus.Failed));
        Assert.That(run.Error, Does.Contain("Loop limit reached"));
        Assert.That(Node(run, "b").Iteration, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task ABackEdgeMayJumpOverCards_AllCardsOfTheLoopRunAgain()
    {
        // t -> a -> b -> c -> check; check.again -> a (jumps back over b and c)
        _max = 3;
        var a = new FnCard("a", (c, _) => new JObject { ["n"] = ((int?)c.Input?["n"] ?? 0) + 1 });
        var b = new FnCard("b", (c, _) => c.Input!.DeepClone());
        var cc = new FnCard("c", (c, _) => c.Input!.DeepClone());
        var run = await Run($"[{N("t", "trigger")},{N("a", "a")},{N("b", "b")},{N("c", "c")},{N("check", "check")},{N("after", "after")}]",
            $"[{E("t", "a")},{E("a", "b")},{E("b", "c")},{E("c", "check")},{E("check", "a", "again")},{E("check", "after", "done")}]", "", default, a, b, cc);
        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded), run.Error);
        Assert.That((a.Runs, b.Runs, cc.Runs, _check.Runs, _after.Runs), Is.EqualTo((3, 3, 3, 3, 1)));
    }

    [Test]
    public async Task FanOutAndJoinInsideALoop_EveryCardRunsEveryRound_AndTheJoinSeesBothBranches()
    {
        _max = 3;
        var a = new FnCard("a", (c, _) => new JObject { ["n"] = ((int?)c.Input?["n"] ?? 0) + 1 });
        var left = new FnCard("left", (c, _) => new JObject { ["n"] = c.Input!["n"]!.DeepClone(), ["side"] = "left" });
        var right = new FnCard("right", (c, _) => new JObject { ["n"] = c.Input!["n"]!.DeepClone(), ["side"] = "right" });
        var join = new FnCard("join", (c, _) => new JObject { ["n"] = c.Input![0]!["n"]!.DeepClone(), ["sides"] = new JArray(c.Input!.Select(i => i["side"]!.DeepClone())) });
        var run = await Run($"[{N("t", "trigger")},{N("a", "a")},{N("l", "left")},{N("r", "right")},{N("j", "join")},{N("check", "check")},{N("after", "after")}]",
            $"[{E("t", "a")},{E("a", "l")},{E("a", "r")},{E("l", "j")},{E("r", "j")},{E("j", "check")},{E("check", "a", "again")},{E("check", "after", "done")}]", "", default, a, left, right, join);
        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded), run.Error);
        Assert.That((a.Runs, left.Runs, right.Runs, join.Runs), Is.EqualTo((3, 3, 3, 3)));
        Assert.That(join.Inputs.All(i => i is JArray { Count: 2 }), Is.True, "the join got both branches every round, never a stale one");
        Assert.That(_after.Runs, Is.EqualTo(1));
    }

    [Test]
    public async Task AdditionalLoopBacksFromParallelCards_WaitForTheSlowOne_ThenStartOneRoundWithBothOutputs()
    {
        // a feeds x and y; both loop back to a. The second round starts only when BOTH have finished, with both outputs.
        var rounds = 0;
        var a = new FnCard("a", (c, n) => { rounds = n; return new JObject { ["round"] = n }; });
        FnCard Looper(string type) => new(type, (c, _) => new CardResult(new JObject { ["from"] = type, ["round"] = c.Input!["round"]!.DeepClone() }, (int)c.Input!["round"]! < 2 ? "again" : "stop"), false, "again", "stop");
        var x = Looper("x");
        var y = Looper("y");
        y.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var runTask = Run($"[{N("t", "trigger")},{N("a", "a")},{N("x", "x")},{N("y", "y")}]", $"[{E("t", "a")},{E("a", "x")},{E("a", "y")},{E("x", "a", "again")},{E("y", "a", "again")}]", "", default, a, x, y);
        await Task.Delay(300);
        Assert.That(a.Runs, Is.EqualTo(1), "x is done but y is still running: no second round yet");
        y.Gate.SetResult();
        var run = await runTask;
        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded), run.Error);
        Assert.That(a.Runs, Is.EqualTo(2));
        Assert.That(a.Inputs[1], Is.InstanceOf<JArray>());
        Assert.That(((JArray)a.Inputs[1]!).Select(i => (string?)i["from"]), Is.EquivalentTo(new[] { "x", "y" }));
        Assert.That(rounds, Is.EqualTo(2));
    }

    [Test]
    public async Task ACardAfterTheLoopThatAlsoHasAnotherParent_WaitsForTheLoopAndThenGetsBoth()
    {
        // t -> other ; t -> inc ⟲ ; check.done -> join ; other -> join
        var other = new FnCard("other", (_, _) => new JObject { ["who"] = "other" });
        var join = new FnCard("join", (c, _) => new JObject { ["parts"] = c.Input!.DeepClone() });
        var run = await Run($"[{N("t", "trigger")},{N("other", "other")},{N("inc", "inc")},{N("check", "check")},{N("join", "join")}]",
            $"[{E("t", "other")},{E("t", "inc")},{E("inc", "check")},{E("check", "inc", "again")},{E("check", "join", "done")},{E("other", "join")}]", "", default, other, join);
        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded), run.Error);
        Assert.That(join.Runs, Is.EqualTo(1), "one run, after the loop ended, not once per round");
        Assert.That(((JArray)Node(run, "join").Output!["parts"]!).Count, Is.EqualTo(2));
    }

    [Test]
    public async Task TwoLoopsOneAfterTheOther_EachRunsItsOwnRounds()
    {
        _max = 2;
        var inc2 = new FnCard("inc2", (c, _) => new JObject { ["n"] = ((int?)c.Input?["n"] ?? 0) + 10 });
        var check2 = new FnCard("check2", (c, _) => new CardResult(c.Input!.DeepClone(), (int)c.Input!["n"]! < 30 ? "again" : "done"), false, "again", "done");
        var run = await Run($"[{N("t", "trigger")},{N("inc", "inc")},{N("check", "check")},{N("inc2", "inc2")},{N("check2", "check2")},{N("after", "after")}]",
            $"[{E("t", "inc")},{E("inc", "check")},{E("check", "inc", "again")},{E("check", "inc2", "done")},{E("inc2", "check2")},{E("check2", "inc2", "again")},{E("check2", "after", "done")}]", "", default, inc2, check2);
        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded), run.Error);
        Assert.That((_inc.Runs, inc2.Runs, _after.Runs), Is.EqualTo((2, 3, 1)), "the second loop adds 10 per round: 12, 22, 32");
        Assert.That((int)Node(run, "after").Output!["final"]!, Is.EqualTo(32));
    }

    [Test]
    public async Task AFailureInsideALoop_StopsTheRun_AndTheFailedRoundKeepsItsInput()
    {
        var boom = new FnCard("boom", (c, n) => n == 2 ? throw new InvalidOperationException("second round broke") : new CardResult(new JObject { ["n"] = n }));
        var run = await Run($"[{N("t", "trigger")},{N("boom", "boom")},{N("check", "check")}]", $"[{E("t", "boom")},{E("boom", "check")},{E("check", "boom", "again")}]", "", default, boom);
        // check needs input.n < max: n=1 < 3 -> again -> boom runs a 2nd time and fails
        Assert.That(run.Status, Is.EqualTo(RunStatus.Failed));
        Assert.That(run.Error, Is.EqualTo("second round broke"));
        Assert.That(Node(run, "boom").Status, Is.EqualTo(NodeRunStatus.Failed));
        Assert.That(Node(run, "boom").Iteration, Is.EqualTo(2));
        Assert.That(Node(run, "boom").Input, Is.Not.Null);
        Assert.That(Node(run, "boom").Previous!.Single().Iteration, Is.EqualTo(1));
    }

    [Test]
    public async Task CancellingInTheMiddleOfALoop_EndsTheRunCancelled_WithoutHanging()
    {
        _max = int.MaxValue;
        _inc.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var runTask = Run(Counter, CounterEdges, "{\"loopLimit\":1000}", cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        var run = await runTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(run.Status, Is.EqualTo(RunStatus.Cancelled));
        Assert.That(run.Nodes.Any(n => n.Status is NodeRunStatus.Pending or NodeRunStatus.Running), Is.False);
    }

    [Test]
    public async Task CardsInALoopThatTheLastRoundDidNotReach_AreFinishedNotPending()
    {
        // IF with a body on the "again" side: in the last round "done" is taken, so the body is skipped
        var body = new FnCard("body", (c, _) => new JObject { ["n"] = c.Input!["n"]!.DeepClone() });
        var run = await Run($"[{N("t", "trigger")},{N("inc", "inc")},{N("check", "check")},{N("body", "body")},{N("after", "after")}]",
            $"[{E("t", "inc")},{E("inc", "check")},{E("check", "body", "again")},{E("body", "inc")},{E("check", "after", "done")}]", "", default, body);
        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded), run.Error);
        Assert.That(run.Nodes.Any(n => n.Status == NodeRunStatus.Pending), Is.False);
        Assert.That(Node(run, "body").Status, Is.EqualTo(NodeRunStatus.Skipped), "not taken in the last round");
        Assert.That(body.Runs, Is.EqualTo(2));
        Assert.That(_inc.Runs, Is.EqualTo(3));
    }

    [Test]
    public async Task SnapshotsStayInRevisionOrder_AcrossRounds()
    {
        _max = 8;
        var run = await Run(Counter, CounterEdges);
        lock (_snapshots)
        {
            var revisions = _snapshots.Select(s => s.Revision).ToList();
            Assert.That(revisions, Is.Ordered.Ascending);
            Assert.That(revisions.Distinct().Count(), Is.EqualTo(revisions.Count));
            Assert.That(_snapshots.Last(), Is.SameAs(run));
        }
    }

    [Test]
    public async Task WhileARoundRuns_TheCardShowsTheNewRound_AndKeepsTheEarlierOnesToLookAt()
    {
        _max = 3;
        await Run(Counter, CounterEdges);
        lock (_snapshots)
        {
            var secondRoundRunning = _snapshots.First(s => Node(s, "inc") is { Status: NodeRunStatus.Running, Iteration: 2 });
            Assert.That(Node(secondRoundRunning, "inc").Previous!.Single().Iteration, Is.EqualTo(1));
            Assert.That(Node(secondRoundRunning, "inc").Output, Is.Null, "the new round has no output yet");
            Assert.That((int)Node(secondRoundRunning, "inc").Input!["n"]!, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task WhenALoopBackIsDecided_TheCardsOfTheLoopShowAsWaitingForTheNextRound()
    {
        _max = 3;
        await Run(Counter, CounterEdges);
        lock (_snapshots)
        {
            // between two rounds `check` is reset to pending (its earlier round kept) before it runs again
            Assert.That(_snapshots.Any(s => Node(s, "check") is { Status: NodeRunStatus.Pending, Previous.Count: > 0 }), Is.True);
        }
    }

    [Test]
    public async Task ALoopNothingStartsFrom_NeverRuns()
    {
        var run = await Run($"[{N("t", "trigger")},{N("a", "inc")},{N("b", "inc")}]", $"[{E("a", "b")},{E("b", "a")}]");
        Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded));
        Assert.That(Node(run, "a").Status, Is.EqualTo(NodeRunStatus.Skipped));
        Assert.That(Node(run, "b").Status, Is.EqualTo(NodeRunStatus.Skipped));
        Assert.That(_inc.Runs, Is.EqualTo(0));
    }

    [Test]
    public async Task TheRunsJson_CarriesIterationAndPrevious_InCamelCase()
    {
        var run = await Run(Counter, CounterEdges);
        var json = JObject.Parse(Tammoa.Helper.JsonHelper.Serialize(run));
        var inc = json["nodes"]!.Single(n => (string?)n["nodeId"] == "inc");
        Assert.That((int)inc["iteration"]!, Is.EqualTo(3));
        Assert.That(inc["previous"]!.Count(), Is.EqualTo(2));
        Assert.That((string?)inc["previous"]![0]!["status"], Is.EqualTo("succeeded"));
        Assert.That((int)inc["previous"]![0]!["input"]!["n"]!, Is.EqualTo(0));
    }
}
