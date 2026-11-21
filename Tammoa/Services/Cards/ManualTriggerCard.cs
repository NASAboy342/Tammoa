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
        CardCategory.Trigger, "play", []);

    public Task<CardResult> ExecuteAsync(CardExecutionContext context, CancellationToken ct) =>
        Task.FromResult<CardResult>(new JObject { ["input"] = context.RunInput?.DeepClone() ?? JValue.CreateNull() });
}
