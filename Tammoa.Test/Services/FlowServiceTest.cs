using NSubstitute;
using Newtonsoft.Json.Linq;
using Tammoa.Models;
using Tammoa.Repositories;
using Tammoa.Services;

namespace Tammoa.Test.Services;

public class FlowServiceTest
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private IFlowRepository _repo = null!;
    private FixedTime _time = null!;
    private IFlowDefinitionValidator _validator = null!;
    private IDataService _data = null!;
    private FlowService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repo = Substitute.For<IFlowRepository>();
        _repo.Update(Arg.Any<Flow>()).Returns(true);
        _time = new FixedTime(T0);
        _validator = Substitute.For<IFlowDefinitionValidator>();
        _data = Substitute.For<IDataService>();
        _data.ExportFlowData(default!, default!).ReturnsForAnyArgs(new JObject());
        _sut = new FlowService(_repo, _time, _validator, _data);
    }

    private static Flow Existing(string name = "Daily report", bool enabled = false, JObject? def = null) =>
        new("abc", name, "desc", enabled, def ?? JObject.Parse("{\"nodes\":[{\"id\":\"n1\"},{\"id\":\"n2\"}],\"edges\":[]}"), T0, T0);

    // --- validate (dry check for an agent: nothing saved, the reason comes back instead of an exception)
    [Test]
    public void Validate_AValidDefinition_IsValid_AndNothingIsSaved()
    {
        var result = _sut.Validate(JObject.Parse("{\"nodes\":[],\"edges\":[]}"));
        Assert.That(result, Is.EqualTo(new FlowValidation(true, null)));
        _repo.DidNotReceiveWithAnyArgs().Insert(default!);
        _repo.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [Test]
    public void Validate_AProblem_ComesBackAsTheReason_NotAnException()
    {
        _validator.When(v => v.Validate(Arg.Any<JObject>())).Do(_ => throw new ArgumentException("Unknown card type 'x'."));
        Assert.That(_sut.Validate(new JObject()), Is.EqualTo(new FlowValidation(false, "Unknown card type 'x'.")));
        Assert.That(_sut.Validate(null), Is.EqualTo(new FlowValidation(false, "A definition is required: { \"nodes\": [], \"edges\": [] }.")));
    }

    // --- create
    [Test]
    public void Create_ValidRequest_InsertsDisabledFlowWithEmptyDefinition()
    {
        var flow = _sut.Create(new CreateFlowRequest("  My flow ", " about "));

        Assert.That(flow.Name, Is.EqualTo("My flow"));
        Assert.That(flow.Description, Is.EqualTo("about"));
        Assert.That(flow.Enabled, Is.False);
        Assert.That(flow.Id, Is.Not.Empty);
        Assert.That(flow.Definition["schemaVersion"]!.Value<int>(), Is.EqualTo(FlowService.DefinitionSchemaVersion));
        Assert.That(((JArray)flow.Definition["nodes"]!).Count, Is.EqualTo(0));
        _repo.Received(1).Insert(Arg.Is<Flow>(f => f.Id == flow.Id));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_BlankName_ThrowsArgumentException(string? name)
    {
        Assert.Throws<ArgumentException>(() => _sut.Create(new CreateFlowRequest(name, null)));
        _repo.DidNotReceive().Insert(Arg.Any<Flow>());
    }

    [Test]
    public void Create_NameTooLong_ThrowsArgumentException() =>
        Assert.Throws<ArgumentException>(() => _sut.Create(new CreateFlowRequest(new string('a', FlowService.MaxNameLength + 1), null)));

    [Test]
    public void Create_DescriptionTooLong_ThrowsArgumentException() =>
        Assert.Throws<ArgumentException>(() => _sut.Create(new CreateFlowRequest("x", new string('a', FlowService.MaxDescriptionLength + 1))));

    // --- list / get
    [Test]
    public void List_ReturnsSummariesWithCardCount()
    {
        _repo.List().Returns(new[] { Existing() });
        var list = _sut.List();
        Assert.That(list, Has.Count.EqualTo(1));
        Assert.That(list[0].CardCount, Is.EqualTo(2));
    }

    [Test]
    public void List_DefinitionWithoutNodes_CountsZero()
    {
        _repo.List().Returns(new[] { Existing(def: new JObject()) });
        Assert.That(_sut.List()[0].CardCount, Is.EqualTo(0));
    }

    [Test]
    public void Get_Missing_ThrowsKeyNotFound()
    {
        _repo.Get("nope").Returns((Flow?)null);
        Assert.Throws<KeyNotFoundException>(() => _sut.Get("nope"));
    }

    // --- update
    [Test]
    public void Update_ValidRequest_KeepsIdEnabledAndCreatedAndBumpsUpdated()
    {
        _repo.Get("abc").Returns(Existing(enabled: true));
        _time.Now = T0.AddHours(1);

        var updated = _sut.Update("abc", new UpdateFlowRequest("Renamed", "new", null));

        Assert.That(updated.Name, Is.EqualTo("Renamed"));
        Assert.That(updated.Enabled, Is.True);
        Assert.That(updated.CreatedAt, Is.EqualTo(T0));
        Assert.That(updated.UpdatedAt, Is.EqualTo(T0.AddHours(1)));
        Assert.That(((JArray)updated.Definition["nodes"]!).Count, Is.EqualTo(2), "definition kept when not sent");
        _repo.Received(1).Update(Arg.Is<Flow>(f => f.Name == "Renamed"));
    }

    [Test]
    public void Update_ReplacesDefinitionWhenSent()
    {
        _repo.Get("abc").Returns(Existing());
        var updated = _sut.Update("abc", new UpdateFlowRequest("x", "", JObject.Parse("{\"nodes\":[],\"edges\":[]}")));
        Assert.That(((JArray)updated.Definition["nodes"]!).Count, Is.EqualTo(0));
    }

    [Test]
    public void Update_BlankName_ThrowsArgumentException()
    {
        _repo.Get("abc").Returns(Existing());
        Assert.Throws<ArgumentException>(() => _sut.Update("abc", new UpdateFlowRequest(" ", "", null)));
        _repo.DidNotReceive().Update(Arg.Any<Flow>());
    }

    [Test]
    public void Update_ValidatorRejectsDefinition_ThrowsArgumentExceptionAndDoesNotSave()
    {
        _repo.Get("abc").Returns(Existing());
        _validator.When(v => v.Validate(Arg.Any<JObject>())).Do(_ => throw new ArgumentException("bad graph"));
        Assert.Throws<ArgumentException>(() => _sut.Update("abc", new UpdateFlowRequest("x", "", JObject.Parse("{\"nodes\":5}"))));
        _repo.DidNotReceive().Update(Arg.Any<Flow>());
    }

    [Test]
    public void Import_ValidatorRejectsDefinition_ThrowsArgumentException()
    {
        _validator.When(v => v.Validate(Arg.Any<JObject>())).Do(_ => throw new ArgumentException("bad graph"));
        Assert.Throws<ArgumentException>(() => _sut.Import(new FlowDocument("tammoa.flow", 1, "x", "", JObject.Parse("{}"))));
        _repo.DidNotReceive().Insert(Arg.Any<Flow>());
    }

    [Test]
    public void Update_Missing_ThrowsKeyNotFound()
    {
        _repo.Get("nope").Returns((Flow?)null);
        Assert.Throws<KeyNotFoundException>(() => _sut.Update("nope", new UpdateFlowRequest("x", "", null)));
    }

    [Test]
    public void Update_RowVanishedBetweenReadAndWrite_ThrowsKeyNotFound()
    {
        _repo.Get("abc").Returns(Existing());
        _repo.Update(Arg.Any<Flow>()).Returns(false);
        Assert.Throws<KeyNotFoundException>(() => _sut.Update("abc", new UpdateFlowRequest("x", "", null)));
    }

    // --- delete (strict confirmation)
    [Test]
    public void Delete_ExactName_Deletes()
    {
        _repo.Get("abc").Returns(Existing("Daily report"));
        _sut.Delete("abc", "Daily report");
        _repo.Received(1).Delete("abc");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("daily report")]      // case differs
    [TestCase("Daily report ")]     // trailing space
    [TestCase("Daily")]
    public void Delete_NotExactName_ThrowsArgumentExceptionAndKeepsFlow(string? confirm)
    {
        _repo.Get("abc").Returns(Existing("Daily report"));
        Assert.Throws<ArgumentException>(() => _sut.Delete("abc", confirm));
        _repo.DidNotReceive().Delete(Arg.Any<string>());
    }

    [Test]
    public void Delete_Missing_ThrowsKeyNotFound()
    {
        _repo.Get("nope").Returns((Flow?)null);
        Assert.Throws<KeyNotFoundException>(() => _sut.Delete("nope", "x"));
    }

    // --- enable / disable
    [Test]
    public void SetEnabled_Change_PersistsAndBumpsUpdated()
    {
        _repo.Get("abc").Returns(Existing(enabled: false));
        _time.Now = T0.AddMinutes(5);
        var f = _sut.SetEnabled("abc", true);
        Assert.That(f.Enabled, Is.True);
        Assert.That(f.UpdatedAt, Is.EqualTo(T0.AddMinutes(5)));
        _repo.Received(1).Update(Arg.Is<Flow>(x => x.Enabled));
    }

    [Test]
    public void SetEnabled_AlreadyInState_DoesNotWrite()
    {
        _repo.Get("abc").Returns(Existing(enabled: true));
        _sut.SetEnabled("abc", true);
        _repo.DidNotReceive().Update(Arg.Any<Flow>());
    }

    [Test]
    public void SetEnabled_Missing_ThrowsKeyNotFound()
    {
        _repo.Get("nope").Returns((Flow?)null);
        Assert.Throws<KeyNotFoundException>(() => _sut.SetEnabled("nope", true));
    }

    // --- duplicate
    [Test]
    public void Duplicate_CopiesDefinitionAsDisabledWithNewId()
    {
        var src = Existing("Report", enabled: true);
        _repo.Get("abc").Returns(src);
        var copy = _sut.Duplicate("abc");
        Assert.That(copy.Id, Is.Not.EqualTo("abc"));
        Assert.That(copy.Name, Is.EqualTo("Report (copy)"));
        Assert.That(copy.Enabled, Is.False);
        Assert.That(JToken.DeepEquals(copy.Definition, src.Definition), Is.True);
        Assert.That(ReferenceEquals(copy.Definition, src.Definition), Is.False);
        _repo.Received(1).Insert(Arg.Is<Flow>(f => f.Id == copy.Id));
    }

    [Test]
    public void Duplicate_LongName_StaysWithinLimit()
    {
        _repo.Get("abc").Returns(Existing(new string('a', FlowService.MaxNameLength)));
        Assert.That(_sut.Duplicate("abc").Name, Has.Length.EqualTo(FlowService.MaxNameLength));
    }

    // --- the flow's data follows the flow
    [Test]
    public void Delete_RemovesTheFlowsStoredData()
    {
        _repo.Get("abc").Returns(Existing("Daily report"));
        _sut.Delete("abc", "Daily report");
        _data.Received(1).DeleteFlowData("abc");
    }

    [Test]
    public void WrongConfirmation_KeepsTheStoredData()
    {
        _repo.Get("abc").Returns(Existing("Daily report"));
        Assert.Throws<ArgumentException>(() => _sut.Delete("abc", "nope"));
        _data.DidNotReceiveWithAnyArgs().DeleteFlowData(default!);
    }

    [Test]
    public void Duplicate_CopiesTheStoredData_ToTheNewFlow()
    {
        _repo.Get("abc").Returns(Existing());
        var copy = _sut.Duplicate("abc");
        _data.Received(1).CopyFlowData("abc", copy.Id);
    }

    [Test]
    public void Export_IncludesTheFlowsOwnData_WhenThereIsSome_AndOmitsTheFieldOtherwise()
    {
        _repo.Get("abc").Returns(Existing());
        _data.ExportFlowData("abc", default!).ReturnsForAnyArgs(JObject.Parse("{\"d1\":\"stored text\"}"));
        var withData = _sut.Export("abc");
        Assert.That((string?)withData.Data!["d1"], Is.EqualTo("stored text"));
        _data.ExportFlowData("abc", default!).ReturnsForAnyArgs(new JObject());
        var without = _sut.Export("abc");
        Assert.That(without.Data, Is.Null);
        Assert.That(Tammoa.Helper.JsonHelper.Serialize(without), Does.Not.Contain("\"data\""));
    }

    [Test]
    public void Import_HandsTheDocumentsDataToTheNewFlow()
    {
        var data = JObject.Parse("{\"d1\":\"x\"}");
        var imported = _sut.Import(new FlowDocument("tammoa.flow", 1, "Imported", "", JObject.Parse("{\"nodes\":[],\"edges\":[]}"), data));
        _data.Received(1).ImportFlowData(imported.Id, Arg.Any<JObject>(), data);
    }

    [Test]
    public void ARejectedImport_StoresNoData()
    {
        _validator.When(v => v.Validate(Arg.Any<JObject>())).Do(_ => throw new ArgumentException("bad"));
        Assert.Throws<ArgumentException>(() => _sut.Import(new FlowDocument("tammoa.flow", 1, "x", "", JObject.Parse("{}"), JObject.Parse("{\"d\":\"x\"}"))));
        _data.DidNotReceiveWithAnyArgs().ImportFlowData(default!, default!, default);
    }

    // --- export / import
    [Test]
    public void Export_ReturnsPortableDocumentWithoutMachineFields()
    {
        _repo.Get("abc").Returns(Existing("Report", enabled: true));
        var doc = _sut.Export("abc");
        Assert.That(doc.Format, Is.EqualTo("tammoa.flow"));
        Assert.That(doc.Version, Is.EqualTo(1));
        Assert.That(doc.Name, Is.EqualTo("Report"));
        var json = Tammoa.Helper.JsonHelper.Serialize(doc);
        Assert.That(json, Does.Not.Contain("\"id\":\"abc\""));
        Assert.That(json, Does.Not.Contain("enabled"));
        Assert.That(json, Does.Not.Contain("createdAt"));
    }

    [Test]
    public void ExportThenImport_RoundTripsContentAsNewDisabledFlow()
    {
        _repo.Get("abc").Returns(Existing("Report", enabled: true));
        var doc = _sut.Export("abc");
        // through real JSON, like a file on disk
        var fromFile = Tammoa.Helper.JsonHelper.Deserialize<FlowDocument>(Tammoa.Helper.JsonHelper.Serialize(doc))!;

        var imported = _sut.Import(fromFile);

        Assert.That(imported.Id, Is.Not.EqualTo("abc"));
        Assert.That(imported.Enabled, Is.False);
        Assert.That(imported.Name, Is.EqualTo("Report"));
        Assert.That(JToken.DeepEquals(imported.Definition, Existing().Definition), Is.True);
        _repo.Received(1).Insert(Arg.Is<Flow>(f => f.Id == imported.Id));
    }

    [Test]
    public void Import_WrongFormat_ThrowsArgumentException() =>
        Assert.Throws<ArgumentException>(() => _sut.Import(new FlowDocument("something.else", 1, "x", "", null)));

    [TestCase(0)]
    [TestCase(2)]
    public void Import_UnsupportedVersion_ThrowsArgumentException(int version) =>
        Assert.Throws<ArgumentException>(() => _sut.Import(new FlowDocument("tammoa.flow", version, "x", "", null)));

    [Test]
    public void Import_BlankName_ThrowsArgumentException() =>
        Assert.Throws<ArgumentException>(() => _sut.Import(new FlowDocument("tammoa.flow", 1, " ", "", null)));

    [Test]
    public void Import_NoDefinition_UsesEmptyDefinition()
    {
        var f = _sut.Import(new FlowDocument("tammoa.flow", 1, "x", null, null));
        Assert.That(((JArray)f.Definition["nodes"]!).Count, Is.EqualTo(0));
    }
}
