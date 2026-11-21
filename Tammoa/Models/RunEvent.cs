namespace Tammoa.Models;

/// <summary>What the event stream carries: a run snapshot ("run"), the news that a run was deleted ("run-removed"), or that the stored text of a Data card changed ("data-changed").</summary>
public record RunEvent(string Kind, FlowRun? Run, string? RunId, string? FlowId, string? CardId = null)
{
    public static RunEvent Changed(FlowRun run) => new("run", run, run.Id, run.FlowId);
    public static RunEvent Removed(string flowId, string runId) => new("run-removed", null, runId, flowId);
    public static RunEvent DataChanged(string flowId, string cardId) => new("data-changed", null, null, flowId, cardId);
}
