using Newtonsoft.Json.Linq;
using NSubstitute;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Repositories;
using Tammoa.Services;

namespace Tammoa.Test.Services;

/// <summary>Real services on real temporary files: a backup must really bring everything back.</summary>
public class BackupServiceTest
{
    private string _dir = null!;
    private IFolderOpener _opener = null!;

    private sealed class Stack
    {
        public required FlowService Flows;
        public required ConnectionService Connections;
        public required DataService Data;
        public required BackupService Backup;
        public required IFlowRepository FlowRepo;
    }

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tammoa-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _opener = Substitute.For<IFolderOpener>();
    }

    [TearDown]
    public void TearDown() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(_dir, true); }

    /// <summary>A whole app on its own data folder (a second one stands for "another computer").</summary>
    private Stack Make(string name = "a")
    {
        var data = Path.Combine(_dir, name);
        Directory.CreateDirectory(data);
        var flowRepo = new FlowRepository(Path.Combine(data, "tammoa.db"));
        var connRepo = new ConnectionRepository(Path.Combine(data, "tammoa.db"));
        var dataService = new DataService(new DataRepository(data), flowRepo, Substitute.For<IRunEventBus>());
        var cards = new CardRegistry([new Tammoa.Services.Cards.ManualTriggerCard(), new Tammoa.Services.Cards.DataStoreCard(new TemplateEngine(), dataService)]);
        var flows = new FlowService(flowRepo, TimeProvider.System, new FlowDefinitionValidator(cards), dataService);
        var connections = new ConnectionService(connRepo, flowRepo, Substitute.For<IOllamaClient>(), TimeProvider.System);
        var info = Substitute.For<IAppInfoService>();
        info.GetVersion().Returns(new AppVersionInfo("Tammoa", "0.1.0"));
        var backup = new BackupService(flows, connections, dataService, info, _opener, TimeProvider.System, Path.Combine(data, "backups"));
        return new Stack { Flows = flows, Connections = connections, Data = dataService, Backup = backup, FlowRepo = flowRepo };
    }

    private static JObject DefWithDataCard() => JObject.Parse(
        """{"nodes":[{"id":"t","type":"trigger.manual","name":"Start","position":{"x":0,"y":0},"params":{}},{"id":"d","type":"data.store","name":"Style","position":{"x":0,"y":100},"params":{"scope":"flow"}}],"edges":[]}""");

    private static void Fill(Stack s, string keyOfConnection = "sekret-key-123")
    {
        var c = s.Connections.Create(new SaveConnectionRequest("Home Ollama", ConnectionProvider.Ollama, "http://localhost:11434", keyOfConnection));
        var f = s.Flows.Create(new CreateFlowRequest("Morning report", "daily"));
        s.Flows.Update(f.Id, new UpdateFlowRequest(f.Name, f.Description, DefWithDataCard()));
        s.Data.Write(f.Id, "d", "Be brief.");
        s.Flows.Create(new CreateFlowRequest("Empty one", ""));
        Assert.That(c.Id, Is.Not.Empty);
    }

    // ---- creating
    [Test]
    public void Create_WritesOneFile_WithFlows_Connections_AndTheTextOfDataCards()
    {
        var a = Make();
        Fill(a);
        var info = a.Backup.Create();

        Assert.That(File.Exists(info.Path), Is.True);
        Assert.That(Path.GetDirectoryName(info.Path), Is.EqualTo(a.Backup.Folder));
        Assert.That((info.Flows, info.Connections), Is.EqualTo((2, 1)));
        var doc = JObject.Parse(File.ReadAllText(info.Path));
        Assert.That((string?)doc["format"], Is.EqualTo("tammoa.backup"));
        Assert.That(doc["flows"]!.Select(f => (string?)f["name"]), Is.EquivalentTo(new[] { "Morning report", "Empty one" }));
        Assert.That(doc.ToString(), Does.Contain("Be brief."), "the text of the Data card travels with its flow");
    }

    [Test]
    public void Create_NeverPutsAKeyInTheFile_OnlyThatOneExists()
    {
        var a = Make();
        Fill(a, "sekret-key-123");
        var text = File.ReadAllText(a.Backup.Create().Path);
        Assert.That(text, Does.Not.Contain("sekret-key-123"));
        Assert.That(text.ToLowerInvariant(), Does.Not.Contain("apikey"));
        Assert.That(JObject.Parse(text)["connections"]![0]!["hasKey"]!.Value<bool>(), Is.True, "only the fact that one existed");
    }

    [Test]
    public void TwoBackupsInTheSameSecond_AreTwoFiles()
    {
        var a = Make();
        var first = a.Backup.Create();
        var second = a.Backup.Create();
        Assert.That(second.FileName, Is.Not.EqualTo(first.FileName));
        Assert.That(a.Backup.List(), Has.Count.EqualTo(2));
    }

    [Test]
    public void AnEmptyApp_CanBeBackedUp_AndRestored()
    {
        var a = Make();
        var r = a.Backup.RestoreFile(a.Backup.Create().FileName);
        Assert.That((r.FlowsAdded, r.ConnectionsAdded, r.Problems), Is.EqualTo((0, 0, Array.Empty<string>())));
    }

    // ---- the round trip (a new computer)
    [Test]
    public void Restore_OnAnotherComputer_BringsFlowsConnectionsAndDataBack_WithTheSameIds()
    {
        var a = Make("a");
        Fill(a);
        var file = a.Backup.Create();
        var original = a.Flows.List().Single(f => f.Name == "Morning report");
        var originalConnection = a.Connections.List().Single();

        var b = Make("b");
        var result = b.Backup.Restore(JObject.Parse(File.ReadAllText(file.Path)).ToObject<BackupDocument>(Newtonsoft.Json.JsonSerializer.Create(Helper_Settings()))!);

        Assert.That((result.FlowsAdded, result.FlowsSkipped, result.ConnectionsAdded, result.ConnectionsSkipped), Is.EqualTo((2, 0, 1, 0)));
        Assert.That(result.Problems, Is.Empty);
        Assert.That(b.Flows.Get(original.Id).Name, Is.EqualTo("Morning report"), "same id, so nothing that points at it breaks");
        Assert.That(b.Flows.Get(original.Id).Enabled, Is.False);
        Assert.That(b.Connections.List().Single().Id, Is.EqualTo(originalConnection.Id), "the cards that name a connection keep working");
        Assert.That(b.Connections.List().Single().HasKey, Is.False, "the key was never in the file");
        Assert.That(result.ConnectionsNeedingKey, Is.EqualTo(new[] { "Home Ollama" }));
        Assert.That(b.Data.Read(original.Id, "d").Text, Is.EqualTo("Be brief."));
    }

    private static Newtonsoft.Json.JsonSerializerSettings Helper_Settings() => Tammoa.Helper.JsonHelper.Settings;

    [Test]
    public void Restore_ByFileName_Twice_MakesNoCopies()
    {
        var a = Make("a");
        Fill(a);
        var file = a.Backup.Create();
        var b = Make("b");
        Directory.CreateDirectory(b.Backup.Folder);
        File.Copy(file.Path, Path.Combine(b.Backup.Folder, file.FileName));

        var first = b.Backup.RestoreFile(file.FileName);
        var second = b.Backup.RestoreFile(file.FileName);
        Assert.That((first.FlowsAdded, first.ConnectionsAdded), Is.EqualTo((2, 1)));
        Assert.That((second.FlowsAdded, second.FlowsSkipped, second.ConnectionsAdded, second.ConnectionsSkipped), Is.EqualTo((0, 2, 0, 1)));
        Assert.That(b.Flows.List(), Has.Count.EqualTo(2));
    }

    [Test]
    public void Restore_NeverOverwrites_WhatTheUserChangedSinceTheBackup()
    {
        var a = Make();
        Fill(a);
        var file = a.Backup.Create();
        var morning = a.Flows.List().Single(f => f.Name == "Morning report");
        a.Flows.Update(morning.Id, new UpdateFlowRequest("Renamed since", "x", null));
        a.Data.Write(morning.Id, "d", "Changed text");

        var r = a.Backup.RestoreFile(file.FileName);
        Assert.That(r.FlowsAdded, Is.EqualTo(0));
        Assert.That(a.Flows.Get(morning.Id).Name, Is.EqualTo("Renamed since"));
        Assert.That(a.Data.Read(morning.Id, "d").Text, Is.EqualTo("Changed text"));
    }

    [Test]
    public void Restore_ADeletedFlow_ComesBack()
    {
        var a = Make();
        Fill(a);
        var file = a.Backup.Create();
        var empty = a.Flows.List().Single(f => f.Name == "Empty one");
        a.Flows.Delete(empty.Id, "Empty one");
        Assert.That(a.Backup.RestoreFile(file.FileName).FlowsAdded, Is.EqualTo(1));
        Assert.That(a.Flows.Get(empty.Id).Name, Is.EqualTo("Empty one"));
    }

    [Test]
    public void SharedDataTexts_AreBackedUp_AndNeverOverwritten()
    {
        var a = Make("a");
        var f = a.Flows.Create(new CreateFlowRequest("Uses shared", ""));
        a.Flows.Update(f.Id, new UpdateFlowRequest(f.Name, f.Description, JObject.Parse(
            """{"nodes":[{"id":"t","type":"trigger.manual","name":"Start","position":{"x":0,"y":0},"params":{}},{"id":"d","type":"data.store","name":"Notes","position":{"x":0,"y":100},"params":{"scope":"shared"}}],"edges":[]}""")));
        a.Data.Write(f.Id, "d", "shared words");
        var file = a.Backup.Create();

        var b = Make("b");
        var document = Tammoa.Helper.JsonHelper.Deserialize<BackupDocument>(File.ReadAllText(file.Path))!;
        Assert.That(b.Backup.Restore(document).SharedTextsAdded, Is.EqualTo(1));
        var where = new DataLocation(DataScope.Shared, "", "", "notes");
        Assert.That(b.Data.ReadAt(where).Text, Is.EqualTo("shared words"));

        b.Data.WriteAt(where, "mine");
        Assert.That(b.Backup.Restore(document).SharedTextsAdded, Is.EqualTo(0));
        Assert.That(b.Data.ReadAt(where).Text, Is.EqualTo("mine"), "a text the user changed is never replaced");
    }

    // ---- bad input
    [Test]
    public void Restore_ANonBackupDocument_IsRefusedWithAReason()
    {
        var a = Make();
        var ex = Assert.Throws<ArgumentException>(() => a.Backup.Restore(new BackupDocument("something.else", 1, DateTimeOffset.UtcNow, null, [], [], null)))!;
        Assert.That(ex.Message, Does.Contain("not a Tammoa backup"));
    }

    [Test]
    public void Restore_ANewerFileVersion_SaysToUpdateFirst()
    {
        var ex = Assert.Throws<ArgumentException>(() => Make().Backup.Restore(new BackupDocument("tammoa.backup", 99, DateTimeOffset.UtcNow, null, [], [], null)))!;
        Assert.That(ex.Message, Does.Contain("newer").And.Contain("Update"));
    }

    [Test]
    public void Restore_OneBadFlow_DoesNotStopTheOthers_AndIsReported()
    {
        var a = Make();
        var good = new BackupFlow("good1", "Fine", "", JObject.Parse("""{"nodes":[],"edges":[]}"""), null);
        var bad = new BackupFlow("bad1", "Broken", "", JObject.Parse("""{"nodes":[{"id":"x","type":"no.such.card","name":"X","position":{"x":0,"y":0},"params":{}}],"edges":[]}"""), null);
        var badId = new BackupFlow("../../etc", "Evil id", "", JObject.Parse("""{"nodes":[],"edges":[]}"""), null);
        var r = a.Backup.Restore(new BackupDocument("tammoa.backup", 1, DateTimeOffset.UtcNow, null, [bad, badId, good], [new BackupConnection("c!", "Bad conn", ConnectionProvider.Ollama, "http://x")], null));

        Assert.That(r.FlowsAdded, Is.EqualTo(1));
        Assert.That(r.Problems, Has.Count.EqualTo(3));
        Assert.That(r.Problems.Any(p => p.Contains("Broken")), Is.True);
        Assert.That(r.Problems.Any(p => p.Contains("Evil id") && p.Contains("not a valid flow id")), Is.True);
        Assert.That(a.Flows.List().Select(f => f.Name), Is.EqualTo(new[] { "Fine" }));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("../x.json")]
    [TestCase("..\\x.json")]
    [TestCase("/etc/passwd")]
    [TestCase("sub/x.json")]
    [TestCase("x.txt")]
    public void RestoreFile_OnlyTakesAPlainBackupFileName(string? name)
        => Assert.Throws<ArgumentException>(() => Make().Backup.RestoreFile(name));

    [Test]
    public void RestoreFile_AMissingFile_Is404_AndAFileThatIsNotABackup_Is400()
    {
        var a = Make();
        Assert.Throws<KeyNotFoundException>(() => a.Backup.RestoreFile("nothing.json"));
        Directory.CreateDirectory(a.Backup.Folder);
        File.WriteAllText(Path.Combine(a.Backup.Folder, "random.json"), """{"hello":"world"}""");
        File.WriteAllText(Path.Combine(a.Backup.Folder, "broken.json"), "{ not json");
        Assert.That(Assert.Throws<ArgumentException>(() => a.Backup.RestoreFile("random.json"))!.Message, Does.Contain("could not be read"));
        Assert.Throws<ArgumentException>(() => a.Backup.RestoreFile("broken.json"));
    }

    [Test]
    public void List_ShowsOnlyRealBackups_NewestFirst()
    {
        var a = Make();
        var first = a.Backup.Create();
        File.WriteAllText(Path.Combine(a.Backup.Folder, "notes.json"), """{"hello":"world"}""");
        File.WriteAllText(Path.Combine(a.Backup.Folder, "readme.txt"), "x");
        var second = a.Backup.Create();
        Assert.That(a.Backup.List().Select(f => f.FileName), Is.EqualTo(new[] { second.FileName, first.FileName }));
    }

    [Test]
    public void OpenFolder_AsksTheFileManager_ForTheBackupsFolder()
    {
        var a = Make();
        a.Backup.OpenFolder();
        _opener.Received(1).Open(a.Backup.Folder);
    }
}
