using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Photino.NET;
using Photino.NET.Server;
using Tammoa.Api;
using Tammoa.Helper;
using Tammoa.Repositories;
using Tammoa.Services;
using Tammoa.Services.Cards;
using Tammoa.Services.Tools;

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

        DataPaths.RestrictToOwner(DataPaths.DataDir()); // the database holds connection keys
        var services = new ServiceCollection();
        services.AddSingleton<IAppInfoService, AppInfoService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IFlowRepository>(_ => new FlowRepository(DataPaths.DatabaseFile()));
        services.AddSingleton<IFlowService, FlowService>();
        services.AddSingleton<ITemplateEngine, TemplateEngine>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<ICardType, ManualTriggerCard>();
        services.AddSingleton<ICardType, SetValueCard>();
        services.AddSingleton<ICardType, ScriptCard>();
        services.AddSingleton<ICardType, IfCard>();
        services.AddSingleton<ICardType, WaitCard>();
        services.AddSingleton<ICardType, HttpRequestCard>();
        services.AddSingleton<ICardType, JsonCard>();
        services.AddSingleton<ICardType, MergeCard>();
        services.AddSingleton<ICardType, ReadFileCard>();
        services.AddSingleton<ICardType, WriteFileCard>();
        services.AddSingleton<ICardType, OllamaCard>();
        services.AddSingleton<ICardType, DataStoreCard>();
        services.AddSingleton<IDataRepository>(_ => new DataRepository(DataPaths.DataDir()));
        services.AddSingleton<IDataService, DataService>();
        services.AddSingleton(_ => new HttpClient(new SocketsHttpHandler { AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = Timeout.InfiniteTimeSpan }); // cards enforce their own timeouts
        services.AddSingleton<IHttpRequestRunner, HttpRequestRunner>();
        services.AddSingleton<IOllamaClient, OllamaClient>();
        services.AddSingleton<ITool, ReadFileTool>();
        services.AddSingleton<ITool, WriteFileTool>();
        services.AddSingleton<ITool, HttpRequestTool>();
        services.AddSingleton<ITool, RunScriptTool>();
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<IToolLoop, ToolLoop>();
        services.AddSingleton<IConnectionRepository>(_ => new ConnectionRepository(DataPaths.DatabaseFile()));
        services.AddSingleton<IConnectionService, ConnectionService>();
        services.AddSingleton<ICardOptionsService, CardOptionsService>();
        services.AddSingleton<ICardRegistry, CardRegistry>();
        services.AddSingleton<IFlowDefinitionValidator, FlowDefinitionValidator>();
        services.AddSingleton<IFlowExecutor, FlowExecutor>();
        services.AddSingleton<IRunRepository, RunRepository>();
        services.AddSingleton<IRunEventBus, RunEventBus>();
        services.AddSingleton<IRunService, RunService>();
        using var provider = services.BuildServiceProvider();

        app.MapAppEndpoints(provider.GetRequiredService<IAppInfoService>());
        app.MapFlowEndpoints(provider.GetRequiredService<IFlowService>());
        app.MapCardEndpoints(provider.GetRequiredService<ICardRegistry>(), provider.GetRequiredService<ICardOptionsService>());
        app.MapConnectionEndpoints(provider.GetRequiredService<IConnectionService>());
        app.MapDataEndpoints(provider.GetRequiredService<IDataService>());
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
