using Newtonsoft.Json.Linq;
using NSubstitute;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Services;

namespace Tammoa.Test.Services;

public class FlowDefinitionValidatorHandlesTest
{
    private FlowDefinitionValidator _sut = null!;

    private static ICardType Card(string type, CardCategory cat, params string[] outputs)
    {
        var c = Substitute.For<ICardType>();
        var info = new CardTypeInfo(type, type, "", cat, "zap", []);
        if (outputs.Length > 0) info = info with { Outputs = outputs.Select(o => new CardOutputInfo(o, o)).ToList() };
        c.Info.Returns(info);
        return c;
    }

    [SetUp]
    public void SetUp()
    {
        var registry = Substitute.For<ICardRegistry>();
        var trigger = Card("trigger", CardCategory.Trigger);
        var ifCard = Card("if", CardCategory.Logic, "true", "false");
        var action = Card("action", CardCategory.Action);
        registry.Find(Arg.Any<string>()).Returns((ICardType?)null);
        registry.Find("trigger").Returns(trigger);
        registry.Find("if").Returns(ifCard);
        registry.Find("action").Returns(action);
        _sut = new FlowDefinitionValidator(registry);
    }

    private static JObject Def(string edges) => JObject.Parse("{\"nodes\":[{\"id\":\"t\",\"type\":\"trigger\"},{\"id\":\"i\",\"type\":\"if\"},{\"id\":\"a\",\"type\":\"action\"},{\"id\":\"b\",\"type\":\"action\"}],\"edges\":" + edges + "}");

    [Test]
    public void BothHandlesOfABranchingCard_AreFine() =>
        Assert.DoesNotThrow(() => _sut.Validate(Def("[{\"source\":\"t\",\"target\":\"i\"},{\"source\":\"i\",\"sourceHandle\":\"true\",\"target\":\"a\"},{\"source\":\"i\",\"sourceHandle\":\"false\",\"target\":\"b\"}]")));

    [Test]
    public void SameTargetFromTwoHandles_IsNotADuplicate() =>
        Assert.DoesNotThrow(() => _sut.Validate(Def("[{\"source\":\"i\",\"sourceHandle\":\"true\",\"target\":\"a\"},{\"source\":\"i\",\"sourceHandle\":\"false\",\"target\":\"a\"}]")));

    [Test]
    public void SameHandleTwice_IsADuplicate() =>
        Assert.Throws<ArgumentException>(() => _sut.Validate(Def("[{\"source\":\"i\",\"sourceHandle\":\"true\",\"target\":\"a\"},{\"source\":\"i\",\"sourceHandle\":\"true\",\"target\":\"a\"}]")));

    [TestCase("[{\"source\":\"i\",\"target\":\"a\"}]")]                                      // IF has no "main"
    [TestCase("[{\"source\":\"a\",\"sourceHandle\":\"true\",\"target\":\"b\"}]")]            // action has no "true"
    [TestCase("[{\"source\":\"i\",\"sourceHandle\":5,\"target\":\"a\"}]")]
    public void UnknownHandle_Throws(string edges)
    {
        var ex = Assert.Throws<ArgumentException>(() => _sut.Validate(Def(edges)));
        Assert.That(ex!.Message, Does.Contain("output"));
    }
}
