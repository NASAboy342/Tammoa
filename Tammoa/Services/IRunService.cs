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

    /// <summary>Deletes one finished run from the log.</summary>
    /// <exception cref="KeyNotFoundException">No such run (404).</exception>
    /// <exception cref="InvalidOperationException">The run is still running: cancel it first (409).</exception>
    void Delete(string runId);

    /// <summary>Deletes every finished run of a flow (running ones stay) and returns their ids.</summary>
    IReadOnlyList<string> Clear(string flowId);

    /// <summary>Asks a running run to stop. A finished run is returned unchanged.</summary>
    /// <exception cref="KeyNotFoundException">No such run (404).</exception>
    FlowRun Cancel(string runId);
}
