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
        ])
    {
        Guide = new CardGuide(
            "Converts between JSON text and data. Parse turns text (for example the output of a script or an API) into fields you can use. Stringify turns data back into text.",
            "Receives the previous card's output as input. The Source setting says which part to convert, e.g. {{input.stdout}} (a script's printed text) or {{input.text}}.",
            new JObject { ["name"] = "Sok", ["tags"] = new JArray("a", "b") },
            [
                new GuideExample("Parse what a script printed", "Turn the printed text into fields.",
                    new JObject { ["mode"] = "parse", ["source"] = "{{input.stdout}}" }, "{{input.stdout}}", "text",
                    new JObject { ["stdout"] = "{\"name\":\"Sok\",\"tags\":[\"a\",\"b\"]}" },
                    "Output { name: \"Sok\", tags: [\"a\",\"b\"] }. The next card reads {{input.name}}."),
                new GuideExample("Stringify some data", "Turn an object into text, e.g. to put it in a file or a prompt.",
                    new JObject { ["mode"] = "stringify", ["source"] = "{{input}}", ["pretty"] = true }, "{{input}}", "text",
                    new JObject { ["name"] = "Sok" }, "Output { text: \"{ \\\"name\\\": \\\"Sok\\\" }\" } (indented). Next card: {{input.text}}."),
            ],
            ["If the Script card printed JSON you do not need this card: read {{input.json.field}} directly.", "A JSON list or number is wrapped as { value: … }."]),
    };

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
