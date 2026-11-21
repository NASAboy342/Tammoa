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
        [new CardParamInfo("seconds", "Seconds", ParamKind.Number, Required: true, DefaultValue: 1, Help: "0 to 3600; decimals allowed.")])
    {
        Guide = new CardGuide(
            "Pauses the flow, then continues. Handy to give a service time, or to space out calls.",
            "Receives the previous card's output and passes it on unchanged after the pause, so cards after it read the same {{input.…}} fields as if the Wait card were not there.",
            new JObject { ["json"] = new JObject { ["id"] = 7 } },
            [new GuideExample("Wait 5 seconds", "Set Seconds to 5. Cancelling the run stops the wait at once.", new JObject { ["seconds"] = 5 }, "5", "text", new JObject { ["json"] = new JObject { ["id"] = 7 } },
                "After 5 s the same object continues, e.g. {{input.json.id}} is still 7.")],
            ["Decimals work: 0.5 is half a second."]),
    };

    public async Task<CardResult> ExecuteAsync(CardExecutionContext context, CancellationToken ct)
    {
        var seconds = ParamReader.GetDouble(context.Params, "seconds", 1);
        if (double.IsNaN(seconds) || seconds < 0 || seconds > MaxSeconds) throw new ArgumentException($"Seconds must be between 0 and {MaxSeconds:0}.");
        await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
        return context.Input?.DeepClone() ?? new JObject();
    }
}
