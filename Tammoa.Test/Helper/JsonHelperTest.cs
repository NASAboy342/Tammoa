using Tammoa.Helper;

namespace Tammoa.Test.Helper;

public class JsonHelperTest
{
    private record Sample(string FirstName, string? Missing);

    [Test]
    public void Serialize_Record_UsesCamelCaseAndSkipsNulls()
    {
        var json = JsonHelper.Serialize(new Sample("Ada", null));
        Assert.That(json, Is.EqualTo("{\"firstName\":\"Ada\"}"));
    }

    [Test]
    public void Deserialize_CamelCase_RoundTrips()
    {
        var back = JsonHelper.Deserialize<Sample>("{\"firstName\":\"Ada\"}");
        Assert.That(back, Is.EqualTo(new Sample("Ada", null)));
    }
}
