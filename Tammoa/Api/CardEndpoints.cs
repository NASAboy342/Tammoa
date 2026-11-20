using Microsoft.AspNetCore.Builder;
using Tammoa.Helper;
using Tammoa.Services;

namespace Tammoa.Api;

public static class CardEndpoints
{
    public static void MapCardEndpoints(this WebApplication app, ICardRegistry registry)
    {
        app.MapGet("/api/cards/types", () => EndpointHelpers.TrySync(() => JsonHelper.Ok(registry.All())));
    }
}
