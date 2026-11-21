using Newtonsoft.Json.Linq;
using NSubstitute;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Services;

namespace Tammoa.Test.Services;

public class FlowDefinitionValidatorLoopTest
{
    private FlowDefinitionValidator _sut = null!;

    [SetUp]
    public void SetUp()
    {
        var registry = Substitute.For<ICardRegistry>();
        ICardType Card(string type, CardCategory cat) { var c = Substitute.For<ICardType>(); c.Info.Returns(new CardTypeInfo(type, type, "", cat, "zap", [])); return c; }
        var trigger = Card("trigger", CardCategory.Trigger);
        var action = Card("action", CardCategory.Action);
        registry.Find(Arg.Any<string>()).Returns((ICardType?)null);
        registry.Find("trigger").Returns(trigger);
        registry.Find("action").Returns(action);
        _sut = new FlowDefinitionValidator(registry);
    }

    private static JObject Def(string edges, string extra = "") => JObject.Parse("{\"nodes\":[{\"id\":\"t\",\"type\":\"trigger\"},{\"id\":\"a\",\"type\":\"action\"},{\"id\":\"b\",\"type\":\"action\"}],\"edges\":" + edges + extra + "}");

    [Test]
    public void ALoopBackToAnEarlierCard_IsFine() =>
        Assert.DoesNotThrow(() => _sut.Validate(Def("[{\"source\":\"t\",\"target\":\"a\"},{\"source\":\"a\",\"target\":\"b\"},{\"source\":\"b\",\"target\":\"a\"}]")));

    [Test]
    public void ACardCannotConnectToItself_EvenNow() =>
        Assert.Throws<ArgumentException>(() => _sut.Validate(Def("[{\"source\":\"a\",\"target\":\"a\"}]")));

    [Test]
    public void NothingMayConnectIntoATrigger_NotEvenAsALoop() =>
        Assert.Throws<ArgumentException>(() => _sut.Validate(Def("[{\"source\":\"a\",\"target\":\"t\"}]")));

    [Test]
    public void TheSameConnectionTwice_IsStillRefused() =>
        Assert.Throws<ArgumentException>(() => _sut.Validate(Def("[{\"source\":\"a\",\"target\":\"b\"},{\"source\":\"a\",\"target\":\"b\"}]")));

    [TestCase(1)]
    [TestCase(50)]
    [TestCase(1000)]
    public void LoopLimit_InRange_IsFine(int limit) =>
        Assert.DoesNotThrow(() => _sut.Validate(JObject.Parse($"{{\"nodes\":[],\"edges\":[],\"settings\":{{\"loopLimit\":{limit}}}}}")));

    [TestCase("0")]
    [TestCase("1001")]
    [TestCase("-5")]
    [TestCase("2.5")]
    [TestCase("\"ten\"")]
    public void LoopLimit_OutOfRangeOrNotAWholeNumber_IsRefusedWithTheReason(string limit)
    {
        var ex = Assert.Throws<ArgumentException>(() => _sut.Validate(JObject.Parse($"{{\"nodes\":[],\"edges\":[],\"settings\":{{\"loopLimit\":{limit}}}}}")));
        Assert.That(ex!.Message, Does.Contain("loop limit"));
    }

    [Test]
    public void SettingsMustBeAnObject_ButMayBeMissingOrHaveNoLimit()
    {
        Assert.DoesNotThrow(() => _sut.Validate(JObject.Parse("{\"nodes\":[],\"edges\":[]}")));
        Assert.DoesNotThrow(() => _sut.Validate(JObject.Parse("{\"nodes\":[],\"edges\":[],\"settings\":{}}")));
        Assert.Throws<ArgumentException>(() => _sut.Validate(JObject.Parse("{\"nodes\":[],\"edges\":[],\"settings\":5}")));
    }

    private static JObject WithSize(string size) => JObject.Parse("{\"nodes\":[{\"id\":\"a\",\"type\":\"action\",\"size\":" + size + "}],\"edges\":[]}");

    [TestCase("{\"w\":208,\"h\":60}")]
    [TestCase("{\"w\":40,\"h\":40}")]
    [TestCase("{\"w\":1600,\"h\":1600}")]
    [TestCase("{\"w\":300.5,\"h\":90.25}")]
    [TestCase("null")]
    public void CardSize_Valid_IsFine(string size) => Assert.DoesNotThrow(() => _sut.Validate(WithSize(size)));

    [TestCase("{\"w\":39,\"h\":60}")]
    [TestCase("{\"w\":208,\"h\":1601}")]
    [TestCase("{\"w\":\"wide\",\"h\":60}")]
    [TestCase("{\"w\":208}")]
    [TestCase("5")]
    [TestCase("{\"w\":-1,\"h\":-1}")]
    public void CardSize_Invalid_IsRefused(string size)
    {
        var ex = Assert.Throws<ArgumentException>(() => _sut.Validate(WithSize(size)));
        Assert.That(ex!.Message, Does.Contain("size"));
    }
}
