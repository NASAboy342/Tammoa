using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using Tammoa.Models;
using Tammoa.Repositories;

namespace Tammoa.Test.Repositories;

public class FlowRepositoryTest
{
    private string _dir = null!;
    private string _db = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tammoa-test-" + Guid.NewGuid().ToString("N"));
        _db = Path.Combine(_dir, "nested", "t.db"); // missing directory must be created
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static Flow Make(string id, string name = "n", DateTimeOffset? updated = null) =>
        new(id, name, "d", true, JObject.Parse("{\"nodes\":[{\"id\":\"a\"}],\"edges\":[]}"),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), updated ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    [Test]
    public void Insert_ThenGetFromFreshInstance_RoundTrips()
    {
        new FlowRepository(_db).Insert(Make("1", "It's \"quoted\"; DROP TABLE flow;--"));
        var back = new FlowRepository(_db).Get("1")!;
        Assert.That(back.Name, Is.EqualTo("It's \"quoted\"; DROP TABLE flow;--"));
        Assert.That(back.Enabled, Is.True);
        Assert.That(back.Definition["nodes"]![0]!["id"]!.Value<string>(), Is.EqualTo("a"));
        Assert.That(back.CreatedAt, Is.EqualTo(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Test]
    public void Constructor_RunTwice_IsIdempotent()
    {
        new FlowRepository(_db).Insert(Make("1"));
        Assert.That(new FlowRepository(_db).List(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Get_Missing_ReturnsNull() => Assert.That(new FlowRepository(_db).Get("x"), Is.Null);

    [Test]
    public void List_OrdersByUpdatedDescending()
    {
        var r = new FlowRepository(_db);
        r.Insert(Make("old", updated: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        r.Insert(Make("new", updated: new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.That(r.List().Select(f => f.Id), Is.EqualTo(new[] { "new", "old" }));
    }

    [Test]
    public void Update_Existing_ChangesRowAndReturnsTrue()
    {
        var r = new FlowRepository(_db);
        r.Insert(Make("1"));
        Assert.That(r.Update(Make("1", "renamed")), Is.True);
        Assert.That(r.Get("1")!.Name, Is.EqualTo("renamed"));
    }

    [Test]
    public void Update_Missing_ReturnsFalse() => Assert.That(new FlowRepository(_db).Update(Make("x")), Is.False);

    [Test]
    public void Delete_RemovesOnlyThatRow()
    {
        var r = new FlowRepository(_db);
        r.Insert(Make("1"));
        r.Insert(Make("2"));
        Assert.That(r.Delete("1"), Is.True);
        Assert.That(r.Delete("1"), Is.False);
        Assert.That(r.List().Select(f => f.Id), Is.EqualTo(new[] { "2" }));
    }
}
