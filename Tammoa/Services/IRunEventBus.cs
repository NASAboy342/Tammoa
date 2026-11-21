using System.Threading.Channels;
using Tammoa.Models;

namespace Tammoa.Services;

/// <summary>Pushes run changes to every connected UI. Each subscriber has its own small buffer (slow clients drop their oldest events,
/// which is safe because every "run" event is a complete snapshot and carries a revision).</summary>
public interface IRunEventBus
{
    void Publish(FlowRun run);

    /// <summary>Tells every UI that a run was deleted.</summary>
    void PublishRemoved(string flowId, string runId);

    /// <summary>Subscribes until <paramref name="ct"/> is cancelled.</summary>
    ChannelReader<RunEvent> Subscribe(CancellationToken ct);
}
