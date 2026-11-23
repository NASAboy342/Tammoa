using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Tammoa.Helper;
using Tammoa.Services;

namespace Tammoa.Api;

/// <summary>Scopes requests that carry `Authorization: Bearer …` (an AI agent) to the agent routes. Requests without it (the app's own UI) pass untouched.</summary>
public static class AgentAccessMiddleware
{
    public static void UseAgentAccess(this WebApplication app, IAgentAccessService service) =>
        app.Use((ctx, next) => InvokeAsync(ctx, next, service));

    public static async Task InvokeAsync(HttpContext ctx, RequestDelegate next, IAgentAccessService service)
    {
        if (!ctx.Request.Path.StartsWithSegments("/api")) { await next(ctx); return; }
        var check = service.Check(ctx.Request.Headers.Authorization.ToString(), ctx.Request.Method, ctx.Request.Path.Value ?? "");
        if (!check.IsAgent || check.Allowed) { await next(ctx); return; }
        ctx.Response.StatusCode = check.Status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.WriteAsync(JsonHelper.Serialize(new { error = check.Error }));
    }
}
