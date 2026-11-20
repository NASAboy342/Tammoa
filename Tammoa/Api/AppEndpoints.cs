using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Tammoa.Helper;
using Tammoa.Services;

namespace Tammoa.Api;

public static class AppEndpoints
{
    public static void MapAppEndpoints(this WebApplication app, IAppInfoService service)
    {
        app.MapGet("/api/health", () => JsonHelper.Ok(new { status = "ok" }));
        app.MapGet("/api/app/version", () => EndpointHelpers.TrySync(() => JsonHelper.Ok(service.GetVersion())));
    }
}
