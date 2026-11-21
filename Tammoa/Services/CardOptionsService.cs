using Tammoa.Models;

namespace Tammoa.Services;

public class CardOptionsService : ICardOptionsService
{
    private readonly IConnectionService _connections;
    private readonly IOllamaClient _ollama;

    public CardOptionsService(IConnectionService connections, IOllamaClient ollama)
    {
        _connections = connections;
        _ollama = ollama;
    }

    public async Task<IReadOnlyList<ParamOption>> GetAsync(string source, IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        switch (source)
        {
            case "ollama.models":
                if (!query.TryGetValue("connectionId", out var id) || string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Choose a connection first.");
                var models = await _ollama.ListModelsAsync(_connections.GetForUse(id), ct);
                return models.Select(m => new ParamOption(m, m)).ToList();
            default:
                throw new KeyNotFoundException($"Unknown options source '{source}'.");
        }
    }
}
