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
        CardCategory.Data, "merge", []);

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
