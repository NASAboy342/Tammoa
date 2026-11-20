using System.Threading.Channels;
using Tammoa.Models;

namespace Tammoa.Services;

public class RunEventBus : IRunEventBus
{
    private readonly List<Channel<FlowRun>> _subscribers = new();
    private readonly object _gate = new();

    public void Publish(FlowRun run)
    {
        Channel<FlowRun>[] targets;
        lock (_gate) targets = _subscribers.ToArray();
        foreach (var c in targets) c.Writer.TryWrite(run);
    }

    public ChannelReader<FlowRun> Subscribe(CancellationToken ct)
    {
        var channel = Channel.CreateBounded<FlowRun>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        lock (_gate) _subscribers.Add(channel);
        ct.Register(() =>
        {
            lock (_gate) _subscribers.Remove(channel);
            channel.Writer.TryComplete();
        });
        return channel.Reader;
    }
}
