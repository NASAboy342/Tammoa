using Newtonsoft.Json.Linq;
using NSubstitute;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Repositories;
using Tammoa.Services;

namespace Tammoa.Test.Services;

public class ConnectionServiceTest
{
    private IConnectionRepository _repo = null!;
    private IFlowRepository _flows = null!;
    private IOllamaClient _ollama = null!;
    private ConnectionService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repo = Substitute.For<IConnectionRepository>();
        _repo.Update(Arg.Any<Connection>()).Returns(true);
        _flows = Substitute.For<IFlowRepository>();
        _flows.List().Returns(Array.Empty<Flow>());
        _ollama = Substitute.For<IOllamaClient>();
        _sut = new ConnectionService(_repo, _flows, _ollama, TimeProvider.System);
    }

    private static Connection Existing(string? key = "secret") =>
        new("c1", "Home", ConnectionProvider.Ollama, "http://localhost:11434", key, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static Flow FlowUsing(string connectionId) =>
        new("f1", "Uses it", "", false, JObject.Parse($"{{\"nodes\":[{{\"id\":\"a\",\"type\":\"ai.ollama\",\"params\":{{\"connection\":\"{connectionId}\",\"model\":\"m\"}}}}],\"edges\":[]}}"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    // ---- create
    [Test]
    public void Create_TrimsAndNormalises_AndNeverReturnsTheKey()
    {
        var info = _sut.Create(new SaveConnectionRequest("  Home  ", ConnectionProvider.Ollama, " http://localhost:11434/ ", " k3y ", false));
        Assert.That(info.Name, Is.EqualTo("Home"));
        Assert.That(info.BaseUrl, Is.EqualTo("http://localhost:11434"));
        Assert.That(info.HasKey, Is.True);
        Assert.That(JObject.FromObject(info).ToString(), Does.Not.Contain("k3y"), "the key must not appear in what the UI receives");
        _repo.Received(1).Insert(Arg.Is<Connection>(c => c.ApiKey == "k3y" && c.Id == info.Id));
    }

    [Test]
    public void Create_WithoutKey_HasNoKey()
    {
        var info = _sut.Create(new SaveConnectionRequest("Home", ConnectionProvider.Ollama, "http://localhost:11434", "  ", false));
        Assert.That(info.HasKey, Is.False);
        _repo.Received(1).Insert(Arg.Is<Connection>(c => c.ApiKey == null));
    }

    [TestCase(null, "http://x")]
    [TestCase("  ", "http://x")]
    [TestCase("Home", null)]
    [TestCase("Home", "")]
    [TestCase("Home", "localhost:11434")]
    [TestCase("Home", "ftp://x")]
    [TestCase("Home", "not a url")]
    public void Create_BadInput_ThrowsArgumentException(string? name, string? url)
    {
        Assert.Throws<ArgumentException>(() => _sut.Create(new SaveConnectionRequest(name, ConnectionProvider.Ollama, url, null, false)));
        _repo.DidNotReceive().Insert(Arg.Any<Connection>());
    }

    [Test]
    public void Create_NoProvider_Throws() =>
        Assert.Throws<ArgumentException>(() => _sut.Create(new SaveConnectionRequest("Home", null, "http://x", null, false)));

    [Test]
    public void Create_LimitsLength()
    {
        Assert.Throws<ArgumentException>(() => _sut.Create(new SaveConnectionRequest(new string('a', ConnectionService.MaxNameLength + 1), ConnectionProvider.Ollama, "http://x", null, false)));
        Assert.Throws<ArgumentException>(() => _sut.Create(new SaveConnectionRequest("Home", ConnectionProvider.Ollama, "http://x", new string('k', ConnectionService.MaxKeyLength + 1), false)));
    }

    // ---- update: the key is kept unless asked
    [Test]
    public void Update_WithoutKey_KeepsTheSavedKey()
    {
        _repo.Get("c1").Returns(Existing("secret"));
        _sut.Update("c1", new SaveConnectionRequest("Renamed", null, "http://other:1", null, false));
        _repo.Received(1).Update(Arg.Is<Connection>(c => c.ApiKey == "secret" && c.Name == "Renamed" && c.BaseUrl == "http://other:1" && c.Provider == ConnectionProvider.Ollama));
    }

    [Test]
    public void Update_WithNewKey_ReplacesIt()
    {
        _repo.Get("c1").Returns(Existing("old"));
        _sut.Update("c1", new SaveConnectionRequest("Home", null, "http://x", "new", false));
        _repo.Received(1).Update(Arg.Is<Connection>(c => c.ApiKey == "new"));
    }

    [Test]
    public void Update_ClearKey_RemovesIt_EvenIfANewKeyIsAlsoSent()
    {
        _repo.Get("c1").Returns(Existing("old"));
        var info = _sut.Update("c1", new SaveConnectionRequest("Home", null, "http://x", null, true));
        Assert.That(info.HasKey, Is.False);
        _repo.Received(1).Update(Arg.Is<Connection>(c => c.ApiKey == null));
    }

    [Test]
    public void Update_Missing_ThrowsKeyNotFound()
    {
        _repo.Get("zz").Returns((Connection?)null);
        Assert.Throws<KeyNotFoundException>(() => _sut.Update("zz", new SaveConnectionRequest("a", null, "http://x", null, false)));
    }

    [Test]
    public void Update_Invalid_DoesNotSave()
    {
        _repo.Get("c1").Returns(Existing());
        Assert.Throws<ArgumentException>(() => _sut.Update("c1", new SaveConnectionRequest("", null, "http://x", null, false)));
        _repo.DidNotReceive().Update(Arg.Any<Connection>());
    }

    // ---- read
    [Test]
    public void ListAndGet_NeverCarryTheKey()
    {
        _repo.List().Returns(new[] { Existing("secret") });
        _repo.Get("c1").Returns(Existing("secret"));
        Assert.That(JArray.FromObject(_sut.List()).ToString(), Does.Not.Contain("secret"));
        Assert.That(JObject.FromObject(_sut.Get("c1")).ToString(), Does.Not.Contain("secret"));
        Assert.That(_sut.Get("c1").HasKey, Is.True);
    }

    [Test]
    public void GetForUse_ReturnsTheKey_ForCardsOnly()
    {
        _repo.Get("c1").Returns(Existing("secret"));
        Assert.That(_sut.GetForUse("c1").ApiKey, Is.EqualTo("secret"));
        _repo.Get("zz").Returns((Connection?)null);
        Assert.Throws<KeyNotFoundException>(() => _sut.GetForUse("zz"));
    }

    [Test]
    public void Providers_ListsOllamaWithItsDefaultAddress()
    {
        var p = _sut.Providers().Single();
        Assert.That(p.Provider, Is.EqualTo(ConnectionProvider.Ollama));
        Assert.That(p.DefaultBaseUrl, Is.EqualTo("http://localhost:11434"));
        Assert.That(p.KeyRequired, Is.False);
    }

    // ---- delete is refused while cards use it
    [Test]
    public void Delete_Unused_Deletes()
    {
        _repo.Get("c1").Returns(Existing());
        _sut.Delete("c1");
        _repo.Received(1).Delete("c1");
    }

    [Test]
    public void Delete_UsedByAFlow_ThrowsInvalidOperationNamingTheFlow()
    {
        _repo.Get("c1").Returns(Existing());
        _flows.List().Returns(new[] { FlowUsing("c1"), FlowUsing("other") with { Id = "f2", Name = "Unrelated" } });
        var ex = Assert.Throws<InvalidOperationException>(() => _sut.Delete("c1"));
        Assert.That(ex!.Message, Does.Contain("Uses it").And.Not.Contain("Unrelated"));
        _repo.DidNotReceive().Delete(Arg.Any<string>());
    }

    [Test]
    public void Delete_Missing_ThrowsKeyNotFound()
    {
        _repo.Get("zz").Returns((Connection?)null);
        Assert.Throws<KeyNotFoundException>(() => _sut.Delete("zz"));
    }

    // ---- test
    [Test]
    public async Task Test_Reachable_ReturnsVersionAndModels()
    {
        _repo.Get("c1").Returns(Existing());
        _ollama.GetVersionAsync(default!, default).ReturnsForAnyArgs("0.9.0");
        _ollama.ListModelsAsync(default!, default).ReturnsForAnyArgs(new[] { "a", "b" });
        var r = await _sut.TestAsync("c1", default);
        Assert.That(r, Is.EqualTo(new ConnectionTestResult(true, "0.9.0", new[] { "a", "b" }, null)).Using<ConnectionTestResult>((x, y) => x.Ok == y.Ok && x.Version == y.Version && x.Models.SequenceEqual(y.Models) && x.Error == y.Error));
    }

    [Test]
    public async Task Test_Unreachable_ReturnsTheReasonInsteadOfThrowing()
    {
        _repo.Get("c1").Returns(Existing());
        _ollama.GetVersionAsync(default!, default).ReturnsForAnyArgs<Task<string>>(_ => throw new InvalidOperationException("Could not reach Ollama at http://x"));
        var r = await _sut.TestAsync("c1", default);
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Error, Does.Contain("Could not reach"));
        Assert.That(r.Models, Is.Empty);
    }

    [Test]
    public void Test_Missing_ThrowsKeyNotFound()
    {
        _repo.Get("zz").Returns((Connection?)null);
        Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.TestAsync("zz", default));
    }
}
