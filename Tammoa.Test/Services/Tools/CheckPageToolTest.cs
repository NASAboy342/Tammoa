using Newtonsoft.Json.Linq;
using NSubstitute;
using Tammoa.Helper;
using Tammoa.Models;
using Tammoa.Services;
using Tammoa.Services.Tools;

namespace Tammoa.Test.Services.Tools;

public class CheckPageToolTest
{
    private string _base = null!;
    private string _root = null!;
    private ToolContext _ctx = null!;
    private static JObject A(object o) => JObject.FromObject(o);

    [SetUp]
    public void SetUp()
    {
        _base = Path.Combine(Path.GetTempPath(), "tammoa-page-test-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_base, "root");
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "game.html"), "<html><body><script src=\"https://cdn.jsdelivr.net/npm/phaser@3/dist/phaser.js\"></script></body></html>");
        _ctx = new ToolContext(_root, ["cdn.jsdelivr.net"]);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_base, true);

    private static IProcessRunner Runner(string stderr, string stdout, out List<string> commands)
    {
        var list = new List<string>();
        commands = list;
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(default!, default!, default, default, default, default).ReturnsForAnyArgs(ci =>
        {
            list.Add((string)ci[0]);
            return Task.FromResult(new ProcessResult(-1, stdout, stderr, true, false)); // the browser never exits by itself: it is stopped by the timeout
        });
        return runner;
    }

    private const string TextureError = "[39307:61420388:1123/181830.468875:INFO:CONSOLE:7] \"Uncaught TypeError: this.textures.add is not a function\", source: file:///x/game.html (7)";

    [Test]
    public async Task ReportsTheErrorsTheBrowserPrinted_WithTheirLine()
    {
        var sut = new CheckPageTool(Runner("noise\n" + TextureError + "\n", "<html><body></body></html>", out _), () => "/usr/bin/chrome");
        var text = await sut.RunAsync(A(new { path = "game.html" }), _ctx, default);
        Assert.That(text, Does.Contain("1 error(s)").And.Contain("this.textures.add is not a function").And.Contain("line 7"));
        Assert.That(text, Does.Contain("No <canvas>"));
    }

    [Test]
    public async Task ACleanPageWithACanvas_SaysSo()
    {
        var sut = new CheckPageTool(Runner("", "<html><body><canvas width=\"8\"></canvas></body></html>", out _), () => "/usr/bin/chrome");
        var text = await sut.RunAsync(A(new { path = "game.html" }), _ctx, default);
        Assert.That(text, Does.Contain("No errors on the console.").And.Contain("A <canvas> element is on the page."));
    }

    [Test]
    public void Summarize_CountsErrorLevelAndFailedLoads_IgnoresLogsAndTheCanvas2dHint_AndShowsAFewLogs()
    {
        var log = string.Join("\n",
            "[1:2:3:INFO:CONSOLE:1] \"hello\", source: file:///x/a.html (1)",
            "[1:2:3:ERROR:CONSOLE:9] \"my own console.error\", source: file:///x/a.html (9)",
            "[1:2:3:INFO:CONSOLE:5] \"Failed to load resource: net::ERR_PROXY_CONNECTION_FAILED\", source: https://cdn.x/p.js (0)",
            "[1:2:3:INFO:CONSOLE:8] \"%c %c Phaser v3 (Canvas) %c https://phaser.io background: #ff0000\", source: https://cdn.x/p.js (8)",
            "[1:2:3:INFO:CONSOLE:77] \"Canvas2D: Multiple readback operations using getImageData are faster\", source: https://cdn.x/p.js (77)");
        var text = CheckPageTool.Summarize(log, "", "", []);
        Assert.That(text, Does.Contain("2 error(s)").And.Contain("my own console.error").And.Contain("Failed to load resource"));
        Assert.That(text, Does.Not.Contain("Canvas2D"));
        Assert.That(text, Does.Contain("Console output: hello").And.Not.Contain("Phaser"), "library banners (%c) are not shown");
    }

    [Test]
    public void Summarize_TheSameErrorOnEveryFrame_IsListedOnce_AndALongListIsCut()
    {
        var one = string.Join("\n", Enumerable.Repeat("[1:2:3:INFO:CONSOLE:7] \"Uncaught Error: boom\", source: f (7)", 50));
        Assert.That(CheckPageTool.Summarize(one, "", "", []), Does.Contain("1 error(s)"));
        var many = string.Join("\n", Enumerable.Range(1, 30).Select(i => $"[1:2:3:ERROR:CONSOLE:{i}] \"problem {i}\", source: f ({i})"));
        var text = CheckPageTool.Summarize(many, "", "", []);
        Assert.That(text, Does.Contain("30 error(s)").And.Contain("and 18 more").And.Contain("problem 12").And.Not.Contain("problem 13"));
    }

    [Test]
    public void Summarize_APageThatNeedsAHostThatIsNotAllowed_SaysWhichOne()
    {
        var html = "<script src=\"https://cdn.jsdelivr.net/a.js\"></script><link href=\"//fonts.example.org/x.css\"><script src=\"local.js\"></script>";
        var allowed = CheckPageTool.Summarize("", "", html, ["cdn.jsdelivr.net"]);
        Assert.That(allowed, Does.Contain("fonts.example.org").And.Contain("blocked").And.Not.Contain("cdn.jsdelivr.net, "));
        Assert.That(CheckPageTool.Summarize("", "", html, ["*.example.org", "cdn.jsdelivr.net"]), Does.Not.Contain("blocked"));
        Assert.That(CheckPageTool.Summarize("", "", html, []), Does.Contain("cdn.jsdelivr.net").And.Contain("fonts.example.org"));
        Assert.That(CheckPageTool.Summarize("", "", html, null), Does.Not.Contain("blocked"), "no limit: nothing is blocked");
    }

    [Test]
    public async Task TheBrowserIsStartedHeadless_WithTheFileUrl_AndWhenLimited_TheNetworkIsClosedExceptForAllowedHosts()
    {
        var sut = new CheckPageTool(Runner("", "", out var commands), () => "/Apps/Google Chrome");
        await sut.RunAsync(A(new { path = "game.html" }), new ToolContext(_root, ["cdn.jsdelivr.net", "*.docs.test"], true), default);
        var c = commands.Single();
        Assert.That(c, Does.StartWith("\"/Apps/Google Chrome\" ").And.Contain("--headless=new").And.Contain("--dump-dom"));
        Assert.That(c, Does.Contain("--proxy-server=http://127.0.0.1:9").And.Contain("--proxy-bypass-list=\"cdn.jsdelivr.net;*.docs.test\""));
        Assert.That(c, Does.Contain("file://").And.Contain("game.html").And.Not.Contain("--no-sandbox"));

        await sut.RunAsync(A(new { path = "game.html" }), new ToolContext(_root, [], true), default);
        Assert.That(commands[1], Does.Contain("--proxy-server").And.Not.Contain("--proxy-bypass-list"), "no allowed hosts: nothing leaves the computer");

        await sut.RunAsync(A(new { path = "game.html" }), new ToolContext(_root, ["cdn.jsdelivr.net"]), default);
        Assert.That(commands[2], Does.Not.Contain("--proxy"), "the limit is off by default: the page may reach any host");
    }

    [Test]
    public void NoBrowserFound_IsAClearError_ThatSuggestsAFallback()
    {
        var sut = new CheckPageTool(Runner("", "", out var commands), () => null);
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAsync(A(new { path = "game.html" }), _ctx, default))!;
        Assert.That(ex.Message, Does.Contain("No Chrome, Edge").And.Contain("node --check"));
        Assert.That(commands, Is.Empty);
    }

    [TestCase("../outside.html")]
    [TestCase("/etc/hosts.html")]
    public void OutsideTheRoot_IsRejected_AndNoBrowserStarts(string path)
    {
        var sut = new CheckPageTool(Runner("", "", out var commands), () => "/usr/bin/chrome");
        Assert.ThrowsAsync<ToolRejectedException>(() => sut.RunAsync(A(new { path }), _ctx, default));
        Assert.That(commands, Is.Empty);
    }

    [Test]
    public void OnlyHtmlFiles_AndAFileThatExists()
    {
        var sut = new CheckPageTool(Runner("", "", out _), () => "/usr/bin/chrome");
        Assert.Throws<ArgumentException>(() => sut.Check(A(new { path = "notes.txt" }), _ctx));
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAsync(A(new { path = "missing.html" }), _ctx, default));
    }

    // ---- BrowserLocator
    [Test]
    public void BrowserLocator_TheEnvironmentVariableWins_AndAMissingOneMeansNone_NotASilentFallback()
    {
        Assert.That(BrowserLocator.Find(p => p == "/my/chrome", k => k == BrowserLocator.EnvVar ? "/my/chrome" : null), Is.EqualTo("/my/chrome"));
        Assert.That(BrowserLocator.Find(_ => false, k => k == BrowserLocator.EnvVar ? "/my/chrome" : null), Is.Null);
    }

    [Test]
    public void BrowserLocator_FindsTheFirstInstalledCandidate_OrNull()
    {
        var seen = new List<string>();
        Assert.That(BrowserLocator.Find(p => { seen.Add(p); return false; }, _ => null), Is.Null);
        Assert.That(seen, Is.Not.Empty);
        Assert.That(BrowserLocator.Find(p => p == seen[1], _ => null), Is.EqualTo(seen[1]), "a later candidate is used when the first is missing");
    }
}
