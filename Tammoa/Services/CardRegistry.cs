using Tammoa.Models;

namespace Tammoa.Services;

public class CardRegistry : ICardRegistry
{
    private readonly Dictionary<string, ICardType> _byType;

    public CardRegistry(IEnumerable<ICardType> cards) => _byType = cards.ToDictionary(c => c.Info.Type);

    public IReadOnlyList<CardTypeInfo> All() => _byType.Values.Select(c => c.Info).OrderBy(i => i.Category).ThenBy(i => i.Name).ToList();

    public ICardType? Find(string type) => _byType.GetValueOrDefault(type);
}
