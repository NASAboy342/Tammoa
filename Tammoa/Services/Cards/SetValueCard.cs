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
            new CardParamInfo("values", "Values (JSON object)", ParamKind.Code, CodeLanguage: "json",
                Help: "Example: { \"message\": \"{{input.stdout}}\" }", Required: true,
                DefaultValue: "{\n  \"message\": \"hello\"\n}"),
        ])
    {
        Guide = new CardGuide(
            "Builds a new piece of data (a JSON object) from the previous card's output, the Run input and fixed text. Use it to pick, rename or combine fields before the next card.",
            "Receives the previous card's output as input. Inside the JSON write {{input.field}} to insert a value; {{run.input}} inserts the Run input.",
            new JObject { ["message"] = "hello" },
            [
                new GuideExample("Build a message", "Put values into text. Several {{ }} in one string become text.",
                    new JObject { ["values"] = "{\n  \"message\": \"Hello {{input.json.name}}, you have {{input.json.count}} new items\"\n}" },
                    "{\n  \"message\": \"Hello {{input.json.name}}, you have {{input.json.count}} new items\"\n}", "json",
                    new JObject { ["json"] = new JObject { ["name"] = "Sok", ["count"] = 3 } },
                    "Output { message: \"Hello Sok, you have 3 new items\" }. The next card reads {{input.message}}."),
                new GuideExample("Keep a number a number", "A value that is ONLY one {{ }} keeps its type (number, true/false, object, list).",
                    new JObject { ["values"] = "{\n  \"total\": \"{{input.json.count}}\",\n  \"label\": \"items\"\n}" },
                    "{\n  \"total\": \"{{input.json.count}}\",\n  \"label\": \"items\"\n}", "json",
                    new JObject { ["json"] = new JObject { ["count"] = 3 } },
                    "total is the number 3 (not the text \"3\"), so an IF card can compare it as a number."),
                new GuideExample("Use the Run input", "{{run.input}} is the text or JSON typed in Run input, available in every card.",
                    new JObject { ["values"] = "{\n  \"question\": \"{{run.input}}\"\n}" },
                    "{\n  \"question\": \"{{run.input}}\"\n}", "json", RunInput: "What is the weather?", Result: "Output { question: \"What is the weather?\" }."),
            ],
            ["The text must be valid JSON: keys and text in double quotes.", "A missing field gives empty text instead of an error.", "Filters: {{input.items | json}} inserts a list as JSON text; {{input.text | sh}} quotes it for a shell command."]),
    };

    public Task<CardResult> ExecuteAsync(CardExecutionContext context, CancellationToken ct)
    {
        var text = ParamReader.GetString(context.Params, "values");
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Values is empty; enter a JSON object.");
        JToken parsed;
        try { parsed = JToken.Parse(text); }
        catch (JsonReaderException ex) { throw new ArgumentException($"Values is not valid JSON: {ex.Message}"); }
        if (parsed is not JObject) throw new ArgumentException("Values must be a JSON object.");
        return Task.FromResult<CardResult>(_templates.Resolve(parsed, context.TemplateContext));
    }
}
