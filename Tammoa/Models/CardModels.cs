using Newtonsoft.Json.Linq;
using Tammoa.Enums;

namespace Tammoa.Models;

public record ParamOption(string Value, string Label);

/// <summary>One output handle of a card (most cards have a single "main"; IF has "true" and "false").</summary>
public record CardOutputInfo(string Key, string Label);

public record CardParamInfo(
    string Key,
    string Label,
    ParamKind Kind,
    string? Help = null,
    bool Required = false,
    JToken? DefaultValue = null,
    string? Placeholder = null,
    IReadOnlyList<ParamOption>? Options = null,        // Select with fixed options
    string? OptionsSource = null,                      // Select with options loaded from GET /api/cards/options/{source}
    string? OptionsDependsOn = null,                   // param key whose value is sent as ?connectionId= (reload when it changes)
    string? CodeLanguage = null,                       // Code: shell | javascript | python | json | text
    string? CodeLanguageParam = null,                  // Code: language comes from another param's value
    ConnectionProvider? Provider = null);              // Connection: only connections of this provider

public record CardTypeInfo(
    string Type,
    string Name,
    string Description,
    CardCategory Category,
    string Icon,
    IReadOnlyList<CardParamInfo> Params)
{
    public static readonly IReadOnlyList<CardOutputInfo> MainOutput = [new CardOutputInfo("main", "")];

    public bool IsTrigger => Category == CardCategory.Trigger;

    /// <summary>Output handles, in display order. Edges start from one of them (default "main").</summary>
    public IReadOnlyList<CardOutputInfo> Outputs { get; init; } = MainOutput;
}

/// <summary>What a card returns: its output, and (for branching cards) which output handle fired.
/// <see cref="Branch"/> null = every outgoing connection is active.</summary>
public record CardResult(JToken Output, string? Branch = null)
{
    public static implicit operator CardResult(JToken output) => new(output);
}

/// <summary>What a card receives when it runs. <see cref="Input"/> is the output of the active parent(s)
/// (null for a trigger; an array of outputs when several connections are active).</summary>
public record CardExecutionContext(JObject Params, JToken? Input, JToken? RunInput)
{
    /// <summary>The object that {{ templates }} are resolved against: { input, run: { input } }.</summary>
    public JObject TemplateContext => new()
    {
        ["input"] = Input?.DeepClone() ?? JValue.CreateNull(),
        ["run"] = new JObject { ["input"] = RunInput?.DeepClone() ?? JValue.CreateNull() },
    };
}
