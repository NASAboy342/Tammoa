using Tammoa.Models;

namespace Tammoa.Services;

public interface IAppInfoService
{
    /// <summary>Returns the app name and informational version (from the assembly).</summary>
    AppVersionInfo GetVersion();
}
