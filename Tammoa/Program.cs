using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Photino.NET;
using Photino.NET.Server;
using Tammoa.Api;
using Tammoa.Helper;
using Tammoa.Repositories;
using Tammoa.Services;
using Tammoa.Services.Cards;

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
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IFlowRepository>(_ => new FlowRepository(DataPaths.DatabaseFile()));
        services.AddSingleton<IFlowService, FlowService>();
        services.AddSingleton<ITemplateEngine, TemplateEngine>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<ICardType, ManualTriggerCard>();
        services.AddSingleton<ICardType, SetValueCard>();
        services.AddSingleton<ICardType, ShellScriptCard>();
        services.AddSingleton<ICardRegistry, CardRegistry>();
        services.AddSingleton<IFlowDefinitionValidator, FlowDefinitionValidator>();
        services.AddSingleton<IFlowExecutor, FlowExecutor>();
        services.AddSingleton<IRunRepository, RunRepository>();
        services.AddSingleton<IRunEventBus, RunEventBus>();
        services.AddSingleton<IRunService, RunService>();
        using var provider = services.BuildServiceProvider();

        app.MapAppEndpoints(provider.GetRequiredService<IAppInfoService>());
        app.MapFlowEndpoints(provider.GetRequiredService<IFlowService>());
        app.MapCardEndpoints(provider.GetRequiredService<ICardRegistry>());
        app.MapRunEndpoints(provider.GetRequiredService<IRunService>(), provider.GetRequiredService<IRunEventBus>());

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
