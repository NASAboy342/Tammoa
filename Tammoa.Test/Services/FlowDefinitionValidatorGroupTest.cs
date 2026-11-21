using Newtonsoft.Json.Linq;
using NSubstitute;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Services;

namespace Tammoa.Test.Services;

public class FlowDefinitionValidatorGroupTest
{
    private FlowDefinitionValidator _sut = null!;

    [SetUp]
    public void SetUp()
    {
        var registry = Substitute.For<ICardRegistry>();
        var action = Substitute.For<ICardType>();
        action.Info.Returns(new CardTypeInfo("action", "action", "", CardCategory.Action, "zap", []));
        registry.Find(Arg.Any<string>()).Returns((ICardType?)null);
        registry.Find("action").Returns(action);
        _sut = new FlowDefinitionValidator(registry);
    }

    private static JObject Def(string groups) =>
        JObject.Parse("{\"nodes\":[{\"id\":\"a\",\"type\":\"action\"},{\"id\":\"b\",\"type\":\"action\"},{\"id\":\"c\",\"type\":\"action\"}],\"edges\":[],\"groups\":" + groups + "}");

    [Test]
    public void NoGroups_IsFine() => Assert.DoesNotThrow(() => _sut.Validate(JObject.Parse("{\"nodes\":[],\"edges\":[]}")));

    [Test]
    public void NamedGroupsOfExistingCards_AreFine() =>
        Assert.DoesNotThrow(() => _sut.Validate(Def("[{\"id\":\"g1\",\"name\":\"Prepare\",\"nodeIds\":[\"a\",\"b\"]},{\"id\":\"g2\",\"name\":\"Ask\",\"nodeIds\":[\"c\"]}]")));

    [Test]
    public void AnEmptyGroup_IsFine() =>
        Assert.DoesNotThrow(() => _sut.Validate(Def("[{\"id\":\"g1\",\"name\":\"Later\",\"nodeIds\":[]}]")));

    [TestCase("{}")]
    [TestCase("\"x\"")]
    public void GroupsThatAreNotAnArray_Throw(string groups) =>
        Assert.Throws<ArgumentException>(() => _sut.Validate(Def(groups)));

    [Test]
    public void AGroupNeedsAnId_AUniqueOne() =>
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => _sut.Validate(Def("[{\"name\":\"X\",\"nodeIds\":[]}]")));
            Assert.Throws<ArgumentException>(() => _sut.Validate(Def("[{\"id\":\"g\",\"name\":\"X\",\"nodeIds\":[]},{\"id\":\"g\",\"name\":\"Y\",\"nodeIds\":[]}]")));
        });

    [TestCase("")]
    [TestCase("   ")]
    public void AGroupNeedsAName(string name) =>
        Assert.Throws<ArgumentException>(() => _sut.Validate(Def("[{\"id\":\"g\",\"name\":\"" + name + "\",\"nodeIds\":[]}]")));

    [Test]
    public void AGroupNameIsLimited()
    {
        Assert.DoesNotThrow(() => _sut.Validate(Def("[{\"id\":\"g\",\"name\":\"" + new string('x', 60) + "\",\"nodeIds\":[]}]")));
        Assert.Throws<ArgumentException>(() => _sut.Validate(Def("[{\"id\":\"g\",\"name\":\"" + new string('x', 61) + "\",\"nodeIds\":[]}]")));
    }

    [Test]
    public void AGroupCannotHoldACardThatDoesNotExist() =>
        Assert.Throws<ArgumentException>(() => _sut.Validate(Def("[{\"id\":\"g\",\"name\":\"X\",\"nodeIds\":[\"a\",\"ghost\"]}]")));

    [Test]
    public void ACardCannotBeInTwoGroups() =>
        Assert.Throws<ArgumentException>(() => _sut.Validate(Def("[{\"id\":\"g1\",\"name\":\"X\",\"nodeIds\":[\"a\"]},{\"id\":\"g2\",\"name\":\"Y\",\"nodeIds\":[\"a\",\"b\"]}]")));

    [Test]
    public void ACardTwiceInOneGroup_IsRefusedToo() =>
        Assert.Throws<ArgumentException>(() => _sut.Validate(Def("[{\"id\":\"g\",\"name\":\"X\",\"nodeIds\":[\"a\",\"a\"]}]")));

    [Test]
    public void NodeIdsMustBeAnArray() =>
        Assert.Throws<ArgumentException>(() => _sut.Validate(Def("[{\"id\":\"g\",\"name\":\"X\",\"nodeIds\":\"a\"}]")));
}
