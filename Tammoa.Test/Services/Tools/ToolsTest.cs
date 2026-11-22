using Newtonsoft.Json.Linq;
using NSubstitute;
using Tammoa.Models;
using Tammoa.Services;
using Tammoa.Services.Tools;

namespace Tammoa.Test.Services.Tools;

public class ToolsTest
{
    private string _base = null!;
    private string _root = null!;
    private ToolContext _ctx = null!;
    private static JObject A(object o) => JObject.FromObject(o);

    [SetUp]
    public void SetUp()
    {
        _base = Path.Combine(Path.GetTempPath(), "tammoa-tools-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_base, "root");
        Directory.CreateDirectory(_root);
        _ctx = new ToolContext(_root, ["api.example.com", "*.docs.test"]);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_base, true);

    // ---- write_file / read_file
    [Test]
    public async Task WriteFile_WritesInsideTheRoot_CreatesFolders_AndAppends()
    {
        var sut = new WriteFileTool();
        var message = await sut.RunAsync(A(new { path = "notes/a.txt", content = "one" }), _ctx, default);
        await sut.RunAsync(A(new { path = "notes/a.txt", content = "+two", mode = "append" }), _ctx, default);
        Assert.That(File.ReadAllText(Path.Combine(_root, "notes", "a.txt")), Is.EqualTo("one+two"));
        Assert.That(message, Does.Contain("3 bytes"));
        await sut.RunAsync(A(new { path = "notes/a.txt", content = "new" }), _ctx, default);
        Assert.That(File.ReadAllText(Path.Combine(_root, "notes", "a.txt")), Is.EqualTo("new"), "overwrite is the default");
    }

    [Test]
    public void WriteFile_OutsideTheRoot_IsRejected_AndNothingIsWritten()
    {
        var sut = new WriteFileTool();
        var target = Path.Combine(_base, "evil.txt");
        Assert.ThrowsAsync<ToolRejectedException>(() => sut.RunAsync(A(new { path = "../evil.txt", content = "x" }), _ctx, default));
        Assert.ThrowsAsync<ToolRejectedException>(() => sut.RunAsync(A(new { path = target, content = "x" }), _ctx, default));
        Assert.Throws<ToolRejectedException>(() => sut.Check(A(new { path = target, content = "x" }), _ctx));
        Assert.That(File.Exists(target), Is.False);
    }

    [Test]
    public void WriteFile_BadArguments_AreArgumentErrors()
    {
        var sut = new WriteFileTool();
        Assert.ThrowsAsync<ArgumentException>(() => sut.RunAsync(A(new { path = "a.txt" }), _ctx, default), "no content");
        Assert.ThrowsAsync<ArgumentException>(() => sut.RunAsync(A(new { content = "x" }), _ctx, default), "no path");
        Assert.ThrowsAsync<ArgumentException>(() => sut.RunAsync(A(new { path = "a.txt", content = "x", mode = "delete" }), _ctx, default));
    }

    [Test]
    public async Task ReadFile_ReadsInside_RejectsOutside_AndReportsAMissingFile()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(_base, "secret.txt"), "top secret");
        var sut = new ReadFileTool();
        Assert.That(await sut.RunAsync(A(new { path = "a.txt" }), _ctx, default), Is.EqualTo("hello"));
        Assert.ThrowsAsync<ToolRejectedException>(() => sut.RunAsync(A(new { path = "../secret.txt" }), _ctx, default));
        Assert.ThrowsAsync<ToolRejectedException>(() => sut.RunAsync(A(new { path = Path.Combine(_base, "secret.txt") }), _ctx, default));
        Assert.ThrowsAsync<InvalidOperationException>(() => sut.RunAsync(A(new { path = "missing.txt" }), _ctx, default));
    }

    [Test]
    public async Task ReadFile_CutsALargeFile()
    {
        File.WriteAllText(Path.Combine(_root, "big.txt"), new string('x', ReadFileTool.MaxBytes + 10));
        var text = await new ReadFileTool().RunAsync(A(new { path = "big.txt" }), _ctx, default);
        Assert.That(text, Does.Contain("[cut:"));
        Assert.That(text.Length, Is.LessThan(ReadFileTool.MaxBytes + 200));
    }

    [Test]
    public void FileTools_WithoutARoot_AreRejected()
    {
        var none = new ToolContext(null, []);
        Assert.Throws<ToolRejectedException>(() => new ReadFileTool().Check(A(new { path = "a.txt" }), none));
        Assert.Throws<ToolRejectedException>(() => new WriteFileTool().Check(A(new { path = "a.txt", content = "x" }), none));
    }

    // ---- http_request
    private static IHttpRequestRunner Http(out List<(string Method, Uri Url)> sent, int status = 200, string text = "{}", Dictionary<string, string>? headers = null)
    {
        var list = new List<(string, Uri)>();
        sent = list;
        var runner = Substitute.For<IHttpRequestRunner>();
        runner.SendAsync(default!, default!, default!, default, default, default, default).ReturnsForAnyArgs(ci =>
        {
            list.Add(((string)ci[0], (Uri)ci[1]));
            return Task.FromResult(new HttpRequestResult(status, status < 300, headers ?? new Dictionary<string, string>(), text, false));
        });
        return runner;
    }

    [Test]
    public async Task Http_AllowedHost_IsCalled_WithoutFollowingRedirects()
    {
        var runner = Http(out var sent, text: "{\"ok\":true}");
        var result = await new HttpRequestTool(runner).RunAsync(A(new { url = "https://api.example.com/v1/x", method = "post", body = "{}" }), _ctx, default);
        Assert.That(sent.Single(), Is.EqualTo(("POST", new Uri("https://api.example.com/v1/x"))));
        Assert.That(result, Does.StartWith("HTTP 200").And.Contain("{\"ok\":true}"));
        await runner.Received(1).SendAsync("POST", Arg.Any<Uri>(), Arg.Any<IReadOnlyDictionary<string, string>>(), "{}", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>(), false);
    }

    [TestCase("https://evil.test/x")]
    [TestCase("https://api.example.com.evil.test/x")]
    [TestCase("https://example.com/x")]
    [TestCase("http://localhost:11434/api/tags")]
    public void Http_HostNotInTheList_IsRejected_AndNothingIsSent(string url)
    {
        var runner = Http(out var sent);
        Assert.ThrowsAsync<ToolRejectedException>(() => new HttpRequestTool(runner).RunAsync(A(new { url }), _ctx, default));
        Assert.That(sent, Is.Empty);
    }

    [Test]
    public async Task Http_WildcardHost_MatchesSubdomainsOnly()
    {
        var runner = Http(out _);
        var sut = new HttpRequestTool(runner);
        await sut.RunAsync(A(new { url = "https://en.docs.test/a" }), _ctx, default);
        Assert.ThrowsAsync<ToolRejectedException>(() => sut.RunAsync(A(new { url = "https://docs.test/a" }), _ctx, default));
        Assert.ThrowsAsync<ToolRejectedException>(() => sut.RunAsync(A(new { url = "https://xdocs.test/a" }), _ctx, default));
    }

    [Test]
    public void Http_NoAllowedHosts_RefusesEverything()
    {
        Assert.Throws<ToolRejectedException>(() => new HttpRequestTool(Http(out _)).Check(A(new { url = "https://api.example.com/" }), new ToolContext(_root, [])));
    }

    [Test]
    public void Http_BadUrlOrMethod_AreArgumentErrors()
    {
        var sut = new HttpRequestTool(Http(out _));
        Assert.Throws<ArgumentException>(() => sut.Check(A(new { url = "not a url" }), _ctx));
        Assert.Throws<ArgumentException>(() => sut.Check(A(new { url = "file:///etc/passwd" }), _ctx));
        Assert.Throws<ArgumentException>(() => sut.Check(A(new { url = "https://api.example.com/", method = "TRACE" }), _ctx));
    }

    [Test]
    public async Task Http_ARedirect_IsReportedNotFollowed()
    {
        var runner = Http(out var sent, 302, "", new Dictionary<string, string> { ["location"] = "https://evil.test/x" });
        var text = await new HttpRequestTool(runner).RunAsync(A(new { url = "https://api.example.com/go" }), _ctx, default);
        Assert.That(text, Does.Contain("redirect to https://evil.test/x"));
        Assert.That(sent, Has.Count.EqualTo(1));
    }

    // ---- run_script
    private static IProcessRunner Runner(out List<(string Command, string Dir)> ran)
    {
        var list = new List<(string, string)>();
        ran = list;
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(default!, default!, default, default, default, default).ReturnsForAnyArgs(ci =>
        {
            list.Add(((string)ci[0], (string)ci[1]));
            return Task.FromResult(new ProcessResult(0, "done\n", "", false, false));
        });
        return runner;
    }

    [Test]
    public async Task Script_RunsInTheRootFolder_AndReturnsOutput()
    {
        var sut = new RunScriptTool(Runner(out var ran));
        var text = await sut.RunAsync(A(new { code = "echo done > out.txt" }), _ctx, default);
        Assert.That(ran.Single().Dir, Is.EqualTo(_root));
        Assert.That(ran.Single().Command, Is.EqualTo("echo done > out.txt"));
        Assert.That(text, Does.Contain("exit code 0").And.Contain("done"));
    }

    [Test]
    public async Task Script_JavaScriptAndPython_RunThroughTheInterpreter_FromAScratchFolderOutsideTheRoot()
    {
        var sut = new RunScriptTool(Runner(out var ran));
        await sut.RunAsync(A(new { language = "javascript", code = "console.log(1)" }), _ctx, default);
        await sut.RunAsync(A(new { language = "python", code = "print(1)" }), _ctx, default);
        Assert.That(ran[0].Command, Does.StartWith("node ").And.Contain("script.js").And.Not.Contain(_root));
        Assert.That(ran[1].Command, Does.Contain("script.py"));
        Assert.That(Directory.GetFiles(_root), Is.Empty, "nothing is left in the root by the tool itself");
    }

    [TestCase("cat /etc/passwd")]
    [TestCase("cd .. && rm -rf *")]
    [TestCase("echo x > ~/a.txt")]
    public void Script_ThatReachesOutside_IsRejected_AndNeverRun(string code)
    {
        var runner = Runner(out var ran);
        Assert.ThrowsAsync<ToolRejectedException>(() => new RunScriptTool(runner).RunAsync(A(new { code }), _ctx, default));
        Assert.That(ran, Is.Empty);
    }

    [Test]
    public void Script_TimedOut_IsAFailure()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(default!, default!, default, default, default, default).ReturnsForAnyArgs(new ProcessResult(-1, "", "", true, false));
        Assert.ThrowsAsync<InvalidOperationException>(() => new RunScriptTool(runner).RunAsync(A(new { code = "sleep 99" }), _ctx, default));
    }

    [Test]
    public void Script_UnknownLanguage_IsAnArgumentError() =>
        Assert.Throws<ArgumentException>(() => new RunScriptTool(Runner(out _)).Check(A(new { language = "ruby", code = "puts 1" }), _ctx));
}
