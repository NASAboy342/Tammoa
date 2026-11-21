using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Tammoa.Helper;

namespace Tammoa.Test.Helper;

public class SmallHelpersTest
{
    [Test]
    public async Task EndpointHelpers_KeyNotFound_Returns404()
    {
        var result = await EndpointHelpers.Try(() => throw new KeyNotFoundException("gone"));
        Assert.That(((ContentHttpResult)result).StatusCode, Is.EqualTo(404));
    }

    [Test]
    public void FilePaths_ExpandsTilde_AndRequiresAbsolute()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.That(FilePaths.Expand("~"), Is.EqualTo(home));
        Assert.That(FilePaths.Expand("~/a/b.txt"), Is.EqualTo(Path.Combine(home, "a/b.txt")));
        Assert.That(FilePaths.Expand("/abs"), Is.EqualTo("/abs"));
        Assert.That(FilePaths.RequireAbsolute("~/x"), Is.EqualTo(Path.GetFullPath(Path.Combine(home, "x"))));
        Assert.Throws<ArgumentException>(() => FilePaths.RequireAbsolute("rel/x"));
        Assert.Throws<ArgumentException>(() => FilePaths.RequireAbsolute("  "));
    }

    [Test, Platform(Exclude = "Win")]
    public void DataPaths_RestrictToOwner_MakesTheFolderPrivate()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tammoa-private-" + Guid.NewGuid().ToString("N"));
        try
        {
            DataPaths.RestrictToOwner(dir);
            Assert.That(File.GetUnixFileMode(dir), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Test]
    public void DataPaths_EnvironmentOverrideWins()
    {
        var old = Environment.GetEnvironmentVariable(DataPaths.DataDirEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(DataPaths.DataDirEnvVar, "/tmp/somewhere");
            Assert.That(DataPaths.DataDir(), Is.EqualTo("/tmp/somewhere"));
            Assert.That(DataPaths.DatabaseFile(), Is.EqualTo(Path.Combine("/tmp/somewhere", "tammoa.db")));
        }
        finally { Environment.SetEnvironmentVariable(DataPaths.DataDirEnvVar, old); }
    }
}
