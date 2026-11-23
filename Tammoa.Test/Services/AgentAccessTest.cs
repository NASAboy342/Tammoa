using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using Tammoa.Api;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Repositories;
using Tammoa.Services;

namespace Tammoa.Test.Services;

public class AgentAccessTest
{
    private string _dir = null!;
    private string _file = null!;
    private ICardRegistry _cards = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tammoa-agent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "agent-access.json");
        _cards = Substitute.For<ICardRegistry>();
        _cards.All().Returns([new CardTypeInfo("trigger.manual", "Manual trigger", "Starts a flow.", CardCategory.Trigger, "play", [])]);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, true);

    private AgentAccessService Make(string? file = null) =>
        new(new AgentAccessRepository(file ?? _file), _cards, "http://localhost:8000/", () => "# the shipped skill");

    private static string Bearer(string token) => "Bearer " + token;

    // ---- settings and token
    [Test]
    public void FirstUse_IsOff_HasAToken_AndKeepsIt_AfterARestart()
    {
        var a = Make().Info();
        Assert.That(a.Enabled, Is.False);
        Assert.That(a.AllowRun, Is.False);
        Assert.That(a.Token, Has.Length.GreaterThan(30));
        Assert.That(a.Url, Is.EqualTo("http://localhost:8000"));
        Assert.That(Make().Info().Token, Is.EqualTo(a.Token), "a new service reads the same file");
    }

    [Test]
    public void Update_SavesTheSwitches_AndSwitchingAccessOffAlsoSwitchesRunningOff()
    {
        var s = Make();
        Assert.That(s.Update(new(true, null)).AllowRun, Is.False, "running is its own switch");
        var on = s.Update(new(null, true));
        Assert.That((on.Enabled, on.AllowRun), Is.EqualTo((true, true)));
        Assert.That(Make().Info().AllowRun, Is.True, "saved");
        var off = s.Update(new(false, null));
        Assert.That((off.Enabled, off.AllowRun), Is.EqualTo((false, false)));
        Assert.That(s.Update(new(null, true)).AllowRun, Is.False, "cannot allow running while access is off");
    }

    [Test]
    public void RegenerateToken_GivesANewOne_AndTheOldOneStopsWorking()
    {
        var s = Make();
        s.Update(new(true, null));
        var old = s.Info().Token;
        var fresh = s.RegenerateToken().Token;
        Assert.That(fresh, Is.Not.EqualTo(old));
        Assert.That(s.Check(Bearer(old), "GET", "/api/flows").Status, Is.EqualTo(401));
        Assert.That(s.Check(Bearer(fresh), "GET", "/api/flows").Allowed, Is.True);
        Assert.That(s.Update(new(null, null)).Enabled, Is.True, "regenerating keeps the switches");
    }

    [Test]
    public void ADamagedFile_MeansOff_WithANewToken_NotACrash()
    {
        File.WriteAllText(_file, "{ not json");
        var info = Make().Info();
        Assert.That(info.Enabled, Is.False);
        Assert.That(info.Token, Is.Not.Empty);
    }

    // ---- who may do what
    [Test]
    public void ARequestWithoutAnAuthorizationHeader_IsTheUi_AndIsNeverRestricted()
    {
        var s = Make();
        Assert.That(s.Check(null, "DELETE", "/api/flows/abc").IsAgent, Is.False);
        Assert.That(s.Check("", "PUT", "/api/agent/access").IsAgent, Is.False);
    }

    [Test]
    public void AnAgent_NeedsTheRightToken_AndAccessSwitchedOn()
    {
        var s = Make();
        var token = s.Info().Token;
        Assert.That(s.Check("Basic abc", "GET", "/api/flows").Status, Is.EqualTo(401));
        Assert.That(s.Check(Bearer("wrong"), "GET", "/api/flows").Status, Is.EqualTo(401));
        var off = s.Check(Bearer(token), "GET", "/api/flows");
        Assert.That((off.Status, off.Error), Is.EqualTo((403, "Agent access is switched off. Ask the user to switch it on in Tammoa (Agent access).")));
        s.Update(new(true, null));
        Assert.That(s.Check(Bearer(token), "GET", "/api/flows").Allowed, Is.True);
        Assert.That(s.Check("bearer " + token, "GET", "/api/flows").Allowed, Is.True, "the scheme is case-insensitive");
    }

    [TestCase("GET", "/api/flows/abc")]
    [TestCase("PUT", "/api/flows/abc")]
    [TestCase("POST", "/api/flows/validate")]
    [TestCase("GET", "/api/cards/options/ollama.models")]
    [TestCase("GET", "/api/runs/r1")]
    [TestCase("GET", "/api/agent/skill")]
    public void AnAgent_MayCallTheListedRoutes(string method, string path)
    {
        var s = Make();
        s.Update(new(true, null));
        Assert.That(s.Check(Bearer(s.Info().Token), method, path).Allowed, Is.True);
    }

    [TestCase("DELETE", "/api/flows/abc")]
    [TestCase("DELETE", "/api/runs/r1")]
    [TestCase("POST", "/api/connections")]
    [TestCase("PUT", "/api/agent/access")]
    [TestCase("POST", "/api/agent/access/token")]
    [TestCase("GET", "/api/agent/access")]
    [TestCase("GET", "/api/events")]
    [TestCase("GET", "/api/flows/abc/extra/path")]
    public void AnAgent_CannotCallAnythingElse_NotEvenItsOwnPermissions(string method, string path)
    {
        var s = Make();
        s.Update(new(true, true));
        var r = s.Check(Bearer(s.Info().Token), method, path);
        Assert.That((r.Allowed, r.Status), Is.EqualTo((false, 403)));
    }

    [Test]
    public void StartingARun_NeedsItsOwnSwitch_AndTheMessageSaysWhatToAsk()
    {
        var s = Make();
        s.Update(new(true, null));
        var token = s.Info().Token;
        var denied = s.Check(Bearer(token), "POST", "/api/flows/f1/runs");
        Assert.That((denied.Allowed, denied.Status), Is.EqualTo((false, 403)));
        Assert.That(denied.Error, Does.Contain("Let the agent run flows").And.Contain("press Run themselves"));
        Assert.That(s.Check(Bearer(token), "GET", "/api/flows/f1/runs").Allowed, Is.True, "reading runs is fine");
        s.Update(new(null, true));
        Assert.That(s.Check(Bearer(token), "POST", "/api/flows/f1/runs").Allowed, Is.True);
        Assert.That(s.Check(Bearer(token), "POST", "/api/runs/r1/cancel").Allowed, Is.True);
    }

    // ---- the middleware
    private static async Task<(int Status, string Body, bool Reached)> Send(IAgentAccessService service, string method, string path, string? auth)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        if (auth is not null) ctx.Request.Headers.Authorization = auth;
        ctx.Response.Body = new MemoryStream();
        var reached = false;
        await AgentAccessMiddleware.InvokeAsync(ctx, _ => { reached = true; return Task.CompletedTask; }, service);
        ctx.Response.Body.Position = 0;
        return (ctx.Response.StatusCode, new StreamReader(ctx.Response.Body).ReadToEnd(), reached);
    }

    [Test]
    public async Task Middleware_LetsTheUiThrough_StopsARefusedAgent_AndOnlyLooksAtApiPaths()
    {
        var s = Make();
        var token = s.Info().Token;
        Assert.That((await Send(s, "DELETE", "/api/flows/x", null)).Reached, Is.True, "the UI");
        Assert.That((await Send(s, "GET", "/index.html", Bearer("anything"))).Reached, Is.True, "not an API path");

        var refused = await Send(s, "GET", "/api/flows", Bearer(token)); // access is off
        Assert.That((refused.Status, refused.Reached), Is.EqualTo((403, false)));
        Assert.That(refused.Body, Does.Contain("switched off").And.Contain("\"error\""));

        s.Update(new(true, null));
        Assert.That((await Send(s, "GET", "/api/flows", Bearer(token))).Reached, Is.True);
        Assert.That((await Send(s, "GET", "/api/flows", Bearer("nope"))).Status, Is.EqualTo(401));
    }

    // ---- the skill
    [Test]
    public void TheSkill_IsTheShippedFile_PlusTheAddressTheRoutesAndTheCards_AndNeverTheToken()
    {
        var s = Make();
        var text = s.RenderSkill();
        Assert.That(text, Does.StartWith("# the shipped skill"));
        Assert.That(text, Does.Contain("http://localhost:8000"));
        foreach (var r in AgentApi.Routes) Assert.That(text, Does.Contain($"`{r.Pattern}`"), r.Pattern);
        Assert.That(text, Does.Contain("trigger.manual").And.Contain("Manual trigger"));
        Assert.That(text, Does.Not.Contain(s.Info().Token));
        Assert.That(text, Does.Contain("Let the agent run flows"), "the run route says it needs the switch");
    }

    [Test]
    public void TheShippedSkillFile_IsInsideTheApp_WithItsFrontMatter()
    {
        var real = new AgentAccessService(new AgentAccessRepository(_file), _cards, "http://localhost:1");
        var text = real.RenderSkill();
        Assert.That(text, Does.StartWith("---\nname: tammoa-flows").Or.StartWith("---\r\nname: tammoa-flows"));
        Assert.That(text, Does.Contain("Never run a flow unless the user asked"));
    }

    // ---- every route of the app is either open to agents or excluded on purpose
    [Test]
    public void EveryRouteOfTheApp_IsEitherForAgentsOrExcludedOnPurpose_SoANewEndpointCannotBeForgotten()
    {
        var builder = WebApplication.CreateBuilder();
        var app = builder.Build();
        app.MapAppEndpoints(Substitute.For<IAppInfoService>());
        app.MapFlowEndpoints(Substitute.For<IFlowService>());
        app.MapCardEndpoints(Substitute.For<ICardRegistry>(), Substitute.For<ICardOptionsService>());
        app.MapConnectionEndpoints(Substitute.For<IConnectionService>());
        app.MapDataEndpoints(Substitute.For<IDataService>());
        app.MapRunEndpoints(Substitute.For<IRunService>(), Substitute.For<IRunEventBus>());
        app.MapAgentEndpoints(Substitute.For<IAgentAccessService>());

        var actual = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Select(m => (m, AgentApi.Normalize("/" + e.RoutePattern.RawText!.TrimStart('/')))))
            .Where(r => r.Item2.StartsWith("/api"))
            .ToHashSet();
        var known = AgentApi.Routes.Select(r => (r.Method, AgentApi.Normalize(r.Pattern)))
            .Concat(AgentApi.NotForAgents.Select(r => (r.Method, AgentApi.Normalize(r.Pattern)))).ToHashSet();

        Assert.That(actual, Is.Not.Empty);
        Assert.That(actual.Except(known), Is.Empty,
            "These routes are new: decide whether an AI agent may use them. If yes, add them to AgentApi.Routes (they then appear in the skill) and say how to use them in AgentSkill/tammoa-flows/SKILL.md; if not, add them to AgentApi.NotForAgents with the reason.");
        Assert.That(known.Except(actual), Is.Empty, "These routes are listed for agents but do not exist (renamed or removed?): fix AgentApi.");
        Assert.That(AgentApi.Routes.Select(r => (r.Method, AgentApi.Normalize(r.Pattern))).Intersect(AgentApi.NotForAgents.Select(r => (r.Method, AgentApi.Normalize(r.Pattern)))), Is.Empty, "a route is in both lists");
    }

    [Test]
    public void AgentApi_FindsTheRouteByShape()
    {
        Assert.That(AgentApi.Find("get", "/api/flows/abc")!.Pattern, Is.EqualTo("/api/flows/{id}"));
        Assert.That(AgentApi.Find("GET", "/api/flows/abc/")!.Pattern, Is.EqualTo("/api/flows/{id}"), "a trailing slash is the same route");
        Assert.That(AgentApi.Find("POST", "/api/flows/validate")!.Pattern, Is.EqualTo("/api/flows/validate"));
        Assert.That(AgentApi.Find("GET", "/api/flows//data/x"), Is.Null, "an empty segment matches nothing");
        Assert.That(AgentApi.Find("PATCH", "/api/flows/abc"), Is.Null);
    }
}
