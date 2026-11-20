using System.Reflection;
using Tammoa.Models;

namespace Tammoa.Services;

public class AppInfoService : IAppInfoService
{
    public AppVersionInfo GetVersion()
    {
        var asm = typeof(AppInfoService).Assembly;
        var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? asm.GetName().Version?.ToString()
                      ?? "0.0.0";
        return new AppVersionInfo("Tammoa", version);
    }
}
