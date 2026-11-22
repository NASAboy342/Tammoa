using Tammoa.Helper;
using Tammoa.Services.Tools;

namespace Tammoa.Test.Helper;

/// <summary>The restricted domain of the AI tools. Everything happens in disposable temp folders.</summary>
public class RootGuardTest
{
    private string _base = null!;
    private string _root = null!;
    private string _outside = null!;

    [SetUp]
    public void SetUp()
    {
        _base = Path.Combine(Path.GetTempPath(), "tammoa-guard-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_base, "root");
        _outside = Path.Combine(_base, "outside");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_base, true);

    private string Resolve(string path) => RootGuard.Resolve(_root, path);

    [Test]
    public void RelativeAndAbsolutePathsInsideTheRoot_AreAccepted()
    {
        Assert.That(Resolve("notes/a.txt"), Does.EndWith(Path.Combine("root", "notes", "a.txt")));
        Assert.That(Resolve(Path.Combine(_root, "b.txt")), Does.EndWith(Path.Combine("root", "b.txt")));
        Assert.That(Resolve("."), Does.EndWith("root"), "the root itself");
        Assert.That(Resolve("a/../b.txt"), Does.EndWith(Path.Combine("root", "b.txt")), ".. that stays inside is fine and is collapsed");
    }

    [TestCase("../outside/x.txt")]
    [TestCase("a/../../outside/x.txt")]
    [TestCase("../root-evil/x.txt")]
    public void DotDotThatLeavesTheRoot_IsRejected(string path) => Assert.Throws<ToolRejectedException>(() => Resolve(path));

    [Test]
    public void AbsolutePathOutsideTheRoot_IsRejected()
    {
        Assert.Throws<ToolRejectedException>(() => Resolve(Path.Combine(_outside, "x.txt")));
        Assert.Throws<ToolRejectedException>(() => Resolve(Path.GetTempPath()));
    }

    [Test]
    public void ASiblingFolderThatOnlySharesTheNamePrefix_IsOutside()
    {
        var evil = _root + "-evil";
        Directory.CreateDirectory(evil);
        Assert.Throws<ToolRejectedException>(() => Resolve(Path.Combine(evil, "x.txt")));
    }

    [Test]
    public void TildeIsExpandedToTheHomeFolder_SoItIsOutside()
    {
        Assert.Throws<ToolRejectedException>(() => Resolve("~/secret.txt"));
    }

    [Test]
    public void ASymbolicLinkInsideTheRootThatPointsOutside_IsRejected()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("symbolic links need privileges on Windows");
        Directory.CreateSymbolicLink(Path.Combine(_root, "link"), _outside);
        Assert.Throws<ToolRejectedException>(() => Resolve("link/x.txt"), "a file below the link");
        Assert.Throws<ToolRejectedException>(() => Resolve("link"), "the link itself");
        File.CreateSymbolicLink(Path.Combine(_root, "filelink.txt"), Path.Combine(_outside, "secret.txt"));
        Assert.Throws<ToolRejectedException>(() => Resolve("filelink.txt"), "a file link, even to a file that does not exist yet");
    }

    [Test]
    public void ASymbolicLinkInsideTheRootThatPointsInside_IsAccepted()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("symbolic links need privileges on Windows");
        Directory.CreateDirectory(Path.Combine(_root, "real"));
        Directory.CreateSymbolicLink(Path.Combine(_root, "alias"), Path.Combine(_root, "real"));
        Assert.That(Resolve("alias/x.txt"), Does.EndWith(Path.Combine("real", "x.txt")), "the returned path is the real one");
    }

    [Test]
    public void ARootThatIsItselfBehindALink_StillWorks()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("symbolic links need privileges on Windows");
        var viaLink = Path.Combine(_base, "rootlink");
        Directory.CreateSymbolicLink(viaLink, _root);
        Assert.DoesNotThrow(() => RootGuard.Resolve(viaLink, "x.txt"));
        Assert.Throws<ToolRejectedException>(() => RootGuard.Resolve(viaLink, "../outside/x.txt"));
    }

    [Test]
    public void EmptyPath_IsAnArgumentError() => Assert.Throws<ArgumentException>(() => Resolve(" "));
}
