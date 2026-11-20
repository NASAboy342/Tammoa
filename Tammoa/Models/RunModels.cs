using Newtonsoft.Json.Linq;
using Tammoa.Enums;

namespace Tammoa.Models;

/// <summary>Immutable snapshot of one card's execution inside a run.</summary>
public record NodeRun(
    string NodeId,
    NodeRunStatus Status,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? FinishedAt = null,
    JToken? Output = null,
    string? Error = null);

/// <summary>Immutable snapshot of a run. The backend owns the state; the UI only displays snapshots.
/// <see cref="Revision"/> grows with every change, so a client holding two snapshots of one run (one pushed, one fetched,
/// arriving in any order) can always tell which is newer.</summary>
public record FlowRun(
    string Id,
    string FlowId,
    string FlowName,
    RunStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    JToken? Input,
    string? Error,
    IReadOnlyList<NodeRun> Nodes,
    int Revision = 0);

public record StartRunRequest(JToken? Input);

public record ProcessResult(int ExitCode, string Stdout, string Stderr, bool TimedOut, bool Truncated);
