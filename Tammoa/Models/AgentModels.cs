using Newtonsoft.Json.Linq;

namespace Tammoa.Models;

/// <summary>What is kept on disk for the agent access. Off until the user switches it on; running flows needs its own switch.</summary>
public record AgentSettings(bool Enabled, bool AllowRun, string Token);

/// <summary>What the UI shows: the address an agent talks to, and the token it must send.</summary>
public record AgentAccessInfo(bool Enabled, bool AllowRun, string Url, string Token);

public record UpdateAgentAccessRequest(bool? Enabled, bool? AllowRun);

public record ValidateFlowRequest(JObject? Definition);

/// <summary>Result of a dry check of a flow definition (nothing is saved).</summary>
public record FlowValidation(bool Valid, string? Error);
