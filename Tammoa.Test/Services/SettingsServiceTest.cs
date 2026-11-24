using Tammoa.Models;
using Tammoa.Repositories;
using Tammoa.Services;

namespace Tammoa.Test.Services;

public class SettingsServiceTest
{
    private string _dir = null!;
    private string File_ => Path.Combine(_dir, "settings.json");

    [SetUp] public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "tammoa-settings-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); }
    [TearDown] public void TearDown() => Directory.Delete(_dir, true);

    private SettingsService Make() => new(new SettingsRepository(File_));

    [Test]
    public void FirstUse_NoAutomaticUpdateCheck()
        => Assert.That(Make().Get(), Is.EqualTo(new AppSettings(false)));

    [Test]
    public void Update_IsSaved_AndSurvivesARestart()
    {
        Assert.That(Make().Update(new SaveSettingsRequest(true)).CheckUpdatesAtStart, Is.True);
        Assert.That(Make().Get().CheckUpdatesAtStart, Is.True, "a new service reads the same file");
    }

    [Test]
    public void Update_WithoutAField_KeepsWhatWasSaved()
    {
        var s = Make();
        s.Update(new SaveSettingsRequest(true));
        Assert.That(s.Update(new SaveSettingsRequest(null)).CheckUpdatesAtStart, Is.True);
    }

    [Test]
    public void ADamagedFile_MeansDefaults_NotACrash()
    {
        File.WriteAllText(File_, "{ not json");
        Assert.That(Make().Get(), Is.EqualTo(new AppSettings(false)));
        Assert.That(Make().Update(new SaveSettingsRequest(true)).CheckUpdatesAtStart, Is.True, "and it can be saved over");
    }
}
