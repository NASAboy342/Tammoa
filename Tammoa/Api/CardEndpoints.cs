using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Tammoa.Helper;
using Tammoa.Services;

namespace Tammoa.Api;

public static class CardEndpoints
{
    public static void MapCardEndpoints(this WebApplication app, ICardRegistry registry, ICardOptionsService options)
    {
        app.MapGet("/api/cards/types", () => EndpointHelpers.TrySync(() => JsonHelper.Ok(registry.All())));

        // Choices of a dynamic Select param, e.g. /api/cards/options/ollama.models?connectionId=…
        app.MapGet("/api/cards/options/{source}", (string source, HttpContext ctx) => EndpointHelpers.Try(async () =>
        {
            var query = ctx.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString());
            return JsonHelper.Ok(await options.GetAsync(source, query, ctx.RequestAborted));
        }));
    }
}
