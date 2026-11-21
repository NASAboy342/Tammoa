using System.Threading.Channels;
using Tammoa.Models;

namespace Tammoa.Services;

public class RunEventBus : IRunEventBus
{
    private readonly List<Channel<RunEvent>> _subscribers = new();
    private readonly object _gate = new();

    public void Publish(FlowRun run) => Send(RunEvent.Changed(run));

    public void PublishRemoved(string flowId, string runId) => Send(RunEvent.Removed(flowId, runId));

    public void PublishDataChanged(string flowId, string cardId) => Send(RunEvent.DataChanged(flowId, cardId));

    private void Send(RunEvent e)
    {
        Channel<RunEvent>[] targets;
        lock (_gate) targets = _subscribers.ToArray();
        foreach (var c in targets) c.Writer.TryWrite(e);
    }

    public ChannelReader<RunEvent> Subscribe(CancellationToken ct)
    {
        var channel = Channel.CreateBounded<RunEvent>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        lock (_gate) _subscribers.Add(channel);
        ct.Register(() =>
        {
            lock (_gate) _subscribers.Remove(channel);
            channel.Writer.TryComplete();
        });
        return channel.Reader;
    }
}
