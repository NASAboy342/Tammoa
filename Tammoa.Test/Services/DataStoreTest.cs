using Newtonsoft.Json.Linq;
using NSubstitute;
using Tammoa.Enums;
using Tammoa.Helper;
using Tammoa.Models;
using Tammoa.Repositories;
using Tammoa.Services;
using Tammoa.Services.Cards;

namespace Tammoa.Test.Services;

public class DataNamesTest
{
    [TestCase("Style")]
    [TestCase("Last answer")]
    [TestCase("my-notes_2")]
    [TestCase("ខ្មែរ")]
    public void GoodNames(string name) => Assert.That(DataNames.Problem(name), Is.Null);

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("a.b")]
    [TestCase("a[0]")]
    [TestCase("a|b")]
    [TestCase("{a}")]
    public void BadNames_AreExplained(string? name) => Assert.That(DataNames.Problem(name), Is.Not.Null.And.Not.Empty);

    [Test]
    public void TooLong_IsRefused() => Assert.That(DataNames.Problem(new string('a', DataNames.MaxLength + 1)), Does.Contain("limited"));

    [Test]
    public void TheMessageSaysWhichCharacterIsTheProblem() => Assert.That(DataNames.Problem("a.b"), Does.Contain("'.'"));

    [TestCase("Notes", "notes.txt")]
    [TestCase("My Notes", "my notes.txt")]
    [TestCase("a/b\\c", "a_b_c.txt")]
    [TestCase("..", "__.txt")]
    [TestCase("", "_.txt")]
    public void SharedFileNames_AreLowerCaseAndSafe(string name, string expected) => Assert.That(DataNames.FileName(name), Is.EqualTo(expected));
}

public class DataRepositoryTest
{
    private string _dir = null!;
    private DataRepository _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tammoa-datarepo-" + Guid.NewGuid().ToString("N"));
        _sut = new DataRepository(_dir);
    }

    [TearDown]
    public void TearDown() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static DataLocation Flow(string flow, string card, string name = "N") => new(DataScope.Flow, flow, card, name);
    private static DataLocation Shared(string name) => new(DataScope.Shared, "f", "c", name);

    [Test]
    public void Nothing_ThenWriteAndReadBack_InAFreshInstance()
    {
        Assert.That(_sut.Read(Flow("f1", "c1")), Is.Null);
        Assert.That(_sut.Modified(Flow("f1", "c1")), Is.Null);
        _sut.Write(Flow("f1", "c1"), "héllo\nwörld ✓");
        Assert.That(new DataRepository(_dir).Read(Flow("f1", "c1")), Is.EqualTo("héllo\nwörld ✓"));
        Assert.That(_sut.Modified(Flow("f1", "c1")), Is.Not.Null);
    }

    [Test]
    public void TheFileIsAPlainTxtFile_YouCanOpenInAnyEditor()
    {
        _sut.Write(Flow("f1", "c1"), "plain");
        var path = _sut.PathFor(Flow("f1", "c1"));
        Assert.That(path, Does.EndWith(Path.Combine("data", "flows", "f1", "c1.txt")));
        Assert.That(File.ReadAllText(path), Is.EqualTo("plain"));
    }

    [Test]
    public void Overwrite_LeavesNoTempFile_AndEmptyTextIsAValidValue()
    {
        _sut.Write(Flow("f1", "c1"), "one");
        _sut.Write(Flow("f1", "c1"), "");
        Assert.That(_sut.Read(Flow("f1", "c1")), Is.EqualTo(""));
        Assert.That(Directory.GetFiles(Path.GetDirectoryName(_sut.PathFor(Flow("f1", "c1")))!), Has.Length.EqualTo(1));
    }

    [Test, Platform(Exclude = "Win")]
    public void FilesAreOwnerOnly()
    {
        _sut.Write(Flow("f1", "c1"), "private");
        Assert.That(File.GetUnixFileMode(_sut.PathFor(Flow("f1", "c1"))), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
    }

    [Test]
    public void FlowScope_IsPerFlow_SharedScope_IsByNameAcrossFlows()
    {
        _sut.Write(Flow("f1", "c1"), "mine");
        Assert.That(_sut.Read(Flow("f2", "c1")), Is.Null, "another flow has its own");
        _sut.Write(new DataLocation(DataScope.Shared, "f1", "c1", "Notes"), "shared text");
        Assert.That(_sut.Read(new DataLocation(DataScope.Shared, "f2", "other-card", "notes")), Is.EqualTo("shared text"), "same name (any case), any flow, any card id");
        Assert.That(_sut.PathFor(Shared("Notes")), Does.EndWith(Path.Combine("data", "shared", "notes.txt")));
    }

    [Test]
    public void IdsAndNamesCannotClimbOutOfTheFolder()
    {
        var evil = Flow("../../etc", "../passwd");
        _sut.Write(evil, "x");
        Assert.That(Path.GetFullPath(_sut.PathFor(evil)), Does.StartWith(Path.GetFullPath(_dir)));
        _sut.Write(Shared("../../outside"), "x");
        Assert.That(Path.GetFullPath(_sut.PathFor(Shared("../../outside"))), Does.StartWith(Path.GetFullPath(_dir)));
    }

    [Test]
    public void DeleteFlow_RemovesThatFlowsFiles_NotOthersNorShared()
    {
        _sut.Write(Flow("f1", "a"), "1");
        _sut.Write(Flow("f1", "b"), "2");
        _sut.Write(Flow("f2", "a"), "3");
        _sut.Write(Shared("Keep"), "s");
        _sut.DeleteFlow("f1");
        Assert.That(_sut.Read(Flow("f1", "a")), Is.Null);
        Assert.That(_sut.Read(Flow("f1", "b")), Is.Null);
        Assert.That(_sut.Read(Flow("f2", "a")), Is.EqualTo("3"));
        Assert.That(_sut.Read(Shared("Keep")), Is.EqualTo("s"));
        Assert.DoesNotThrow(() => _sut.DeleteFlow("never-existed"));
    }

    [Test]
    public void CopyFlow_CopiesEveryFileToTheNewFlow()
    {
        _sut.Write(Flow("f1", "a"), "1");
        _sut.Write(Flow("f1", "b"), "2");
        _sut.CopyFlow("f1", "f9");
        Assert.That(_sut.Read(Flow("f9", "a")), Is.EqualTo("1"));
        Assert.That(_sut.Read(Flow("f9", "b")), Is.EqualTo("2"));
        _sut.Write(Flow("f9", "a"), "changed");
        Assert.That(_sut.Read(Flow("f1", "a")), Is.EqualTo("1"), "the copy is independent");
        Assert.DoesNotThrow(() => _sut.CopyFlow("none", "f10"));
    }
}

public class DataServiceTest
{
    private string _dir = null!;
    private IFlowRepository _flows = null!;
    private IRunEventBus _bus = null!;
    private DataService _sut = null!;

    private static JObject Def(params (string Id, string Type, string Name, string? Scope)[] nodes) => new()
    {
        ["nodes"] = new JArray(nodes.Select(n => new JObject { ["id"] = n.Id, ["type"] = n.Type, ["name"] = n.Name, ["params"] = n.Scope is null ? new JObject() : new JObject { ["scope"] = n.Scope } })),
        ["edges"] = new JArray(),
    };

    private void FlowWith(JObject def) => _flows.Get("f1").Returns(new Flow("f1", "Flow", "", false, def, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tammoa-dataservice-" + Guid.NewGuid().ToString("N"));
        _flows = Substitute.For<IFlowRepository>();
        _flows.Get(Arg.Any<string>()).Returns((Flow?)null);
        _bus = Substitute.For<IRunEventBus>();
        _sut = new DataService(new DataRepository(_dir), _flows, _bus);
        FlowWith(Def(("d1", "data.store", "Style", null), ("d2", "data.store", "Settings", null), ("s1", "script.shell", "Not data", null), ("d3", "data.store", "Shared notes", "shared")));
    }

    [TearDown]
    public void TearDown() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Test]
    public void WriteThenRead_ByFlowAndCard_AndEveryWindowIsTold()
    {
        var saved = _sut.Write("f1", "d1", "Be brief.");
        Assert.That(saved.Text, Is.EqualTo("Be brief."));
        Assert.That(saved.Exists, Is.True);
        Assert.That(saved.Size, Is.EqualTo(9));
        Assert.That(saved.Name, Is.EqualTo("Style"));
        Assert.That(saved.Scope, Is.EqualTo("flow"));
        Assert.That(saved.Path, Does.EndWith("d1.txt"));
        Assert.That(_sut.Read("f1", "d1").Text, Is.EqualTo("Be brief."));
        _bus.Received(1).PublishDataChanged("f1", "d1");
    }

    [Test]
    public void ReadingBeforeAnythingWasSaved_IsEmptyNotAnError()
    {
        var e = _sut.Read("f1", "d2");
        Assert.That(e.Text, Is.EqualTo(""));
        Assert.That(e.Exists, Is.False);
        Assert.That(e.UpdatedAt, Is.Null);
    }

    [Test]
    public void EmptyOrNullText_ClearsIt()
    {
        _sut.Write("f1", "d1", "something");
        Assert.That(_sut.Write("f1", "d1", null).Text, Is.EqualTo(""));
        Assert.That(_sut.Read("f1", "d1").Text, Is.EqualTo(""));
        Assert.That(_sut.Read("f1", "d1").Exists, Is.True, "cleared, not missing");
    }

    [Test]
    public void TooLong_IsRefused_AndNothingIsWritten()
    {
        Assert.Throws<ArgumentException>(() => _sut.Write("f1", "d1", new string('x', IDataService.MaxLength + 1)));
        Assert.That(_sut.Read("f1", "d1").Exists, Is.False);
        _bus.DidNotReceiveWithAnyArgs().PublishDataChanged(default!, default!);
        Assert.DoesNotThrow(() => _sut.Write("f1", "d1", new string('x', IDataService.MaxLength)));
    }

    [TestCase("f1", "s1")]        // a card that is not a Data card
    [TestCase("f1", "nope")]
    [TestCase("missing", "d1")]
    public void NotADataCardOfThatFlow_Is404(string flow, string card)
    {
        Assert.Throws<KeyNotFoundException>(() => _sut.Read(flow, card));
        Assert.Throws<KeyNotFoundException>(() => _sut.Write(flow, card, "x"));
    }

    [Test]
    public void SharedScope_IsSharedByNameBetweenFlows()
    {
        _sut.Write("f1", "d3", "hello everyone");
        _flows.Get("f2").Returns(new Flow("f2", "Other", "", false, Def(("zz", "data.store", "SHARED NOTES", "shared")), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        Assert.That(_sut.Read("f2", "zz").Text, Is.EqualTo("hello everyone"));
    }

    [Test]
    public void List_ShowsDataCardsOnly_WithPreviewSizeAndJsonFlag()
    {
        _sut.Write("f1", "d1", new string('a', 500));
        _sut.Write("f1", "d2", "{\"city\":\"Phnom Penh\"}");
        var list = _sut.List("f1");
        Assert.That(list.Select(l => l.Name), Is.EquivalentTo(new[] { "Style", "Settings", "Shared notes" }));
        var style = list.Single(l => l.Name == "Style");
        Assert.That(style.Preview, Has.Length.EqualTo(201));
        Assert.That(style.Preview, Does.EndWith("…"));
        Assert.That(style.Size, Is.EqualTo(500));
        Assert.That(style.IsJson, Is.False);
        Assert.That(list.Single(l => l.Name == "Settings").IsJson, Is.True);
        Assert.That(list.Single(l => l.Name == "Shared notes").Scope, Is.EqualTo("shared"));
        Assert.Throws<KeyNotFoundException>(() => _sut.List("missing"));
    }

    [TestCase("{\"a\":1}", JTokenType.Object)]
    [TestCase("  \n {\"a\":1}", JTokenType.Object)]
    [TestCase("[1,2]", JTokenType.Array)]
    [TestCase("123", JTokenType.String)]            // a number stays text: only objects and lists become data
    [TestCase("true", JTokenType.String)]
    [TestCase("\"quoted\"", JTokenType.String)]
    [TestCase("{not json", JTokenType.String)]
    [TestCase("plain text", JTokenType.String)]
    [TestCase("", JTokenType.String)]
    public void ToValue_OnlyJsonObjectsAndListsBecomeData(string text, JTokenType expected) => Assert.That(_sut.ToValue(text).Type, Is.EqualTo(expected));

    [Test]
    public void LoadContext_NameToValue_ForEveryDataCardOfTheDefinition()
    {
        _sut.Write("f1", "d1", "Be brief.");
        _sut.Write("f1", "d2", "{\"city\":\"Phnom Penh\",\"days\":3}");
        var ctx = _sut.LoadContext("f1", _flows.Get("f1")!.Definition);
        Assert.That((string?)ctx["Style"], Is.EqualTo("Be brief."));
        Assert.That((string?)ctx["Settings"]!["city"], Is.EqualTo("Phnom Penh"));
        Assert.That((int)ctx["Settings"]!["days"]!, Is.EqualTo(3));
        Assert.That((string?)ctx["Shared notes"], Is.EqualTo(""), "a Data card with nothing saved yet is an empty text");
        Assert.That(ctx.ContainsKey("Not data"), Is.False);
    }

    [Test]
    public void LoadContext_SeesTheLatestText_EachTimeItIsCalled()
    {
        var def = _flows.Get("f1")!.Definition;
        Assert.That((string?)_sut.LoadContext("f1", def)["Style"], Is.EqualTo(""));
        _sut.Write("f1", "d1", "new");
        Assert.That((string?)_sut.LoadContext("f1", def)["Style"], Is.EqualTo("new"));
    }

    [Test]
    public void LoadContext_NoDataCards_IsEmpty() => Assert.That(_sut.LoadContext("f1", Def(("a", "script.shell", "A", null))), Is.Empty);

    [Test]
    public void ExportThenImport_CarriesTheFlowsOwnDataOnly()
    {
        _sut.Write("f1", "d1", "style text");
        _sut.Write("f1", "d3", "shared text");              // shared: not part of the flow's file
        var def = _flows.Get("f1")!.Definition;
        var exported = _sut.ExportFlowData("f1", def);
        Assert.That(exported.Properties().Select(p => p.Name), Is.EqualTo(new[] { "d1" }), "only non-empty, flow-scoped texts");
        _flows.Get("f2").Returns(new Flow("f2", "New", "", false, def, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        _sut.ImportFlowData("f2", def, exported);
        Assert.That(_sut.Read("f2", "d1").Text, Is.EqualTo("style text"));
        Assert.That(_sut.Read("f2", "d2").Exists, Is.False);
    }

    [Test]
    public void Import_IgnoresUnknownCardsOversizeTextAndNonText_AndNull()
    {
        var def = _flows.Get("f1")!.Definition;
        _flows.Get("f3").Returns(new Flow("f3", "New", "", false, def, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var data = new JObject { ["d1"] = new string('x', IDataService.MaxLength + 1), ["d2"] = 42, ["ghost"] = "boo" };
        Assert.DoesNotThrow(() => _sut.ImportFlowData("f3", def, data));
        Assert.DoesNotThrow(() => _sut.ImportFlowData("f3", def, null));
        Assert.That(_sut.Read("f3", "d1").Exists, Is.False);
        Assert.That(_sut.Read("f3", "d2").Exists, Is.False);
    }

    [Test]
    public void DeleteAndCopy_FollowTheFlow()
    {
        _sut.Write("f1", "d1", "x");
        _sut.CopyFlowData("f1", "f2");
        var def = _flows.Get("f1")!.Definition; // read first: calling another substitute member inside .Returns(...) breaks NSubstitute
        _flows.Get("f2").Returns(new Flow("f2", "Copy", "", false, def, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        Assert.That(_sut.Read("f2", "d1").Text, Is.EqualTo("x"));
        _sut.DeleteFlowData("f1");
        Assert.That(_sut.Read("f1", "d1").Exists, Is.False);
        Assert.That(_sut.Read("f2", "d1").Text, Is.EqualTo("x"));
    }
}

public class DataStoreCardTest
{
    private static readonly TemplateEngine T = new();
    private string _dir = null!;
    private DataService _data = null!;
    private DataStoreCard _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tammoa-datacard-" + Guid.NewGuid().ToString("N"));
        _data = new DataService(new DataRepository(_dir), Substitute.For<IFlowRepository>(), Substitute.For<IRunEventBus>());
        _sut = new DataStoreCard(T, _data);
    }

    [TearDown]
    public void TearDown() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static CardExecutionContext Ctx(object? settings = null, JToken? input = null, string name = "Notes", string? flow = "f1", string? node = "c1", JToken? run = null) =>
        new(settings is null ? new JObject() : JObject.FromObject(settings), input, run, null, null, flow, node, name);

    private void Seed(string text, string scope = "flow", string name = "Notes") => _data.WriteAt(new DataLocation(scope, "f1", "c1", name), text);

    [Test]
    public void ItIsASource_ThatNeedsNoTriggerAndNoInput()
    {
        Assert.That(_sut.Info.IsSource, Is.True);
        Assert.That(_sut.Info.IsTrigger, Is.False);
        Assert.That(_sut.Info.Category, Is.EqualTo(CardCategory.Data));
        Assert.That(_sut.Info.Params.Single(p => p.Key == "value").Kind, Is.EqualTo(ParamKind.Stored));
    }

    [Test]
    public async Task WithNothingConnected_ItOutputsTheStoredText()
    {
        Seed("Be brief.");
        var r = (await _sut.ExecuteAsync(Ctx(), default)).Output;
        Assert.That((string?)r["text"], Is.EqualTo("Be brief."));
        Assert.That((string?)r["name"], Is.EqualTo("Notes"));
        Assert.That((string?)r["scope"], Is.EqualTo("flow"));
        Assert.That((int)r["size"]!, Is.EqualTo(9));
        Assert.That((bool)r["changed"]!, Is.False);
        Assert.That(r["json"], Is.Null, "plain text has no json field");
        Assert.That(r["updatedAt"], Is.Not.Null);
    }

    [Test]
    public async Task NothingStoredYet_IsAnEmptyText()
    {
        var r = (await _sut.ExecuteAsync(Ctx(), default)).Output;
        Assert.That((string?)r["text"], Is.EqualTo(""));
        Assert.That(r["updatedAt"], Is.Null);
    }

    [Test]
    public async Task JsonText_AlsoComesOutAsData()
    {
        Seed("{\"city\":\"Phnom Penh\",\"tags\":[\"a\"]}");
        var r = (await _sut.ExecuteAsync(Ctx(), default)).Output;
        Assert.That((string?)r["json"]!["city"], Is.EqualTo("Phnom Penh"));
        Assert.That((string?)r["text"], Does.StartWith("{"), "and the raw text is still there");
    }

    [Test]
    public async Task KeepIsTheDefault_AConnectedCardNeverOverwritesByAccident()
    {
        Seed("original");
        var r = (await _sut.ExecuteAsync(Ctx(input: JObject.Parse("{\"text\":\"incoming\"}")), default)).Output;
        Assert.That((string?)r["text"], Is.EqualTo("original"));
        Assert.That((bool)r["changed"]!, Is.False);
        var explicitKeep = (await _sut.ExecuteAsync(Ctx(new { mode = "keep" }, JObject.Parse("{\"text\":\"incoming\"}")), default)).Output;
        Assert.That((string?)explicitKeep["text"], Is.EqualTo("original"));
    }

    [Test]
    public async Task Replace_SavesTheTemplate_AndOutputsTheNewText()
    {
        Seed("old");
        var r = (await _sut.ExecuteAsync(Ctx(new { mode = "replace", writeFrom = "Answer: {{input.text}}" }, JObject.Parse("{\"text\":\"42\"}")), default)).Output;
        Assert.That((string?)r["text"], Is.EqualTo("Answer: 42"));
        Assert.That((bool)r["changed"]!, Is.True);
        Assert.That(_data.ReadAt(new DataLocation("flow", "f1", "c1", "Notes")).Text, Is.EqualTo("Answer: 42"), "it is really stored, not just returned");
    }

    [Test]
    public async Task Replace_DefaultTemplateIsTheWholeInput_AnObjectIsSavedAsJson()
    {
        var r = (await _sut.ExecuteAsync(Ctx(new { mode = "replace" }, JObject.Parse("{\"a\":1,\"b\":[2]}")), default)).Output;
        Assert.That((string?)r["text"], Is.EqualTo("{\"a\":1,\"b\":[2]}"));
        Assert.That((int)r["json"]!["a"]!, Is.EqualTo(1), "so the saved object is readable as data afterwards");
    }

    [Test]
    public async Task Append_AddsANewLine_AndNoBlankLineWhenEmpty()
    {
        var settings = new { mode = "append", writeFrom = "{{run.input}}" };
        await _sut.ExecuteAsync(Ctx(settings, new JObject(), run: "first"), default);
        var r = (await _sut.ExecuteAsync(Ctx(settings, new JObject(), run: "second"), default)).Output;
        Assert.That((string?)r["text"], Is.EqualTo("first\nsecond"));
    }

    [Test]
    public async Task WriteModesWithNothingConnected_ChangeNothing()
    {
        Seed("kept");
        var r = (await _sut.ExecuteAsync(Ctx(new { mode = "replace" }), default)).Output;
        Assert.That((string?)r["text"], Is.EqualTo("kept"));
        Assert.That((bool)r["changed"]!, Is.False);
    }

    [Test]
    public async Task SharedScope_UsesTheNameNotTheCard()
    {
        _data.WriteAt(new DataLocation("shared", "other-flow", "other-card", "notes"), "from another flow");
        var r = (await _sut.ExecuteAsync(Ctx(new { scope = "shared" }, name: "Notes"), default)).Output;
        Assert.That((string?)r["text"], Is.EqualTo("from another flow"));
        Assert.That((string?)r["scope"], Is.EqualTo("shared"));
    }

    [Test]
    public void Replace_TooBigForTheLimit_IsRefused()
    {
        var input = new JObject { ["text"] = new string('x', IDataService.MaxLength + 10) };
        Assert.Throws<ArgumentException>(() => _sut.ExecuteAsync(Ctx(new { mode = "replace", writeFrom = "{{input.text}}" }, input), default).GetAwaiter().GetResult());
    }

    [TestCase(null, "c1", "Notes")]          // not inside a flow run
    [TestCase("f1", null, "Notes")]
    [TestCase("f1", "c1", "")]                // no name to refer to it by
    [TestCase("f1", "c1", "a.b")]
    public void BadSituations_AreConfigurationErrors(string? flow, string? node, string name) =>
        Assert.ThrowsAsync<ArgumentException>(() => _sut.ExecuteAsync(Ctx(name: name, flow: flow, node: node), default));

    [TestCase("{\"scope\":\"universe\"}")]
    [TestCase("{\"mode\":\"destroy\"}")]
    public void UnknownScopeOrMode_IsRefused(string settings) =>
        Assert.ThrowsAsync<ArgumentException>(() => _sut.ExecuteAsync(new CardExecutionContext(JObject.Parse(settings), null, null, null, null, "f1", "c1", "Notes"), default));
}

public class TemplateContextDataTest
{
    [Test]
    public void TheDataCardsAreAvailableToEveryTemplate_AsDataName()
    {
        var ctx = new CardExecutionContext(new JObject(), null, null, JObject.Parse("{\"Style\":\"Be brief.\",\"Settings\":{\"city\":\"Phnom Penh\"}}")).TemplateContext;
        var t = new TemplateEngine();
        Assert.That(t.Render("{{data.Style}} in {{data.Settings.city}}", ctx), Is.EqualTo("Be brief. in Phnom Penh"));
        Assert.That(t.Render("{{data.Missing}}|", ctx), Is.EqualTo("|"));
        Assert.That(t.Render("{{data.Settings | json}}", ctx), Is.EqualTo("{\"city\":\"Phnom Penh\"}"));
        Assert.That(t.Resolve(new JValue("{{data.Settings}}"), ctx).Type, Is.EqualTo(JTokenType.Object), "a lone reference keeps its type");
    }

    [Test]
    public void WithoutAnyDataCards_DataIsAnEmptyObject()
    {
        var ctx = new CardExecutionContext(new JObject(), null, null).TemplateContext;
        Assert.That(ctx["data"], Is.InstanceOf<JObject>());
        Assert.That(new TemplateEngine().Render("[{{data.X}}]", ctx), Is.EqualTo("[]"));
    }

    [Test]
    public void ANameWithSpaces_IsReferencedAsIs() =>
        Assert.That(new TemplateEngine().Render("{{data.Last answer}}", new CardExecutionContext(new JObject(), null, null, JObject.Parse("{\"Last answer\":\"42\"}")).TemplateContext), Is.EqualTo("42"));
}
