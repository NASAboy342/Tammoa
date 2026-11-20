using Newtonsoft.Json.Linq;
using NSubstitute;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Services;

namespace Tammoa.Test.Services;

public class FlowDefinitionValidatorTest
{
    private FlowDefinitionValidator _sut = null!;

    private static ICardType Card(string type, CardCategory cat)
    {
        var c = Substitute.For<ICardType>();
        c.Info.Returns(new CardTypeInfo(type, type, "", cat, "zap", []));
        return c;
    }

    [SetUp]
    public void SetUp()
    {
        var registry = Substitute.For<ICardRegistry>();
        var trigger = Card("trigger", CardCategory.Trigger); // build first: configuring a substitute inside another .Returns(...) breaks NSubstitute
        var action = Card("action", CardCategory.Action);
        registry.Find(Arg.Any<string>()).Returns((ICardType?)null); // unknown types must be null, not an auto-substitute
        registry.Find("trigger").Returns(trigger);
        registry.Find("action").Returns(action);
        _sut = new FlowDefinitionValidator(registry);
    }

    private static JObject Def(string nodes, string edges = "[]") => JObject.Parse($"{{\"nodes\":{nodes},\"edges\":{edges}}}");
    private const string Two = "[{\"id\":\"t\",\"type\":\"trigger\"},{\"id\":\"a\",\"type\":\"action\",\"name\":\"A\",\"position\":{\"x\":1,\"y\":2.5},\"params\":{\"k\":1}}]";

    [Test]
    public void Validate_EmptyAndMissingArrays_Pass()
    {
        Assert.DoesNotThrow(() => _sut.Validate(new JObject()));
        Assert.DoesNotThrow(() => _sut.Validate(Def("[]")));
    }

    [Test]
    public void Validate_GoodGraph_Passes() =>
        Assert.DoesNotThrow(() => _sut.Validate(Def(Two, "[{\"id\":\"e1\",\"source\":\"t\",\"target\":\"a\"}]")));

    [TestCase("{\"nodes\":5}")]
    [TestCase("{\"edges\":\"x\"}")]
    public void Validate_NotArrays_Throws(string json) => Assert.Throws<ArgumentException>(() => _sut.Validate(JObject.Parse(json)));

    [Test]
    public void Validate_DuplicateIds_Throws() =>
        Assert.Throws<ArgumentException>(() => _sut.Validate(Def("[{\"id\":\"x\",\"type\":\"action\"},{\"id\":\"x\",\"type\":\"action\"}]")));

    [TestCase("[{\"type\":\"action\"}]")]                     // no id
    [TestCase("[{\"id\":\"\",\"type\":\"action\"}]")]
    [TestCase("[{\"id\":\"x\",\"type\":\"nope\"}]")]          // unknown type
    [TestCase("[{\"id\":\"x\"}]")]                            // no type
    [TestCase("[5]")]
    [TestCase("[{\"id\":\"x\",\"type\":\"action\",\"params\":5}]")]
    [TestCase("[{\"id\":\"x\",\"type\":\"action\",\"position\":{\"x\":\"a\",\"y\":1}}]")]
    [TestCase("[{\"id\":\"x\",\"type\":\"action\",\"name\":5}]")]
    public void Validate_BadNode_Throws(string nodes) => Assert.Throws<ArgumentException>(() => _sut.Validate(Def(nodes)));

    [Test]
    public void Validate_TooManyNodes_Throws()
    {
        var nodes = new JArray(Enumerable.Range(0, FlowDefinitionValidator.MaxNodes + 1).Select(i => new JObject { ["id"] = $"n{i}", ["type"] = "action" }));
        Assert.Throws<ArgumentException>(() => _sut.Validate(new JObject { ["nodes"] = nodes }));
    }

    [TestCase("[{\"source\":\"t\",\"target\":\"zzz\"}]")]                                           // dangling
    [TestCase("[{\"source\":\"a\",\"target\":\"a\"}]")]                                             // self loop
    [TestCase("[{\"source\":\"t\",\"target\":\"a\"},{\"source\":\"t\",\"target\":\"a\"}]")]         // duplicate
    [TestCase("[{\"source\":\"a\",\"target\":\"t\"}]")]                                             // into a trigger
    public void Validate_BadEdge_Throws(string edges) => Assert.Throws<ArgumentException>(() => _sut.Validate(Def(Two, edges)));

    [Test]
    public void Validate_Cycle_ThrowsWithLoopMessage()
    {
        const string nodes = "[{\"id\":\"a\",\"type\":\"action\"},{\"id\":\"b\",\"type\":\"action\"},{\"id\":\"c\",\"type\":\"action\"}]";
        const string edges = "[{\"source\":\"a\",\"target\":\"b\"},{\"source\":\"b\",\"target\":\"c\"},{\"source\":\"c\",\"target\":\"a\"}]";
        var ex = Assert.Throws<ArgumentException>(() => _sut.Validate(Def(nodes, edges)));
        Assert.That(ex!.Message, Does.Contain("Loops"));
    }

    [Test]
    public void Validate_DiamondIsNotACycle()
    {
        const string nodes = "[{\"id\":\"t\",\"type\":\"trigger\"},{\"id\":\"a\",\"type\":\"action\"},{\"id\":\"b\",\"type\":\"action\"},{\"id\":\"d\",\"type\":\"action\"}]";
        const string edges = "[{\"source\":\"t\",\"target\":\"a\"},{\"source\":\"t\",\"target\":\"b\"},{\"source\":\"a\",\"target\":\"d\"},{\"source\":\"b\",\"target\":\"d\"}]";
        Assert.DoesNotThrow(() => _sut.Validate(Def(nodes, edges)));
    }
}
