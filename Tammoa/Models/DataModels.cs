namespace Tammoa.Models;

/// <summary>Where a Data card's text lives: "flow" = private to the flow, "shared" = shared by name with every flow.</summary>
public static class DataScope
{
    public const string Flow = "flow";
    public const string Shared = "shared";
    public static bool IsValid(string? scope) => scope is Flow or Shared;
}

/// <summary>Identifies the text file of one Data card.</summary>
public record DataLocation(string Scope, string FlowId, string CardId, string Name);

/// <summary>The stored text of one Data card, as the UI edits it.</summary>
public record DataEntry(string CardId, string Name, string Scope, string Text, string Path, long Size, DateTimeOffset? UpdatedAt, bool Exists);

/// <summary>A Data card of a flow with a short preview, for lists ("what data can I use here").</summary>
public record DataCardSummary(string CardId, string Name, string Scope, string Preview, long Size, DateTimeOffset? UpdatedAt, bool IsJson);

public record SaveDataRequest(string? Text);
