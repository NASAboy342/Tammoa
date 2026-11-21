using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Tammoa.Helper;
using Tammoa.Models;
using Tammoa.Services;

namespace Tammoa.Api;

public static class DataEndpoints
{
    public static void MapDataEndpoints(this WebApplication app, IDataService service)
    {
        app.MapGet("/api/flows/{id}/data", (string id) => EndpointHelpers.TrySync(() => JsonHelper.Ok(service.List(id))));

        app.MapGet("/api/flows/{id}/data/{cardId}", (string id, string cardId) => EndpointHelpers.TrySync(() => JsonHelper.Ok(service.Read(id, cardId))));

        app.MapPut("/api/flows/{id}/data/{cardId}", (string id, string cardId, HttpRequest req) => EndpointHelpers.Try(async () =>
        {
            var body = await JsonHelper.ReadBodyAsync<SaveDataRequest>(req) ?? throw new ArgumentException("A request body is required.");
            return JsonHelper.Ok(service.Write(id, cardId, body.Text));
        }));
    }
}
