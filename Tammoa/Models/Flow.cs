using Newtonsoft.Json.Linq;

namespace Tammoa.Models;

/// <summary>A saved automation flow. <see cref="Definition"/> holds the cards and connections as JSON
/// ({ "nodes": [], "edges": [] }); the card canvas fills it in later.</summary>
public record Flow(
    string Id,
    string Name,
    string Description,
    bool Enabled,
    JObject Definition,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>List row: a flow without its (possibly large) definition.</summary>
public record FlowSummary(
    string Id,
    string Name,
    string Description,
    bool Enabled,
    int CardCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record CreateFlowRequest(string? Name, string? Description);

public record UpdateFlowRequest(string? Name, string? Description, JObject? Definition);

public record DeleteFlowRequest(string? ConfirmName);

/// <summary>Portable file format for sharing a flow between machines. Contains only what defines the flow:
/// no id, enabled state or timestamps (machine-specific), and never secrets (cards must reference
/// credentials by name, not embed API keys).</summary>
public record FlowDocument(string? Format, int Version, string? Name, string? Description, JObject? Definition);
