using Tammoa.Models;

namespace Tammoa.Services;

/// <summary>Supplies the choices of dynamic Select params (e.g. the models a connection offers).</summary>
public interface ICardOptionsService
{
    /// <exception cref="KeyNotFoundException">Unknown source or connection (404).</exception>
    /// <exception cref="ArgumentException">Missing query value (400).</exception>
    /// <exception cref="InvalidOperationException">The upstream service could not be reached (409, readable message).</exception>
    Task<IReadOnlyList<ParamOption>> GetAsync(string source, IReadOnlyDictionary<string, string> query, CancellationToken ct);
}
