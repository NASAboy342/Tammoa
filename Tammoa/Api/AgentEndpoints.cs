using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Tammoa.Helper;
using Tammoa.Models;
using Tammoa.Services;

namespace Tammoa.Api;

public static class AgentEndpoints
{
    public static void MapAgentEndpoints(this WebApplication app, IAgentAccessService service)
    {
        // For the UI (no token needed; an agent is refused by the middleware).
        app.MapGet("/api/agent/access", () => EndpointHelpers.TrySync(() => JsonHelper.Ok(service.Info())));
        app.MapPut("/api/agent/access", (HttpRequest req) => EndpointHelpers.Try(async () =>
        {
            var body = await JsonHelper.ReadBodyAsync<UpdateAgentAccessRequest>(req) ?? throw new ArgumentException("A request body is required.");
            return JsonHelper.Ok(service.Update(body));
        }));
        app.MapPost("/api/agent/access/token", () => EndpointHelpers.TrySync(() => JsonHelper.Ok(service.RegenerateToken())));
        // The skill: the UI copies it without a token; an agent reads it with one.
        app.MapGet("/api/agent/skill", () => EndpointHelpers.TrySync(() => Results.Text(service.RenderSkill(), "text/markdown; charset=utf-8")));
    }
}
