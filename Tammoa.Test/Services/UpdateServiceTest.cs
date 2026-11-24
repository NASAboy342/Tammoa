using System.Net;
using NSubstitute;
using Tammoa.Models;
using Tammoa.Services;

namespace Tammoa.Test.Services;

public class UpdateServiceTest
{
    private const string Repo = "https://github.com/someone/Tammoa";

    private static IAppInfoService Info(string version = "0.1.0")
    {
        var info = Substitute.For<IAppInfoService>();
        info.GetVersion().Returns(new AppVersionInfo("Tammoa", version));
        return info;
    }

    private static UpdateService Make(StubHandler stub, string? repo = Repo, string version = "0.1.0") => new(new HttpClient(stub), Info(version), repo);

    private static string Release(string tag, string url = "https://github.com/someone/Tammoa/releases/tag/v0.2.0") => $"{{\"tag_name\":\"{tag}\",\"html_url\":\"{url}\"}}";

    [Test]
    public async Task ANewerRelease_IsReported_WithItsAddress_AndAskedFromTheRightRepository()
    {
        var stub = StubHandler.Json(Release("v0.2.0"));
        var r = await Make(stub).CheckAsync(default);
        Assert.That(r, Is.EqualTo(new UpdateInfo("0.1.0", "0.2.0", true, "https://github.com/someone/Tammoa/releases/tag/v0.2.0", null)));
        Assert.That(stub.Seen[0].Request.RequestUri!.ToString(), Is.EqualTo("https://api.github.com/repos/someone/Tammoa/releases/latest"));
        Assert.That(stub.Seen[0].Request.Headers.UserAgent.ToString(), Is.Not.Empty, "GitHub refuses a request without a User-Agent");
        Assert.That(stub.Seen[0].Request.Headers.Authorization, Is.Null, "no token, no personal data is sent");
    }

    [TestCase("v0.1.0")]
    [TestCase("0.1.0")]
    [TestCase("v0.0.9")]
    public async Task TheSameOrAnOlderRelease_IsNotAnUpdate(string tag)
    {
        var r = await Make(StubHandler.Json(Release(tag))).CheckAsync(default);
        Assert.That(r.UpdateAvailable, Is.False);
        Assert.That(r.Error, Is.Null);
        Assert.That(r.LatestVersion, Is.Not.Null);
    }

    [Test]
    public async Task ATenIsNewerThanANine_NotTextOrder()
    {
        var r = await Make(StubHandler.Json(Release("v0.10.0")), version: "0.9.0").CheckAsync(default);
        Assert.That(r.UpdateAvailable, Is.True);
    }

    [Test]
    public async Task NoReleaseYet_IsAFriendlyMessage()
    {
        var r = await Make(StubHandler.Json("{\"message\":\"Not Found\"}", HttpStatusCode.NotFound)).CheckAsync(default);
        Assert.That((r.UpdateAvailable, r.Error), Is.EqualTo((false, "No release has been published yet.")));
    }

    [TestCase(HttpStatusCode.Forbidden, "limiting")]
    [TestCase(HttpStatusCode.TooManyRequests, "limiting")]
    [TestCase(HttpStatusCode.InternalServerError, "500")]
    public async Task GitHubProblems_ComeBackAsAReadableReason(HttpStatusCode status, string contains)
    {
        var r = await Make(StubHandler.Json("{}", status)).CheckAsync(default);
        Assert.That(r.UpdateAvailable, Is.False);
        Assert.That(r.Error, Does.Contain(contains));
    }

    [Test]
    public async Task NoInternet_NeverThrows_ItSaysSo()
    {
        var r = await Make(new StubHandler((_, _) => throw new HttpRequestException("no route"))).CheckAsync(default);
        Assert.That(r.Error, Does.Contain("internet"));
        Assert.That(r.CurrentVersion, Is.EqualTo("0.1.0"));
    }

    [Test]
    public async Task AnUnreadableAnswer_IsAReason_NotACrash()
    {
        Assert.That((await Make(StubHandler.Json("<html>captive portal</html>")).CheckAsync(default)).Error, Does.Contain("could not be read"));
        Assert.That((await Make(StubHandler.Json(Release("banana"))).CheckAsync(default)).Error, Does.Contain("unreadable version"));
    }

    [Test]
    public async Task AnAddressThatIsNotGitHub_IsNotShown_TheReleasesPageIsUsed()
    {
        var r = await Make(StubHandler.Json(Release("v0.2.0", "https://evil.example/download"))).CheckAsync(default);
        Assert.That(r.Url, Is.EqualTo("https://github.com/someone/Tammoa/releases"));
    }

    [Test]
    public async Task NoUpdateSource_SaysSo_AndDoesNotCallAnyone()
    {
        var stub = StubHandler.Json("{}");
        var r = await Make(stub, repo: "").CheckAsync(default);
        Assert.That(r.Error, Does.Contain("No update source"));
        Assert.That(stub.Seen, Is.Empty);
    }

    [Test]
    public void TheCallersCancel_IsNotTurnedIntoAMessage()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.CatchAsync<OperationCanceledException>(() => Make(new StubHandler(async (_, ct) => { await Task.Delay(5000, ct); return new HttpResponseMessage(); })).CheckAsync(cts.Token));
    }

    [TestCase("v1.2.3", "1.2.3")]
    [TestCase("1.2", "1.2.0")]
    [TestCase("1.2.3-beta.1", "1.2.3")]
    [TestCase("1.2.3+abc", "1.2.3")]
    public void Versions_AreReadByTheirNumbers(string text, string expected)
    {
        Assert.That(UpdateService.TryParseVersion(text, out var v), Is.True);
        Assert.That(v.ToString(3), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("latest")]
    [TestCase("v")]
    public void ANonVersion_IsRefused(string text) => Assert.That(UpdateService.TryParseVersion(text, out _), Is.False);

    [TestCase("https://github.com/a/b", "a", "b", true)]
    [TestCase("https://github.com/a/b.git", "a", "b", true)]
    [TestCase("https://github.com/a/b/", "a", "b", true)]
    [TestCase("https://gitlab.com/a/b", "", "", false)]
    [TestCase("https://github.com/a", "", "", false)]
    [TestCase("not a url", "", "", false)]
    [TestCase(null, "", "", false)]
    public void TheRepositoryAddress_IsParsed(string? url, string owner, string repo, bool ok)
    {
        Assert.That(UpdateService.TryGitHubRepo(url, out var o, out var r), Is.EqualTo(ok));
        Assert.That((o, r), Is.EqualTo((owner, repo)));
    }

    [Test]
    public void TheBuild_CarriesItsUpdateSource()
        => Assert.That(UpdateService.RepositoryUrlOf(typeof(UpdateService).Assembly), Does.StartWith("https://github.com/"));
}
