using Tammoa.Services;

namespace Tammoa.Test.Services;

public class AppInfoServiceTest
{
    [Test]
    public void GetVersion_Default_ReturnsNameAndNonEmptyVersion()
    {
        var sut = new AppInfoService();

        var info = sut.GetVersion();

        Assert.That(info.Name, Is.EqualTo("Tammoa"));
        Assert.That(info.Version, Is.Not.Empty);
    }
}
