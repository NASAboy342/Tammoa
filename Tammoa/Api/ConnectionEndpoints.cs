using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Tammoa.Helper;
using Tammoa.Models;
using Tammoa.Services;

namespace Tammoa.Api;

public static class ConnectionEndpoints
{
    public static void MapConnectionEndpoints(this WebApplication app, IConnectionService service)
    {
        app.MapGet("/api/connections/providers", () => EndpointHelpers.TrySync(() => JsonHelper.Ok(service.Providers())));
        app.MapGet("/api/connections", () => EndpointHelpers.TrySync(() => JsonHelper.Ok(service.List())));
        app.MapGet("/api/connections/{id}", (string id) => EndpointHelpers.TrySync(() => JsonHelper.Ok(service.Get(id))));

        app.MapPost("/api/connections", (HttpRequest req) => EndpointHelpers.Try(async () =>
        {
            var body = await JsonHelper.ReadBodyAsync<SaveConnectionRequest>(req) ?? throw new ArgumentException("A request body is required.");
            return JsonHelper.Ok(service.Create(body), StatusCodes.Status201Created);
        }));

        app.MapPut("/api/connections/{id}", (string id, HttpRequest req) => EndpointHelpers.Try(async () =>
        {
            var body = await JsonHelper.ReadBodyAsync<SaveConnectionRequest>(req) ?? throw new ArgumentException("A request body is required.");
            return JsonHelper.Ok(service.Update(id, body));
        }));

        app.MapDelete("/api/connections/{id}", (string id) => EndpointHelpers.TrySync(() =>
        {
            service.Delete(id);
            return Results.NoContent();
        }));

        app.MapPost("/api/connections/{id}/test", (string id, HttpContext ctx) => EndpointHelpers.Try(async () =>
            JsonHelper.Ok(await service.TestAsync(id, ctx.RequestAborted))));
    }
}
