using Tammoa.Models;

namespace Tammoa.Services;

/// <summary>Asks GitHub for the latest published release of Tammoa and compares it with the running version. Never throws for connectivity problems:
/// the reason comes back in <see cref="UpdateInfo.Error"/>, so a missing internet connection never disturbs the app.</summary>
public interface IUpdateService
{
    Task<UpdateInfo> CheckAsync(CancellationToken ct);
}
