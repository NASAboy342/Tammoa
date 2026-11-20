using Newtonsoft.Json.Linq;
using Tammoa.Enums;

namespace Tammoa.Models;

public record CardParamInfo(
    string Key,
    string Label,
    ParamKind Kind,
    string? Help = null,
    bool Required = false,
    JToken? DefaultValue = null,
    string? Placeholder = null);

public record CardTypeInfo(
    string Type,
    string Name,
    string Description,
    CardCategory Category,
    string Icon,
    IReadOnlyList<CardParamInfo> Params)
{
    public bool IsTrigger => Category == CardCategory.Trigger;
}

/// <summary>What a card receives when it runs. <see cref="Input"/> is the parent's output
/// (null for a trigger; an array of outputs when the card has several parents).</summary>
public record CardExecutionContext(JObject Params, JToken? Input, JToken? RunInput)
{
    /// <summary>The object that {{ templates }} are resolved against: { input, run: { input } }.</summary>
    public JObject TemplateContext => new()
    {
        ["input"] = Input?.DeepClone() ?? JValue.CreateNull(),
        ["run"] = new JObject { ["input"] = RunInput?.DeepClone() ?? JValue.CreateNull() },
    };
}
