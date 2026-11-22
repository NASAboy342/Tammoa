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
    ConnectionProvider? Provider = null,               // Connection: only connections of this provider
    IReadOnlyDictionary<string, string>? CodeHelp = null,          // Code: one-line "how do I read the input" hint per language (shown under the editor)
    IReadOnlyDictionary<string, string>? CodePlaceholders = null); // Code: starter text per language (shown while the editor is empty)

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

    /// <summary>A source runs at the start of a run even with nothing connected to it and without being a trigger (e.g. the Data card).
    /// A flow still needs a trigger to start.</summary>
    public bool IsSource { get; init; }

    /// <summary>Output handles, in display order. Edges start from one of them (default "main").</summary>
    public IReadOnlyList<CardOutputInfo> Outputs { get; init; } = MainOutput;

    /// <summary>The card's tutorial: what it does, how its input arrives, what it outputs, ready examples. The UI renders it; it has no card-specific text.</summary>
    public CardGuide? Guide { get; init; }
}

/// <summary>One worked example in a card's tutorial. <see cref="Params"/> are the settings "Use this example" applies to the card;
/// <see cref="Code"/> is what to show (and copy); <see cref="SampleInput"/> is the input the example assumes;
/// <see cref="Result"/> says in words what the card then outputs and how the next card can use it.</summary>
public record GuideExample(
    string Title,
    string Description,
    JObject? Params = null,
    string? Code = null,
    string? Language = null,
    JToken? SampleInput = null,
    string? Result = null,
    JToken? RunInput = null,
    /// <summary>The Data cards the example assumes: name -> value (only for the examples' sake).</summary>
    JObject? SampleData = null);

/// <summary>A card's tutorial. <see cref="OutputExample"/> doubles as the "fields this card will provide" shown to the card connected after it,
/// before anything has run.</summary>
public record CardGuide(
    string Overview,
    string InputNote,
    JToken? OutputExample,
    IReadOnlyList<GuideExample> Examples,
    IReadOnlyList<string>? Tips = null);

/// <summary>What a card returns: its output, and (for branching cards) which output handle fired.
/// <see cref="Branch"/> null = every outgoing connection is active.</summary>
public record CardResult(JToken Output, string? Branch = null)
{
    public static implicit operator CardResult(JToken output) => new(output);
}

/// <summary>What a card receives when it runs. <see cref="Input"/> is the output of the active parent(s)
/// (null for a trigger; an array of outputs when several connections are active).</summary>
public record CardExecutionContext(
    JObject Params,
    JToken? Input,
    JToken? RunInput,
    JObject? Data = null,                 // the Data cards of the flow: name -> value (text, or parsed JSON), read when this card started
    Action<JToken>? Progress = null,      // report what the card has produced so far (shown live, throttled by the engine)
    string? FlowId = null,
    string? NodeId = null,
    string? NodeName = null)
{
    /// <summary>The object that {{ templates }} are resolved against: { input, run: { input }, data: { Name: value } }.</summary>
    public JObject TemplateContext => new()
    {
        ["input"] = Input?.DeepClone() ?? JValue.CreateNull(),
        ["run"] = new JObject { ["input"] = RunInput?.DeepClone() ?? JValue.CreateNull() },
        ["data"] = Data?.DeepClone() ?? new JObject(),
    };
}

/// <summary>A function the model may call (sent to the model as a JSON-schema function).</summary>
public record ToolDefinition(string Name, string Description, JObject Parameters);
