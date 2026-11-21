using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Models;

namespace Tammoa.Services.Cards;

/// <summary>When several connections feed a card, its input is an array of their outputs. Merge combines those objects into one
/// (later connections win on equal keys).</summary>
public class MergeCard : ICardType
{
    public const string TypeId = "data.merge";

    public CardTypeInfo Info { get; } = new(
        TypeId, "Merge", "Combines the outputs of several incoming connections into one object (later ones win on equal keys).",
        CardCategory.Data, "merge", [])
    {
        Guide = new CardGuide(
            "Combines the outputs of several cards into one object. Connect two or more cards to its input.",
            "When several connections are active, the card's input is a list of their outputs, in the order the connections were made. Merge joins those objects into one; when two have the same field, the later one wins.",
            new JObject { ["name"] = "Sok", ["city"] = "Phnom Penh" },
            [new GuideExample("Join two Set value cards", "Connect \"Set value A\" and \"Set value B\" into Merge.", Code: "A: { \"name\": \"Sok\" }\nB: { \"city\": \"Phnom Penh\" }", Language: "text",
                SampleInput: new JArray(new JObject { ["name"] = "Sok" }, new JObject { ["city"] = "Phnom Penh" }),
                Result: "Output { name: \"Sok\", city: \"Phnom Penh\" }. The next card reads {{input.name}} and {{input.city}}.")],
            ["Every incoming output must be an object.", "Without Merge, a card with two inputs gets a list: {{input[0].name}}, {{input[1].city}}."]),
    };

    public Task<CardResult> ExecuteAsync(CardExecutionContext context, CancellationToken ct)
    {
        switch (context.Input)
        {
            case JObject single:
                return Task.FromResult<CardResult>((JObject)single.DeepClone());
            case JArray items:
                var merged = new JObject();
                foreach (var item in items)
                {
                    if (item is not JObject o) throw new ArgumentException("Merge needs every incoming output to be an object.");
                    merged.Merge(o.DeepClone(), new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace, MergeNullValueHandling = MergeNullValueHandling.Merge });
                }
                return Task.FromResult<CardResult>(merged);
            default:
                throw new ArgumentException("Merge needs object input.");
        }
    }
}
