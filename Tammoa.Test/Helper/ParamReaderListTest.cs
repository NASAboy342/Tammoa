using Newtonsoft.Json.Linq;
using Tammoa.Helper;

namespace Tammoa.Test.Helper;

public class ParamReaderListTest
{
    [Test]
    public void StringList_ReadsArraysAndSeparatedText_DropsEmptyAndDuplicates()
    {
        Assert.That(ParamReader.GetStringList(JObject.Parse("{\"a\":[\"x\",\" y \",\"x\",\"\"]}"), "a"), Is.EqualTo(new[] { "x", "y" }));
        Assert.That(ParamReader.GetStringList(JObject.Parse("{\"a\":\"p.test, q.test;\\nr.test,,\"}"), "a"), Is.EqualTo(new[] { "p.test", "q.test", "r.test" }));
        Assert.That(ParamReader.GetStringList(JObject.Parse("{\"a\":null}"), "a"), Is.Empty);
        Assert.That(ParamReader.GetStringList(new JObject(), "a"), Is.Empty);
    }
}
