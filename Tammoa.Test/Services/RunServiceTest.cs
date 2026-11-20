using Newtonsoft.Json.Linq;
using NSubstitute;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Repositories;
using Tammoa.Services;

namespace Tammoa.Test.Services;

public class RunServiceTest
{
    private IFlowService _flows = null!;
    private ICardRegistry _cards = null!;
    private IFlowExecutor _executor = null!;
    private IRunRepository _runs = null!;
    private IRunEventBus _bus = null!;
    private RunService _sut = null!;

    private static Flow FlowWith(string nodesJson) =>
        new("f1", "My flow", "", false, JObject.Parse($"{{\"nodes\":{nodesJson},\"edges\":[]}}"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [SetUp]
    public void SetUp()
    {
        _flows = Substitute.For<IFlowService>();
        _cards = Substitute.For<ICardRegistry>();
        _executor = Substitute.For<IFlowExecutor>();
        _runs = new RunRepository();
        _bus = Substitute.For<IRunEventBus>();
        var trigger = Substitute.For<ICardType>();
        trigger.Info.Returns(new CardTypeInfo("trigger", "T", "", CardCategory.Trigger, "play", []));
        var action = Substitute.For<ICardType>();
        action.Info.Returns(new CardTypeInfo("act", "A", "", CardCategory.Action, "zap", []));
        _cards.Find(Arg.Any<string>()).Returns((ICardType?)null);
        _cards.Find("trigger").Returns(trigger);
        _cards.Find("act").Returns(action);
        _sut = new RunService(_flows, _cards, _executor, _runs, _bus, TimeProvider.System);
    }

    [Test]
    public void Start_Valid_ReturnsRunningSnapshotAtOnce_PublishesAndStoresIt()
    {
        _flows.Get("f1").Returns(FlowWith("[{\"id\":\"t\",\"type\":\"trigger\"},{\"id\":\"a\",\"type\":\"act\"}]"));
        var tcs = new TaskCompletionSource<FlowRun>();
        _executor.ExecuteAsync(default!, default!, default, default!, default).ReturnsForAnyArgs(tcs.Task);

        var run = _sut.Start("f1", "hello");

        Assert.That(run.Status, Is.EqualTo(RunStatus.Running));
        Assert.That(run.FlowName, Is.EqualTo("My flow"));
        Assert.That(run.Input!.ToString(), Is.EqualTo("hello"));
        Assert.That(run.Nodes.Select(n => (n.NodeId, n.Status)), Is.EqualTo(new[] { ("t", NodeRunStatus.Pending), ("a", NodeRunStatus.Pending) }));
        Assert.That(_sut.Get(run.Id), Is.EqualTo(run));
        _bus.Received().Publish(Arg.Is<FlowRun>(r => r.Id == run.Id));
        tcs.SetResult(run);
    }

    [Test]
    public void Start_NoTrigger_ThrowsArgumentException_AndStartsNothing()
    {
        _flows.Get("f1").Returns(FlowWith("[{\"id\":\"a\",\"type\":\"act\"}]"));
        Assert.Throws<ArgumentException>(() => _sut.Start("f1", null));
        _executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, default, default!, default);
    }

    [Test]
    public void Start_MissingFlow_ThrowsKeyNotFound()
    {
        _flows.Get("zz").Returns(_ => throw new KeyNotFoundException());
        Assert.Throws<KeyNotFoundException>(() => _sut.Start("zz", null));
    }

    [Test]
    public async Task Start_ExecutorUpdates_AreStoredAndPublished()
    {
        _flows.Get("f1").Returns(FlowWith("[{\"id\":\"t\",\"type\":\"trigger\"}]"));
        _executor.ExecuteAsync(default!, default!, default, default!, default).ReturnsForAnyArgs(ci =>
        {
            var initial = ci.Arg<FlowRun>();
            ci.Arg<Action<FlowRun>>()(initial with { Status = RunStatus.Succeeded, FinishedAt = DateTimeOffset.UtcNow });
            return Task.FromResult(initial);
        });
        var run = _sut.Start("f1", null);
        await WaitUntil(() => _sut.Get(run.Id).Status == RunStatus.Succeeded);
        Assert.That(_sut.ListForFlow("f1").Single().Status, Is.EqualTo(RunStatus.Succeeded));
    }

    [Test]
    public async Task Start_ExecutorCrashes_RunEndsFailedNotStuckRunning()
    {
        _flows.Get("f1").Returns(FlowWith("[{\"id\":\"t\",\"type\":\"trigger\"}]"));
        _executor.ExecuteAsync(default!, default!, default, default!, default).ReturnsForAnyArgs<Task<FlowRun>>(_ => throw new InvalidOperationException("engine down"));
        var run = _sut.Start("f1", null);
        await WaitUntil(() => _sut.Get(run.Id).Status == RunStatus.Failed);
        Assert.That(_sut.Get(run.Id).Error, Is.EqualTo("engine down"));
    }

    [Test]
    public async Task Cancel_RunningRun_SignalsTheExecutorToken()
    {
        _flows.Get("f1").Returns(FlowWith("[{\"id\":\"t\",\"type\":\"trigger\"}]"));
        var seen = new TaskCompletionSource<CancellationToken>();
        _executor.ExecuteAsync(default!, default!, default, default!, default).ReturnsForAnyArgs(async ci =>
        {
            var token = ci.Arg<CancellationToken>();
            seen.SetResult(token);
            await Task.Delay(Timeout.Infinite, token).ContinueWith(_ => { });
            return ci.Arg<FlowRun>();
        });
        var run = _sut.Start("f1", null);
        var token = await seen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _sut.Cancel(run.Id);
        Assert.That(token.IsCancellationRequested, Is.True);
    }

    [Test]
    public void Cancel_UnknownRun_ThrowsKeyNotFound() => Assert.Throws<KeyNotFoundException>(() => _sut.Cancel("nope"));

    [Test]
    public void Get_UnknownRun_ThrowsKeyNotFound() => Assert.Throws<KeyNotFoundException>(() => _sut.Get("nope"));

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.That(condition(), Is.True, "condition not reached in time");
    }
}
