using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Tammoa.Helper;
using Tammoa.Models;
using Tammoa.Services;

namespace Tammoa.Api;

public static class FlowEndpoints
{
    public static void MapFlowEndpoints(this WebApplication app, IFlowService service)
    {
        app.MapGet("/api/flows", () => EndpointHelpers.TrySync(() => JsonHelper.Ok(service.List())));

        app.MapPost("/api/flows/validate", (HttpRequest req) => EndpointHelpers.Try(async () =>
        {
            var body = await JsonHelper.ReadBodyAsync<ValidateFlowRequest>(req) ?? throw new ArgumentException("A request body is required.");
            return JsonHelper.Ok(service.Validate(body.Definition));
        }));
        app.MapGet("/api/flows/{id}/export", (string id) => EndpointHelpers.TrySync(() => JsonHelper.Ok(service.Export(id))));

        app.MapPost("/api/flows/import", (HttpRequest req) => EndpointHelpers.Try(async () =>
        {
            var body = await JsonHelper.ReadBodyAsync<FlowDocument>(req) ?? throw new ArgumentException("A request body is required.");
            return JsonHelper.Ok(service.Import(body), StatusCodes.Status201Created);
        }));

        app.MapGet("/api/flows/{id}", (string id) => EndpointHelpers.TrySync(() => JsonHelper.Ok(service.Get(id))));

        app.MapPost("/api/flows", (HttpRequest req) => EndpointHelpers.Try(async () =>
        {
            var body = await JsonHelper.ReadBodyAsync<CreateFlowRequest>(req) ?? throw new ArgumentException("A request body is required.");
            return JsonHelper.Ok(service.Create(body), StatusCodes.Status201Created);
        }));

        app.MapPut("/api/flows/{id}", (string id, HttpRequest req) => EndpointHelpers.Try(async () =>
        {
            var body = await JsonHelper.ReadBodyAsync<UpdateFlowRequest>(req) ?? throw new ArgumentException("A request body is required.");
            return JsonHelper.Ok(service.Update(id, body));
        }));

        app.MapDelete("/api/flows/{id}", (string id, HttpRequest req) => EndpointHelpers.Try(async () =>
        {
            var body = await JsonHelper.ReadBodyAsync<DeleteFlowRequest>(req);
            service.Delete(id, body?.ConfirmName);
            return Results.NoContent();
        }));

        app.MapPost("/api/flows/{id}/enable", (string id) => EndpointHelpers.TrySync(() => JsonHelper.Ok(service.SetEnabled(id, true))));
        app.MapPost("/api/flows/{id}/disable", (string id) => EndpointHelpers.TrySync(() => JsonHelper.Ok(service.SetEnabled(id, false))));
        app.MapPost("/api/flows/{id}/duplicate", (string id) => EndpointHelpers.TrySync(() => JsonHelper.Ok(service.Duplicate(id), StatusCodes.Status201Created)));
    }
}
