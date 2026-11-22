using Newtonsoft.Json.Linq;
using Tammoa.Models;

namespace Tammoa.Services.Tools;

/// <summary>A tool call refused by a rule (outside the root folder, host not allowed, not approved). The reason goes back to the model, it does not fail the card.</summary>
public class ToolRejectedException(string reason) : Exception(reason);

/// <summary>What the card allows its tools to touch.</summary>
public record ToolContext(string? Root, IReadOnlyList<string> AllowedHosts);

/// <summary>One action the model may ask for. Description and parameters are sent to the model as a function schema.</summary>
public interface ITool
{
    ToolDefinition Definition { get; }
    /// <summary>True for tools that work on files: the card must then have a root folder.</summary>
    bool NeedsRoot { get; }

    /// <summary>The rules only, nothing is done. Run before the approver is asked, so rule violations never reach it.</summary>
    /// <exception cref="ToolRejectedException">A rule says no.</exception>
    /// <exception cref="ArgumentException">Missing or wrong arguments (the model can fix them).</exception>
    void Check(JObject arguments, ToolContext context);

    /// <summary>Does the work and returns the text the model gets back.</summary>
    /// <exception cref="ToolRejectedException">A rule says no.</exception>
    /// <exception cref="ArgumentException">Missing or wrong arguments.</exception>
    /// <exception cref="InvalidOperationException">The work failed.</exception>
    Task<string> RunAsync(JObject arguments, ToolContext context, CancellationToken ct);
}

public interface IToolRegistry
{
    ITool? Find(string id);
}
