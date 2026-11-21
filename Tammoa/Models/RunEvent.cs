namespace Tammoa.Models;

/// <summary>What the event stream carries: a run snapshot ("run") or the news that a run was deleted ("run-removed").</summary>
public record RunEvent(string Kind, FlowRun? Run, string? RunId, string? FlowId)
{
    public static RunEvent Changed(FlowRun run) => new("run", run, run.Id, run.FlowId);
    public static RunEvent Removed(string flowId, string runId) => new("run-removed", null, runId, flowId);
}
