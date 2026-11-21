using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Services.Cards;

/// <summary>Branches the flow: connections from the "true" handle run when the condition holds, from "false" otherwise.
/// The card passes its input through unchanged.</summary>
public class IfCard : ICardType
{
    public const string TypeId = "logic.if";
    private readonly ITemplateEngine _templates;

    public IfCard(ITemplateEngine templates) => _templates = templates;

    public CardTypeInfo Info { get; } = new(
        TypeId, "IF",
        "Splits the flow in two: the true output runs when the condition holds, the false output otherwise. Passes its input through.",
        CardCategory.Logic, "split",
        [
            new CardParamInfo("value1", "Value", ParamKind.Text, Required: true, Placeholder: "{{input.json.status}}"),
            new CardParamInfo("operator", "Is", ParamKind.Select, Required: true, DefaultValue: "equals", Options:
            [
                new("equals", "equal to"), new("notEquals", "not equal to"), new("contains", "contains"), new("notContains", "does not contain"),
                new("startsWith", "starts with"), new("endsWith", "ends with"), new("greaterThan", "greater than (numbers)"),
                new("lessThan", "less than (numbers)"), new("isEmpty", "empty"), new("isNotEmpty", "not empty"), new("matches", "matches regex"),
            ]),
            new CardParamInfo("value2", "Compared with", ParamKind.Text, Placeholder: "ok", Help: "Ignored for empty / not empty."),
        ])
    {
        Outputs = [new CardOutputInfo("true", "true"), new CardOutputInfo("false", "false")],
        Guide = new CardGuide(
            "Decides which way the flow goes. If the condition holds, the cards connected to the TRUE output run; otherwise the ones connected to FALSE. The other side is skipped.",
            "Receives the previous card's output as input and passes it on unchanged to whichever side runs. Write the value to test with a template, e.g. {{input.json.status}}.",
            new JObject { ["json"] = new JObject { ["status"] = "ok" } },
            [
                new GuideExample("Is the status \"ok\"?", "Compare a field of the previous card's output with a word.",
                    new JObject { ["value1"] = "{{input.json.status}}", ["operator"] = "equals", ["value2"] = "ok" },
                    "{{input.json.status}}  is  equal to  ok", "text", new JObject { ["json"] = new JObject { ["status"] = "ok" } },
                    "TRUE side runs. The input is passed on unchanged, so the next card still reads {{input.json.status}}."),
                new GuideExample("Is the number above a limit?", "Numbers: greater than / less than compare as numbers (text that is not a number is an error).",
                    new JObject { ["value1"] = "{{input.json.count}}", ["operator"] = "greaterThan", ["value2"] = "10" },
                    "{{input.json.count}}  is  greater than (numbers)  10", "text", new JObject { ["json"] = new JObject { ["count"] = 12 } },
                    "12 > 10, so TRUE runs."),
                new GuideExample("Did the previous card print anything?", "Use \"not empty\" to check that a value exists.",
                    new JObject { ["value1"] = "{{input.stdout}}", ["operator"] = "isNotEmpty" },
                    "{{input.stdout}}  is  not empty", "text", new JObject { ["stdout"] = "3 files\n" }, "TRUE runs when the script printed something."),
            ],
            ["Connect the true and false handles of the card to different cards. Drag from the dot labelled TRUE or FALSE.", "To test the Run input use {{run.input}}.", "\"matches\" takes a regular expression, e.g. ^order-\\d+$"]),
    };

    public Task<CardResult> ExecuteAsync(CardExecutionContext context, CancellationToken ct)
    {
        var ctx = context.TemplateContext;
        var left = _templates.Render(ParamReader.GetString(context.Params, "value1"), ctx);
        var right = _templates.Render(ParamReader.GetString(context.Params, "value2"), ctx);
        var op = ParamReader.GetString(context.Params, "operator");
        if (op.Length == 0) op = "equals";

        var result = op switch
        {
            "equals" => left == right,
            "notEquals" => left != right,
            "contains" => left.Contains(right, StringComparison.Ordinal),
            "notContains" => !left.Contains(right, StringComparison.Ordinal),
            "startsWith" => left.StartsWith(right, StringComparison.Ordinal),
            "endsWith" => left.EndsWith(right, StringComparison.Ordinal),
            "greaterThan" => Numbers(left, right) is var (a, b) && a > b,
            "lessThan" => Numbers(left, right) is var (c, d) && c < d,
            "isEmpty" => left.Trim().Length == 0,
            "isNotEmpty" => left.Trim().Length > 0,
            "matches" => Matches(left, right),
            _ => throw new ArgumentException($"Unknown operator '{op}'."),
        };
        return Task.FromResult(new CardResult(context.Input?.DeepClone() ?? new JObject(), result ? "true" : "false"));
    }

    private static (double, double) Numbers(string left, string right)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (!double.TryParse(left.Trim(), System.Globalization.NumberStyles.Float, inv, out var a))
            throw new ArgumentException($"'{left}' is not a number.");
        if (!double.TryParse(right.Trim(), System.Globalization.NumberStyles.Float, inv, out var b))
            throw new ArgumentException($"'{right}' is not a number.");
        return (a, b);
    }

    private static bool Matches(string text, string pattern)
    {
        try { return Regex.IsMatch(text, pattern, RegexOptions.None, TimeSpan.FromSeconds(1)); }
        catch (ArgumentException ex) { throw new ArgumentException($"Invalid regular expression: {ex.Message}"); }
        catch (RegexMatchTimeoutException) { throw new InvalidOperationException("The regular expression took too long."); }
    }
}
