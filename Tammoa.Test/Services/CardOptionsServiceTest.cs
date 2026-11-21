using NSubstitute;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Services;

namespace Tammoa.Test.Services;

public class CardOptionsServiceTest
{
    private IConnectionService _connections = null!;
    private IOllamaClient _ollama = null!;
    private CardOptionsService _sut = null!;
    private static readonly Connection C = new("c1", "Home", ConnectionProvider.Ollama, "http://x", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [SetUp]
    public void SetUp()
    {
        _connections = Substitute.For<IConnectionService>();
        _ollama = Substitute.For<IOllamaClient>();
        _sut = new CardOptionsService(_connections, _ollama);
    }

    [Test]
    public async Task OllamaModels_ListsTheModelsOfTheConnection()
    {
        _connections.GetForUse("c1").Returns(C);
        _ollama.ListModelsAsync(C, default).Returns(new[] { "a:1", "b:2" });
        var options = await _sut.GetAsync("ollama.models", new Dictionary<string, string> { ["connectionId"] = "c1" }, default);
        Assert.That(options.Select(o => o.Value), Is.EqualTo(new[] { "a:1", "b:2" }));
        Assert.That(options.Select(o => o.Label), Is.EqualTo(new[] { "a:1", "b:2" }));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void OllamaModels_WithoutAConnection_ThrowsArgumentException(string? id)
    {
        var query = id is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["connectionId"] = id };
        Assert.ThrowsAsync<ArgumentException>(() => _sut.GetAsync("ollama.models", query, default));
    }

    [Test]
    public void UnknownConnection_ThrowsKeyNotFound()
    {
        _connections.GetForUse("zz").Returns(_ => throw new KeyNotFoundException());
        Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.GetAsync("ollama.models", new Dictionary<string, string> { ["connectionId"] = "zz" }, default));
    }

    [Test]
    public void UnknownSource_ThrowsKeyNotFound() =>
        Assert.ThrowsAsync<KeyNotFoundException>(() => _sut.GetAsync("nope", new Dictionary<string, string>(), default));

    [Test]
    public void UnreachableServer_PassesTheReadableMessage()
    {
        _connections.GetForUse("c1").Returns(C);
        _ollama.ListModelsAsync(C, default).Returns<Task<IReadOnlyList<string>>>(_ => throw new InvalidOperationException("Could not reach Ollama at http://x"));
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => _sut.GetAsync("ollama.models", new Dictionary<string, string> { ["connectionId"] = "c1" }, default));
        Assert.That(ex!.Message, Does.Contain("Could not reach"));
    }
}
