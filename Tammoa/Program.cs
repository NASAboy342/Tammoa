using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Photino.NET;
using Photino.NET.Server;
using Tammoa.Api;
using Tammoa.Services;

namespace Tammoa;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Packaged apps are not launched from their own folder; the static server resolves wwwroot from the cwd.
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        // This already built the host, so its DI container is sealed: app services get their own container.
        var app = PhotinoServer.CreateStaticFileServer(args, out string baseUrl);
        app.Urls.Add("http://localhost:8000");

        var services = new ServiceCollection();
        services.AddSingleton<IAppInfoService, AppInfoService>();
        using var provider = services.BuildServiceProvider();

        app.MapAppEndpoints(provider.GetRequiredService<IAppInfoService>());

#if DEBUG
        var appUrl = "http://localhost:5173"; // Vite dev server (hot reload)
#else
        // The ?v= cache-buster: the WebView HTTP cache survives restarts and would serve a stale index.html.
        var appUrl = $"{baseUrl}/index.html?v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
#endif

        var window = new PhotinoWindow()
            .SetTitle("Tammoa")
            .SetIconFile(Path.Combine(AppContext.BaseDirectory, "AppIcon", "app.ico"))
            .SetSize(new System.Drawing.Size(1280, 800))
            .Center()
            .SetResizable(true);

        app.RunAsync();
        window.Load(appUrl);
        window.WaitForClose();
    }
}
