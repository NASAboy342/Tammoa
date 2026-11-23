using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Tammoa.Models;
using Tammoa.Repositories;

namespace Tammoa.Services;

public class AgentAccessService : IAgentAccessService
{
    public const string ResourceName = "AgentSkill.SKILL.md";
    private readonly IAgentAccessRepository _repo;
    private readonly ICardRegistry _cards;
    private readonly string _apiUrl;
    private readonly Func<string> _skillTemplate;
    private readonly object _lock = new();
    private AgentSettings? _cache;

    public AgentAccessService(IAgentAccessRepository repo, ICardRegistry cards, string apiUrl, Func<string>? skillTemplate = null)
    {
        _repo = repo;
        _cards = cards;
        _apiUrl = apiUrl.TrimEnd('/');
        _skillTemplate = skillTemplate ?? LoadShippedSkill;
    }

    public AgentAccessInfo Info() { lock (_lock) return ToInfo(Current()); }

    public AgentAccessInfo Update(UpdateAgentAccessRequest request)
    {
        lock (_lock)
        {
            var s = Current();
            var enabled = request.Enabled ?? s.Enabled;
            var allowRun = enabled && (request.AllowRun ?? s.AllowRun); // running only makes sense while access is on; switching access off also switches running off
            return ToInfo(Save(s with { Enabled = enabled, AllowRun = allowRun }));
        }
    }

    public AgentAccessInfo RegenerateToken() { lock (_lock) return ToInfo(Save(Current() with { Token = NewToken() })); }

    public AgentCheck Check(string? authorization, string method, string path)
    {
        if (string.IsNullOrWhiteSpace(authorization)) return AgentCheck.NotAgent;
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return new(true, false, 401, "Send the token as 'Authorization: Bearer <token>'.");
        AgentSettings s;
        lock (_lock) s = Current();
        if (!SameToken(authorization[prefix.Length..].Trim(), s.Token)) return new(true, false, 401, "The token is wrong or was replaced. Ask the user for the current one (Tammoa > Agent access).");
        if (!s.Enabled) return new(true, false, 403, "Agent access is switched off. Ask the user to switch it on in Tammoa (Agent access).");
        var route = AgentApi.Find(method, path);
        if (route is null) return new(true, false, 403, $"{method.ToUpperInvariant()} {path} is not available to an agent. See the routes in the skill (GET /api/agent/skill).");
        if (route.NeedsRunPermission && !s.AllowRun) return new(true, false, 403, "Running flows is switched off. Ask the user to switch on 'Let the agent run flows' in Tammoa (Agent access), or ask them to press Run themselves.");
        return new(true, true, 200, null);
    }

    public string RenderSkill() => AgentSkillBuilder.Render(_skillTemplate(), _apiUrl, AgentApi.Routes, _cards.All());

    private AgentSettings Current()
    {
        if (_cache is not null) return _cache;
        var loaded = _repo.Load();
        // a missing or damaged file: access stays OFF and a fresh token is made
        return Save(loaded is { Token.Length: > 0 } ? loaded : new AgentSettings(false, false, NewToken()));
    }

    private AgentSettings Save(AgentSettings s)
    {
        _repo.Save(s);
        return _cache = s;
    }

    private AgentAccessInfo ToInfo(AgentSettings s) => new(s.Enabled, s.AllowRun, _apiUrl, s.Token);

    private static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool SameToken(string given, string real) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(real));

    private static string LoadShippedSkill()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The skill file is missing from the app ({ResourceName}).");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
