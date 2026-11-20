using Newtonsoft.Json.Linq;
using Tammoa.Models;

namespace Tammoa.Services;

public interface IRunService
{
    /// <summary>Starts a run in the background and returns its first snapshot (status Running) immediately.
    /// Runs are independent: any number can execute at the same time.</summary>
    /// <exception cref="KeyNotFoundException">No such flow (404).</exception>
    /// <exception cref="ArgumentException">The flow has no trigger card (400).</exception>
    FlowRun Start(string flowId, JToken? input);

    /// <exception cref="KeyNotFoundException">No such run (404).</exception>
    FlowRun Get(string runId);

    IReadOnlyList<FlowRun> ListForFlow(string flowId);

    /// <summary>Asks a running run to stop. A finished run is returned unchanged.</summary>
    /// <exception cref="KeyNotFoundException">No such run (404).</exception>
    FlowRun Cancel(string runId);
}
