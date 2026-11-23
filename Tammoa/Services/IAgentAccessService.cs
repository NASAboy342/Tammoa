using Tammoa.Models;

namespace Tammoa.Services;

/// <summary>The outcome of checking one request for agent access. `IsAgent` false = a normal UI request, let it through.</summary>
public record AgentCheck(bool IsAgent, bool Allowed, int Status, string? Error)
{
    public static readonly AgentCheck NotAgent = new(false, true, 200, null);
}

public interface IAgentAccessService
{
    /// <summary>Settings for the UI. The first call makes the token.</summary>
    AgentAccessInfo Info();
    AgentAccessInfo Update(UpdateAgentAccessRequest request);
    /// <summary>Makes a new token; the old one stops working at once.</summary>
    AgentAccessInfo RegenerateToken();
    /// <summary>`Authorization` header value (or null), HTTP method and path of a request.</summary>
    AgentCheck Check(string? authorization, string method, string path);
    /// <summary>The skill as Markdown: the file shipped with the app, plus the address, the routes and the card catalog of THIS running app.</summary>
    string RenderSkill();
}
