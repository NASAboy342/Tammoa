using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Repositories;
using Tammoa.Services;

namespace Tammoa.Test.Services;

public class RunEventBusAndRepoTest
{
    private static FlowRun Run(string id, string flow = "f", RunStatus s = RunStatus.Running, int minute = 0) =>
        new(id, flow, "F", s, new DateTimeOffset(2026, 1, 1, 0, minute % 60, 0, TimeSpan.Zero).AddHours(minute / 60), null, null, null, []);

    [Test]
    public async Task Bus_DeliversToEverySubscriber_AndStopsAfterUnsubscribe()
    {
        var bus = new RunEventBus();
        using var c1 = new CancellationTokenSource();
        using var c2 = new CancellationTokenSource();
        var r1 = bus.Subscribe(c1.Token);
        var r2 = bus.Subscribe(c2.Token);
        bus.Publish(Run("a"));
        Assert.That((await r1.ReadAsync()).RunId, Is.EqualTo("a"));
        Assert.That((await r2.ReadAsync()).RunId, Is.EqualTo("a"));
        c1.Cancel();
        bus.Publish(Run("b"));
        Assert.That((await r2.ReadAsync()).RunId, Is.EqualTo("b"));
        Assert.That(await r1.WaitToReadAsync(), Is.False, "cancelled subscriber's channel is completed");
    }

    [Test]
    public void Bus_SlowSubscriberDropsOldest_NeverBlocksPublisher()
    {
        var bus = new RunEventBus();
        using var cts = new CancellationTokenSource();
        var reader = bus.Subscribe(cts.Token);
        for (var i = 0; i < 1000; i++) bus.Publish(Run("r" + i));
        var got = new List<string>();
        while (reader.TryRead(out var r)) got.Add(r.RunId!);
        Assert.That(got.Count, Is.EqualTo(256));
        Assert.That(got.Last(), Is.EqualTo("r999"));
    }

    [Test]
    public void Repo_SaveReplacesById_ListsNewestFirstPerFlow()
    {
        var repo = new RunRepository();
        repo.Save(Run("old", minute: 1));
        repo.Save(Run("new", minute: 5));
        repo.Save(Run("other", flow: "g", minute: 3));
        repo.Save(Run("old", s: RunStatus.Succeeded, minute: 1));
        Assert.That(repo.ListForFlow("f").Select(r => r.Id), Is.EqualTo(new[] { "new", "old" }));
        Assert.That(repo.Get("old")!.Status, Is.EqualTo(RunStatus.Succeeded));
        Assert.That(repo.Get("missing"), Is.Null);
    }

    [Test]
    public void Repo_PrunesOldestFinishedButKeepsRunning()
    {
        var repo = new RunRepository();
        repo.Save(Run("running-oldest", s: RunStatus.Running, minute: 0));
        for (var i = 1; i <= RunRepository.MaxRuns + 5; i++) repo.Save(Run("r" + i, s: RunStatus.Succeeded, minute: i));
        Assert.That(repo.Get("running-oldest"), Is.Not.Null);
        Assert.That(repo.Get("r1"), Is.Null);
        Assert.That(repo.ListForFlow("f").Count, Is.LessThanOrEqualTo(RunRepository.MaxRuns));
    }
}
