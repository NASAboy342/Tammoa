using Tammoa.Models;

namespace Tammoa.Services;

public interface ICardRegistry
{
    IReadOnlyList<CardTypeInfo> All();
    ICardType? Find(string type);
}
