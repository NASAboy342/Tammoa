using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Helper;
using Tammoa.Models;
using Tammoa.Repositories;

namespace Tammoa.Services;

public class ConnectionService : IConnectionService
{
    public const int MaxNameLength = 100;
    public const int MaxKeyLength = 2000;

    private readonly IConnectionRepository _connections;
    private readonly IFlowRepository _flows;
    private readonly IOllamaClient _ollama;
    private readonly TimeProvider _time;

    public ConnectionService(IConnectionRepository connections, IFlowRepository flows, IOllamaClient ollama, TimeProvider time)
    {
        _connections = connections;
        _flows = flows;
        _ollama = ollama;
        _time = time;
    }

    public IReadOnlyList<ProviderInfo> Providers() =>
    [
        new ProviderInfo(ConnectionProvider.Ollama, "Ollama", "http://localhost:11434", false,
            "Optional. Leave empty for a normal Ollama server; set it only if the server sits behind a proxy that expects a Bearer token."),
    ];

    public IReadOnlyList<ConnectionInfo> List() => _connections.List().Select(ToInfo).ToList();

    public ConnectionInfo Get(string id) => ToInfo(GetForUse(id));

    public Connection GetForUse(string id) => _connections.Get(id) ?? throw new KeyNotFoundException($"Connection '{id}' was not found.");

    public ConnectionInfo Create(SaveConnectionRequest r)
    {
        var now = _time.GetUtcNow();
        var key = r.ApiKey?.Trim();
        var connection = new Connection(
            Guid.NewGuid().ToString("N"),
            ValidName(r.Name),
            r.Provider ?? throw new ArgumentException("Choose a provider."),
            ValidUrl(r.BaseUrl),
            string.IsNullOrEmpty(key) ? null : ValidKey(key),
            now, now);
        _connections.Insert(connection);
        return ToInfo(connection);
    }

    public bool Restore(BackupConnection b)
    {
        if (!BackupIds.IsValid(b.Id)) throw new ArgumentException($"'{b.Id}' is not a valid connection id.");
        if (_connections.Get(b.Id) is not null) return false;
        var now = _time.GetUtcNow();
        _connections.Insert(new Connection(b.Id, ValidName(b.Name), b.Provider, ValidUrl(b.BaseUrl), null, now, now));
        return true;
    }

    public ConnectionInfo Update(string id, SaveConnectionRequest r)
    {
        var existing = GetForUse(id);
        var key = existing.ApiKey;
        if (r.ClearApiKey) key = null;
        else if (!string.IsNullOrWhiteSpace(r.ApiKey)) key = ValidKey(r.ApiKey.Trim());

        var updated = existing with
        {
            Name = ValidName(r.Name),
            Provider = r.Provider ?? existing.Provider,
            BaseUrl = ValidUrl(r.BaseUrl),
            ApiKey = key,
            UpdatedAt = _time.GetUtcNow(),
        };
        if (!_connections.Update(updated)) throw new KeyNotFoundException($"Connection '{id}' was not found.");
        return ToInfo(updated);
    }

    public void Delete(string id)
    {
        var existing = GetForUse(id);
        var users = _flows.List().Where(f => UsesConnection(f.Definition, id)).Select(f => f.Name).ToList();
        if (users.Count > 0)
            throw new InvalidOperationException($"'{existing.Name}' is used by cards in: {string.Join(", ", users.Take(5))}{(users.Count > 5 ? ", …" : "")}. Remove or change those cards first.");
        _connections.Delete(id);
    }

    public async Task<ConnectionTestResult> TestAsync(string id, CancellationToken ct)
    {
        var c = GetForUse(id);
        try
        {
            var version = await _ollama.GetVersionAsync(c, ct);
            var models = await _ollama.ListModelsAsync(c, ct);
            return new ConnectionTestResult(true, version, models, null);
        }
        catch (InvalidOperationException ex)
        {
            return new ConnectionTestResult(false, null, [], ex.Message);
        }
    }

    private static bool UsesConnection(JObject definition, string id) =>
        (definition["nodes"] as JArray ?? new JArray()).OfType<JObject>()
            .Any(n => n["params"] is JObject p && p.Properties().Any(prop => prop.Value.Type == JTokenType.String && (string?)prop.Value == id));

    private static ConnectionInfo ToInfo(Connection c) => new(c.Id, c.Name, c.Provider, c.BaseUrl, !string.IsNullOrEmpty(c.ApiKey), c.CreatedAt, c.UpdatedAt);

    private static string ValidName(string? name)
    {
        var t = name?.Trim();
        if (string.IsNullOrEmpty(t)) throw new ArgumentException("A connection needs a name.");
        if (t.Length > MaxNameLength) throw new ArgumentException($"The name is limited to {MaxNameLength} characters.");
        return t;
    }

    private static string ValidUrl(string? url)
    {
        var t = url?.Trim();
        if (string.IsNullOrEmpty(t)) throw new ArgumentException("A connection needs a URL.");
        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("The URL must be a full http(s) address, e.g. http://localhost:11434.");
        return t.TrimEnd('/');
    }

    private static string ValidKey(string key) =>
        key.Length > MaxKeyLength ? throw new ArgumentException($"The key is limited to {MaxKeyLength} characters.") : key;
}
