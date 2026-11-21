using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Services.Cards;

public class WaitCard : ICardType
{
    public const string TypeId = "flow.wait";
    public const double MaxSeconds = 3600;

    public CardTypeInfo Info { get; } = new(
        TypeId, "Wait", "Pauses the flow for a while, then passes its input through. Cancelling the run stops the wait.",
        CardCategory.Logic, "clock",
        [new CardParamInfo("seconds", "Seconds", ParamKind.Number, Required: true, DefaultValue: 1, Help: "0 to 3600; decimals allowed.")]);

    public async Task<CardResult> ExecuteAsync(CardExecutionContext context, CancellationToken ct)
    {
        var seconds = ParamReader.GetDouble(context.Params, "seconds", 1);
        if (double.IsNaN(seconds) || seconds < 0 || seconds > MaxSeconds) throw new ArgumentException($"Seconds must be between 0 and {MaxSeconds:0}.");
        await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
        return context.Input?.DeepClone() ?? new JObject();
    }
}
