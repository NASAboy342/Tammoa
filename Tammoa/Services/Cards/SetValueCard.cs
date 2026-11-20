using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Services.Cards;

public class SetValueCard : ICardType
{
    public const string TypeId = "data.set";
    private readonly ITemplateEngine _templates;

    public SetValueCard(ITemplateEngine templates) => _templates = templates;

    public CardTypeInfo Info { get; } = new(
        TypeId, "Set value",
        "Builds a JSON object. Strings may use {{input.field}} and {{run.input}}; a string that is only one {{ }} keeps its type.",
        CardCategory.Data, "sliders",
        [
            new CardParamInfo("values", "Values (JSON object)", ParamKind.Textarea,
                Help: "Example: { \"message\": \"{{input.stdout}}\" }", Required: true,
                DefaultValue: "{\n  \"message\": \"hello\"\n}"),
        ]);

    public Task<JToken> ExecuteAsync(CardExecutionContext context, CancellationToken ct)
    {
        var text = ParamReader.GetString(context.Params, "values");
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Values is empty; enter a JSON object.");
        JToken parsed;
        try { parsed = JToken.Parse(text); }
        catch (JsonReaderException ex) { throw new ArgumentException($"Values is not valid JSON: {ex.Message}"); }
        if (parsed is not JObject) throw new ArgumentException("Values must be a JSON object.");
        return Task.FromResult(_templates.Resolve(parsed, context.TemplateContext));
    }
}
