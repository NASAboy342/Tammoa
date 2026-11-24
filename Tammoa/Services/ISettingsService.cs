using Tammoa.Models;

namespace Tammoa.Services;

public interface ISettingsService
{
    /// <summary>Defaults when nothing was saved: no automatic update check.</summary>
    AppSettings Get();
    AppSettings Update(SaveSettingsRequest request);
}
