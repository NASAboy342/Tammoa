using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Services.Cards;

public class JsonCard : ICardType
{
    public const string TypeId = "data.json";
    private readonly ITemplateEngine _templates;

    public JsonCard(ITemplateEngine templates) => _templates = templates;

    public CardTypeInfo Info { get; } = new(
        TypeId, "JSON",
        "Parse: turns JSON text into data (an object stays an object, anything else becomes { value }). Stringify: turns data into JSON text { text }.",
        CardCategory.Data, "braces",
        [
            new CardParamInfo("mode", "Mode", ParamKind.Select, Required: true, DefaultValue: "parse", Options: [new("parse", "Parse text → data"), new("stringify", "Stringify data → text")]),
            new CardParamInfo("source", "Source", ParamKind.Text, Required: true, DefaultValue: "{{input.stdout}}", Placeholder: "{{input.text}}"),
            new CardParamInfo("pretty", "Pretty-print (stringify)", ParamKind.Bool, DefaultValue: false),
        ]);

    public Task<CardResult> ExecuteAsync(CardExecutionContext context, CancellationToken ct)
    {
        var ctx = context.TemplateContext;
        var mode = ParamReader.GetString(context.Params, "mode");
        var source = ParamReader.GetString(context.Params, "source");
        if (source.Trim().Length == 0) throw new ArgumentException("Source is empty.");

        if (mode is "" or "parse")
        {
            var text = _templates.Render(source, ctx);
            JToken parsed;
            try { parsed = JToken.Parse(text); }
            catch (JsonReaderException ex) { throw new ArgumentException($"The source is not valid JSON: {ex.Message}"); }
            return Task.FromResult<CardResult>(parsed is JObject o ? o : new JObject { ["value"] = parsed });
        }
        if (mode == "stringify")
        {
            var value = _templates.Resolve(new JValue(source), ctx); // a lone {{ }} keeps its type
            var text = value.ToString(ParamReader.GetBool(context.Params, "pretty", false) ? Formatting.Indented : Formatting.None);
            return Task.FromResult<CardResult>(new JObject { ["text"] = value.Type == JTokenType.String ? JsonConvert.ToString((string)value!) : text });
        }
        throw new ArgumentException($"Unknown mode '{mode}'.");
    }
}
