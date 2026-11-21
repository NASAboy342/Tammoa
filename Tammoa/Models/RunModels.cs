using Newtonsoft.Json.Linq;
using Tammoa.Enums;

namespace Tammoa.Models;

/// <summary>Immutable snapshot of one card's execution inside a run.
/// <see cref="Input"/> is what the card received (null for a trigger, which takes the run input; an array when several connections were active),
/// kept so the UI can show "the input this card got" and which fields can be used.
/// <see cref="Live"/> is what a running card has produced so far (e.g. the answer an AI model is still writing); it is replaced by Output when the card finishes.</summary>
public record NodeRun(
    string NodeId,
    NodeRunStatus Status,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? FinishedAt = null,
    JToken? Output = null,
    string? Error = null,
    JToken? Input = null,
    JToken? Live = null);

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
