namespace Tammoa.Services;

/// <summary>One route an agent may call. `NeedsRunPermission`: also needs the user's "let the agent run flows" switch.</summary>
public record AgentRoute(string Method, string Pattern, string Summary, bool NeedsRunPermission = false);

/// <summary>A route an agent may NOT call, and why. Together with <see cref="AgentApi.Routes"/> it must cover every route of the app
/// (a test fails when a new endpoint is in neither list): adding an endpoint forces the question "should an AI agent be able to use it?".</summary>
public record AgentExcluded(string Method, string Pattern, string Reason);

/// <summary>The single list of what an AI agent (a request with `Authorization: Bearer <token>`) can do. The skill the app serves is generated from it.
/// A request WITHOUT that header is the app's own UI and is never restricted.</summary>
public static class AgentApi
{
    public static readonly IReadOnlyList<AgentRoute> Routes =
    [
        new("GET", "/api/health", "Check that Tammoa answers."),
        new("GET", "/api/agent/skill", "This skill (Markdown), always the version of the running app."),
        new("GET", "/api/cards/types", "Every card type with its parameters, outputs and guide: the catalog you build flows from."),
        new("GET", "/api/cards/options/{source}", "Choices of a dynamic Select parameter, e.g. `/api/cards/options/ollama.models?connectionId=ID` lists the models of a connection."),
        new("GET", "/api/connections", "The saved connections (id, name, provider, address; never the key). Cards refer to them by id."),
        new("GET", "/api/flows", "List the flows (no definitions)."),
        new("GET", "/api/flows/{id}", "One flow with its definition."),
        new("POST", "/api/flows", "Create an empty, disabled flow. Body `{ \"name\": \"…\", \"description\": \"…\" }`. Fill it with PUT."),
        new("PUT", "/api/flows/{id}", "Replace name, description and definition. Body `{ \"name\", \"description\", \"definition\": { \"nodes\": [], \"edges\": [] } }`. Invalid input gives 400 with the reason."),
        new("POST", "/api/flows/validate", "Dry check of a definition, nothing is saved. Body `{ \"definition\": { … } }`, answer `{ \"valid\": true }` or `{ \"valid\": false, \"error\": \"…\" }`."),
        new("POST", "/api/flows/{id}/duplicate", "Copy a flow (disabled)."),
        new("GET", "/api/flows/{id}/export", "The portable document of a flow."),
        new("POST", "/api/flows/import", "Create a flow from an exported document."),
        new("GET", "/api/flows/{id}/data/{cardId}", "The stored text of a Data card."),
        new("PUT", "/api/flows/{id}/data/{cardId}", "Save the text of a Data card. Body `{ \"text\": \"…\" }`."),
        new("POST", "/api/flows/{id}/runs", "Start a run (returns at once, status `running`). Body `{ \"input\": … }` is optional.", NeedsRunPermission: true),
        new("GET", "/api/flows/{id}/runs", "The runs of a flow, newest first."),
        new("GET", "/api/runs/{id}", "One run: its status and every card's output, error and timing. Poll it until the status is not `running`."),
        new("POST", "/api/runs/{id}/cancel", "Stop a running run."),
    ];

    public static readonly IReadOnlyList<AgentExcluded> NotForAgents =
    [
        new("DELETE", "/api/flows/{id}", "Deleting needs the user's decision (the UI asks to type the name)."),
        new("DELETE", "/api/flows/{id}/runs", "Run history belongs to the user."),
        new("DELETE", "/api/runs/{id}", "Run history belongs to the user."),
        new("POST", "/api/flows/{id}/enable", "Enabling is a deliberate choice of the user."),
        new("POST", "/api/flows/{id}/disable", "Same as enable."),
        new("GET", "/api/flows/{id}/data", "Covered by reading one Data card at a time."),
        new("GET", "/api/connections/providers", "Not needed to build flows."),
        new("GET", "/api/connections/{id}", "The list already has what an agent needs."),
        new("POST", "/api/connections", "Connections hold keys and addresses: only the user creates them."),
        new("PUT", "/api/connections/{id}", "Same as create."),
        new("DELETE", "/api/connections/{id}", "Same as create."),
        new("POST", "/api/connections/{id}/test", "Not needed to build flows."),
        new("GET", "/api/app/version", "Not needed to build flows."),
        new("GET", "/api/events", "A live stream for the UI; an agent polls the run instead."),
        new("GET", "/api/agent/access", "The token and the switches belong to the user."),
        new("PUT", "/api/agent/access", "The agent must not change its own permissions."),
        new("POST", "/api/agent/access/token", "The agent must not change its own token."),
    ];

    /// <summary>Route shape without the parameter names, so `{id}` and `{flowId}` compare equal.</summary>
    public static string Normalize(string pattern) =>
        string.Join('/', pattern.Split('/').Select(s => s.StartsWith('{') && s.EndsWith('}') ? "{}" : s));

    /// <summary>The allowed route a request matches, or null.</summary>
    public static AgentRoute? Find(string method, string path)
    {
        var segments = path.TrimEnd('/').Split('/');
        foreach (var r in Routes)
        {
            if (!string.Equals(r.Method, method, StringComparison.OrdinalIgnoreCase)) continue;
            var pattern = r.Pattern.Split('/');
            if (pattern.Length != segments.Length) continue;
            var ok = true;
            for (var i = 0; i < pattern.Length && ok; i++)
                ok = pattern[i].StartsWith('{') ? segments[i].Length > 0 : string.Equals(pattern[i], segments[i], StringComparison.OrdinalIgnoreCase);
            if (ok) return r;
        }
        return null;
    }
}
