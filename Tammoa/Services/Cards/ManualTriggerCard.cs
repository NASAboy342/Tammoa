using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Models;

namespace Tammoa.Services.Cards;

public class ManualTriggerCard : ICardType
{
    public const string TypeId = "trigger.manual";

    public CardTypeInfo Info { get; } = new(
        TypeId, "Manual trigger",
        "Starts the flow when you press Run. Output: { input } = the value passed with the run.",
        CardCategory.Trigger, "play", [])
    {
        Guide = new CardGuide(
            "Every flow starts here. Press Run and whatever you typed in the Run input box is handed to the next card.",
            "A trigger takes no input from another card. It receives the Run input: the text or JSON typed in the Run input box (empty if you typed nothing).",
            new JObject { ["input"] = "Summarise this please" },
            [
                new GuideExample("Pass some text", "Type a sentence in Run input. The next card reads it as {{input.input}}.",
                    Code: "{{input.input}}", Language: "text", SampleInput: "Summarise this please",
                    Result: "The trigger outputs { input: \"Summarise this please\" }. In the next card use {{input.input}} (or, from any card, {{run.input}})."),
                new GuideExample("Pass JSON", "Type valid JSON in Run input and its fields are available by name.",
                    Code: "{{input.input.city}}", Language: "text", SampleInput: new JObject { ["city"] = "Phnom Penh", ["days"] = 3 },
                    Result: "With Run input {\"city\":\"Phnom Penh\",\"days\":3}: {{input.input.city}} is Phnom Penh and {{input.input.days}} is 3 (a number)."),
            ],
            ["The Run input is also available in EVERY card as {{run.input}}, even far down the flow.", "Text that is valid JSON (5, true, {\"a\":1}) keeps its type; anything else stays text."]),
    };

    public Task<CardResult> ExecuteAsync(CardExecutionContext context, CancellationToken ct) =>
        Task.FromResult<CardResult>(new JObject { ["input"] = context.RunInput?.DeepClone() ?? JValue.CreateNull() });
}
