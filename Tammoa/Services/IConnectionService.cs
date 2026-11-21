using Tammoa.Models;

namespace Tammoa.Services;

public interface IConnectionService
{
    IReadOnlyList<ProviderInfo> Providers();
    IReadOnlyList<ConnectionInfo> List();

    /// <exception cref="KeyNotFoundException">404.</exception>
    ConnectionInfo Get(string id);

    /// <exception cref="ArgumentException">Blank/too long name, unknown provider, bad URL, key too long (400).</exception>
    ConnectionInfo Create(SaveConnectionRequest request);

    /// <exception cref="ArgumentException">As Create (400).</exception>
    /// <exception cref="KeyNotFoundException">404.</exception>
    ConnectionInfo Update(string id, SaveConnectionRequest request);

    /// <exception cref="InvalidOperationException">Cards in saved flows still use the connection (409).</exception>
    /// <exception cref="KeyNotFoundException">404.</exception>
    void Delete(string id);

    /// <summary>Contacts the service. Connectivity problems come back as Ok=false with a message; they do not throw.</summary>
    /// <exception cref="KeyNotFoundException">404.</exception>
    Task<ConnectionTestResult> TestAsync(string id, CancellationToken ct);

    /// <summary>Internal: the connection WITH its key, for cards. Never expose the result to the UI.</summary>
    /// <exception cref="KeyNotFoundException">No such connection.</exception>
    Connection GetForUse(string id);
}
