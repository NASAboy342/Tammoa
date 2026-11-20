using Newtonsoft.Json.Linq;
using Tammoa.Models;

namespace Tammoa.Services;

public interface IFlowExecutor
{
    /// <summary>
    /// Executes a validated definition. Cards whose parents all succeeded run concurrently; a failed card makes every
    /// card downstream of it Skipped and stops scheduling new cards; cancellation marks unfinished cards Cancelled.
    /// <paramref name="onChange"/> receives a new immutable snapshot after every state change (it may be called from any thread).
    /// Never throws for card failures: they are recorded in the returned run.
    /// </summary>
    Task<FlowRun> ExecuteAsync(FlowRun initial, JObject definition, JToken? runInput, Action<FlowRun> onChange, CancellationToken ct);
}
