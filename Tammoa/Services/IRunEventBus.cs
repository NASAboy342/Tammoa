using System.Threading.Channels;
using Tammoa.Models;

namespace Tammoa.Services;

/// <summary>Pushes run snapshots to every connected UI. Each subscriber has its own small buffer (slow clients drop their oldest snapshots, which is safe because each snapshot is complete).</summary>
public interface IRunEventBus
{
    void Publish(FlowRun run);

    /// <summary>Subscribes until <paramref name="ct"/> is cancelled.</summary>
    ChannelReader<FlowRun> Subscribe(CancellationToken ct);
}
