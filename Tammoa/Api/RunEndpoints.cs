using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Tammoa.Helper;
using Tammoa.Models;
using Tammoa.Services;

namespace Tammoa.Api;

public static class RunEndpoints
{
    public static void MapRunEndpoints(this WebApplication app, IRunService service, IRunEventBus bus)
    {
        app.MapPost("/api/flows/{id}/runs", (string id, HttpRequest req) => EndpointHelpers.Try(async () =>
        {
            var body = await JsonHelper.ReadBodyAsync<StartRunRequest>(req); // an empty body is fine: no input
            return JsonHelper.Ok(service.Start(id, body?.Input), StatusCodes.Status202Accepted);
        }));

        app.MapGet("/api/flows/{id}/runs", (string id) => EndpointHelpers.TrySync(() => JsonHelper.Ok(service.ListForFlow(id))));
        app.MapGet("/api/runs/{id}", (string id) => EndpointHelpers.TrySync(() => JsonHelper.Ok(service.Get(id))));
        app.MapDelete("/api/runs/{id}", (string id) => EndpointHelpers.TrySync(() =>
        {
            service.Delete(id);
            return Results.NoContent();
        }));
        app.MapDelete("/api/flows/{id}/runs", (string id) => EndpointHelpers.TrySync(() => JsonHelper.Ok(new { deleted = service.Clear(id) })));
        app.MapPost("/api/runs/{id}/cancel", (string id) => EndpointHelpers.TrySync(() => JsonHelper.Ok(service.Cancel(id))));

        // Server-sent events: every change of any run, as a complete snapshot. The UI only displays what arrives here.
        app.MapGet("/api/events", async (HttpContext ctx) =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            var reader = bus.Subscribe(ctx.RequestAborted);
            try
            {
                await ctx.Response.WriteAsync(": connected\n\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                while (!ctx.RequestAborted.IsCancellationRequested)
                {
                    using var wait = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
                    wait.CancelAfter(TimeSpan.FromSeconds(15));
                    try
                    {
                        if (!await reader.WaitToReadAsync(wait.Token)) break;
                        while (reader.TryRead(out var e))
                        {
                            var data = e.Kind switch
                            {
                                "run" => JsonHelper.Serialize(e.Run),
                                "run-removed" => JsonHelper.Serialize(new { runId = e.RunId, flowId = e.FlowId }),
                                _ => JsonHelper.Serialize(new { flowId = e.FlowId, cardId = e.CardId }), // data-changed
                            };
                            await ctx.Response.WriteAsync($"event: {e.Kind}\ndata: {data}\n\n", ctx.RequestAborted);
                        }
                    }
                    catch (OperationCanceledException) when (!ctx.RequestAborted.IsCancellationRequested)
                    {
                        await ctx.Response.WriteAsync(": keep-alive\n\n", ctx.RequestAborted); // heartbeat keeps proxies and the WebView connection open
                    }
                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                }
            }
            catch (OperationCanceledException) { /* client went away */ }
        });
    }
}
