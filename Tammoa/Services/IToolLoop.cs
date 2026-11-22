using Newtonsoft.Json.Linq;
using Tammoa.Models;
using Tammoa.Services.Tools;

namespace Tammoa.Services;

public record ToolLoopRequest(
    Connection Connection, string Model, string? System, string Prompt, double? Temperature, int? MaxTokens, bool? Think, TimeSpan Timeout,
    IReadOnlyList<string> ToolIds, ToolContext Context, string? ApproverModel, int MaxSteps,
    string? ApproverInstructions = null); // replaces the built-in approver policy when set (the reply format is always added by the code)

/// <summary>The "agent" loop of the AI card: ask the model, run the tools it asks for (within the rules), give the results back, repeat until it answers in plain text.</summary>
public interface IToolLoop
{
    /// <summary>Returns the card output: the usual fields plus <c>toolCalls</c> and <c>steps</c>. <paramref name="progress"/> gets the state so far after every model piece and every tool call.</summary>
    /// <exception cref="ArgumentException">A selected tool does not exist.</exception>
    /// <exception cref="InvalidOperationException">The model failed, or still wanted tools after the last allowed step.</exception>
    Task<JObject> RunAsync(ToolLoopRequest request, Action<JToken>? progress, CancellationToken ct);
}
