using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Services.Cards;

/// <summary>Reusable data: a piece of text kept in a .txt file, typed by hand or written by a flow, readable from ANY card of the flow as
/// {{data.Name}} (no wiring, no trigger). The card's NAME is its reference name. When it is connected it also outputs the text.</summary>
public class DataStoreCard : ICardType
{
    public const string TypeId = "data.store";
    private readonly ITemplateEngine _templates;
    private readonly IDataService _data;

    public DataStoreCard(ITemplateEngine templates, IDataService data)
    {
        _templates = templates;
        _data = data;
    }

    public CardTypeInfo Info { get; } = new(
        TypeId, "Data",
        "Stores text you can reuse: type it yourself or let a flow write it. Any card reads it as {{data.Name}} (the card's name), without wiring and without a trigger.",
        CardCategory.Data, "database",
        [
            new CardParamInfo("value", "Stored text", ParamKind.Stored, Help: "Saved as a .txt file. Press Save text; it does not need the flow to be saved again."),
            new CardParamInfo("scope", "Shared with", ParamKind.Select, DefaultValue: "flow",
                Options: [new("flow", "This flow only"), new("shared", "All flows (shared by name)")], Help: "Shared: every flow with a Data card of the same name uses the same text."),
            new CardParamInfo("mode", "When a card is connected to it", ParamKind.Select, DefaultValue: "keep",
                Options: [new("keep", "Keep the text (only read)"), new("replace", "Replace the text with what comes in"), new("append", "Add what comes in as a new line")],
                Help: "Only matters when another card is connected INTO this one."),
            new CardParamInfo("writeFrom", "What to save", ParamKind.Text, DefaultValue: "{{input}}", Placeholder: "{{input.text}}",
                Help: "A template, used when Replace or Add is chosen and a card is connected in. {{input}} saves the whole input."),
        ])
    {
        IsSource = true,
        Guide = DataStoreGuide.Create(),
    };

    public Task<CardResult> ExecuteAsync(CardExecutionContext context, CancellationToken ct)
    {
        if (context.FlowId is null || context.NodeId is null) throw new ArgumentException("A Data card only works inside a flow run.");
        var name = context.NodeName?.Trim();
        if (DataNames.Problem(name) is { } problem) throw new ArgumentException(problem);

        var scope = ParamReader.GetString(context.Params, "scope");
        if (scope.Length == 0) scope = DataScope.Flow;
        if (!DataScope.IsValid(scope)) throw new ArgumentException($"Unknown scope '{scope}'.");
        var mode = ParamReader.GetString(context.Params, "mode");
        if (mode.Length == 0) mode = "keep";
        if (mode is not ("keep" or "replace" or "append")) throw new ArgumentException($"Unknown mode '{mode}'.");

        var location = new DataLocation(scope, context.FlowId, context.NodeId, name!);
        var changed = false;
        if (context.Input is not null && mode != "keep")
        {
            var template = ParamReader.GetString(context.Params, "writeFrom");
            var incoming = _templates.Render(template.Length == 0 ? "{{input}}" : template, context.TemplateContext);
            var existing = _data.ReadAt(location).Text;
            var text = mode == "append" && existing.Length > 0 ? existing + "\n" + incoming : incoming;
            _data.WriteAt(location, text);
            changed = true;
        }

        var entry = _data.ReadAt(location);
        var output = new JObject
        {
            ["text"] = entry.Text,
            ["name"] = entry.Name,
            ["scope"] = entry.Scope,
            ["size"] = entry.Size,
            ["changed"] = changed,
        };
        if (entry.UpdatedAt is { } at) output["updatedAt"] = at;
        var value = _data.ToValue(entry.Text);
        if (value is not JValue) output["json"] = value; // a JSON object or list is also available as data
        return Task.FromResult<CardResult>(output);
    }
}
