using Microsoft.Data.Sqlite;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Repositories;

namespace Tammoa.Test.Repositories;

public class ConnectionRepositoryTest
{
    private string _dir = null!;
    private string _db = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tammoa-conn-" + Guid.NewGuid().ToString("N"));
        _db = Path.Combine(_dir, "t.db");
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static Connection Make(string id, string name = "n", string? key = "k") =>
        new(id, name, ConnectionProvider.Ollama, "http://localhost:11434", key, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

    [Test]
    public void RoundTrip_KeepsEverythingIncludingTheKey_AndANullKey()
    {
        var repo = new ConnectionRepository(_db);
        repo.Insert(Make("1", "It's \"quoted\"; DROP TABLE connection;--", "k'ey"));
        repo.Insert(Make("2", key: null));
        var fresh = new ConnectionRepository(_db);
        var one = fresh.Get("1")!;
        Assert.That(one.Name, Is.EqualTo("It's \"quoted\"; DROP TABLE connection;--"));
        Assert.That(one.ApiKey, Is.EqualTo("k'ey"));
        Assert.That(one.Provider, Is.EqualTo(ConnectionProvider.Ollama));
        Assert.That(one.UpdatedAt, Is.EqualTo(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)));
        Assert.That(fresh.Get("2")!.ApiKey, Is.Null);
    }

    [Test]
    public void List_IsSortedByNameIgnoringCase_AndConstructorIsIdempotent()
    {
        var repo = new ConnectionRepository(_db);
        repo.Insert(Make("1", "banana"));
        repo.Insert(Make("2", "Apple"));
        Assert.That(new ConnectionRepository(_db).List().Select(c => c.Name), Is.EqualTo(new[] { "Apple", "banana" }));
    }

    [Test]
    public void Update_ChangesRow_AndFalseWhenMissing()
    {
        var repo = new ConnectionRepository(_db);
        repo.Insert(Make("1"));
        Assert.That(repo.Update(Make("1", "renamed", null)), Is.True);
        Assert.That(repo.Get("1")!.Name, Is.EqualTo("renamed"));
        Assert.That(repo.Get("1")!.ApiKey, Is.Null);
        Assert.That(repo.Update(Make("x")), Is.False);
    }

    [Test]
    public void Delete_RemovesOnlyThatRow()
    {
        var repo = new ConnectionRepository(_db);
        repo.Insert(Make("1"));
        repo.Insert(Make("2"));
        Assert.That(repo.Delete("1"), Is.True);
        Assert.That(repo.Delete("1"), Is.False);
        Assert.That(repo.List().Select(c => c.Id), Is.EqualTo(new[] { "2" }));
    }

    [Test]
    public void Get_Missing_ReturnsNull() => Assert.That(new ConnectionRepository(_db).Get("x"), Is.Null);

    [Test]
    public void FlowAndConnectionTables_ShareOneDatabaseFile()
    {
        var flows = new FlowRepository(_db);
        var conns = new ConnectionRepository(_db);
        conns.Insert(Make("1"));
        Assert.That(flows.List(), Is.Empty);
        Assert.That(conns.List(), Has.Count.EqualTo(1));
    }
}
