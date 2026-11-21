using Newtonsoft.Json.Linq;
using NSubstitute;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Services;
using Tammoa.Services.Cards;

namespace Tammoa.Test.Services;

public class FlowDefinitionValidatorDataTest
{
    private FlowDefinitionValidator _sut = null!;

    [SetUp]
    public void SetUp()
    {
        var registry = Substitute.For<ICardRegistry>();
        ICardType Card(string type, bool source = false) { var c = Substitute.For<ICardType>(); c.Info.Returns(new CardTypeInfo(type, type, "", CardCategory.Data, "zap", []) { IsSource = source }); return c; }
        var data = Card(DataStoreCard.TypeId, true);
        var script = Card("script.shell");
        registry.Find(Arg.Any<string>()).Returns((ICardType?)null);
        registry.Find(DataStoreCard.TypeId).Returns(data);
        registry.Find("script.shell").Returns(script);
        _sut = new FlowDefinitionValidator(registry);
    }

    private static JObject Def(params string[] nodes) => JObject.Parse("{\"nodes\":[" + string.Join(",", nodes) + "],\"edges\":[]}");
    private static string D(string id, string name, string? scope = null) =>
        $"{{\"id\":\"{id}\",\"type\":\"data.store\",\"name\":{Newtonsoft.Json.JsonConvert.ToString(name)}" + (scope is null ? "" : $",\"params\":{{\"scope\":\"{scope}\"}}") + "}";
    private static string Script(string id, string name) => $"{{\"id\":\"{id}\",\"type\":\"script.shell\",\"name\":\"{name}\"}}";

    [Test]
    public void GoodDataCards_Pass() => Assert.DoesNotThrow(() => _sut.Validate(Def(D("a", "Style"), D("b", "Last answer", "flow"), D("c", "Shared", "shared"))));

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("a.b")]
    [TestCase("a[0]")]
    [TestCase("x|y")]
    [TestCase("{x}")]
    public void ANameThatCannotBeReferenced_IsRefusedWithTheReason(string name)
    {
        var ex = Assert.Throws<ArgumentException>(() => _sut.Validate(Def(D("a", name))));
        Assert.That(ex!.Message, Is.Not.Empty);
    }

    [Test]
    public void TwoDataCardsWithTheSameName_AreRefused_IgnoringCase()
    {
        var ex = Assert.Throws<ArgumentException>(() => _sut.Validate(Def(D("a", "Style"), D("b", "style"))));
        Assert.That(ex!.Message, Does.Contain("Two Data cards are called"));
    }

    [Test]
    public void TheSameNameOnAnotherKindOfCard_IsFine() => Assert.DoesNotThrow(() => _sut.Validate(Def(D("a", "Style"), Script("b", "Style"))));

    [Test]
    public void OtherCardsMayUseDotsInTheirNames() => Assert.DoesNotThrow(() => _sut.Validate(Def(Script("a", "v1.2 build [x]"))));

    [Test]
    public void AnUnknownScope_IsRefused() => Assert.Throws<ArgumentException>(() => _sut.Validate(Def(D("a", "Style", "universe"))));

    [Test]
    public void ADataCardMayHaveAnIncomingConnection_ThatIsHowAFlowWritesToIt()
    {
        var def = Def(Script("s", "S"), D("d", "Notes"));
        def["edges"] = JArray.Parse("[{\"source\":\"s\",\"target\":\"d\"}]");
        Assert.DoesNotThrow(() => _sut.Validate(def));
    }
}
