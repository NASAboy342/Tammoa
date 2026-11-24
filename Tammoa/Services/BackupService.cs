using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Services;

public class BackupService : IBackupService
{
    public const string DocumentFormat = "tammoa.backup";
    public const int DocumentVersion = 1;
    public const long MaxFileBytes = 200L * 1024 * 1024;
    private static readonly Regex FileNamePattern = new(@"^[A-Za-z0-9][A-Za-z0-9._-]*\.json$", RegexOptions.Compiled);

    private readonly IFlowService _flows;
    private readonly IConnectionService _connections;
    private readonly IDataService _data;
    private readonly IAppInfoService _info;
    private readonly IFolderOpener _opener;
    private readonly TimeProvider _time;

    public BackupService(IFlowService flows, IConnectionService connections, IDataService data, IAppInfoService info, IFolderOpener opener, TimeProvider time, string folder)
    {
        _flows = flows;
        _connections = connections;
        _data = data;
        _info = info;
        _opener = opener;
        _time = time;
        Folder = folder;
    }

    public string Folder { get; }

    public BackupFileInfo Create()
    {
        var now = _time.GetUtcNow();
        var flows = _flows.List().Select(s =>
        {
            var f = _flows.Get(s.Id);
            var data = _data.ExportFlowData(f.Id, f.Definition);
            return new BackupFlow(f.Id, f.Name, f.Description, f.Definition, data.HasValues ? data : null);
        }).ToList();
        var connections = _connections.List().Select(c => new BackupConnection(c.Id, c.Name, c.Provider, c.BaseUrl, c.HasKey)).ToList();
        var shared = _data.ExportShared();
        var document = new BackupDocument(DocumentFormat, DocumentVersion, now, _info.GetVersion().Version, flows, connections, shared.HasValues ? shared : null);

        Directory.CreateDirectory(Folder);
        var name = $"tammoa-backup-{now.UtcDateTime:yyyyMMdd-HHmmss}.json";
        var path = Path.Combine(Folder, name);
        for (var n = 2; File.Exists(path); n++) // two backups in the same second
        {
            name = $"tammoa-backup-{now.UtcDateTime:yyyyMMdd-HHmmss}-{n}.json";
            path = Path.Combine(Folder, name);
        }
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonConvert.SerializeObject(document, Formatting.Indented, JsonHelper.Settings));
        File.Move(temp, path);
        return new BackupFileInfo(name, path, new FileInfo(path).Length, now, flows.Count, connections.Count);
    }

    public IReadOnlyList<BackupFileInfo> List()
    {
        if (!Directory.Exists(Folder)) return [];
        var result = new List<BackupFileInfo>();
        foreach (var file in Directory.GetFiles(Folder, "*.json"))
        {
            var info = new FileInfo(file);
            if (info.Length > MaxFileBytes || !FileNamePattern.IsMatch(info.Name)) continue;
            var doc = TryRead(file);
            if (doc is null) continue;
            result.Add(new BackupFileInfo(info.Name, file, info.Length, doc.CreatedAt, doc.Flows?.Count ?? 0, doc.Connections?.Count ?? 0));
        }
        return result.OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.FileName, StringComparer.Ordinal).ToList();
    }

    public RestoreResult Restore(BackupDocument document)
    {
        if (document.Format != DocumentFormat) throw new ArgumentException($"This is not a Tammoa backup file (expected format '{DocumentFormat}').");
        if (document.Version < 1 || document.Version > DocumentVersion)
            throw new ArgumentException($"This backup is from a newer Tammoa (file version {document.Version}); this app reads up to {DocumentVersion}. Update Tammoa first.");

        var problems = new List<string>();
        int connectionsAdded = 0, connectionsSkipped = 0, flowsAdded = 0, flowsSkipped = 0;
        var needKey = new List<string>();

        // connections first: the cards of the flows refer to them by id
        foreach (var c in document.Connections ?? [])
        {
            try
            {
                if (_connections.Restore(c)) { connectionsAdded++; if (c.HasKey) needKey.Add(c.Name); }
                else connectionsSkipped++;
            }
            catch (ArgumentException ex) { problems.Add($"Connection '{c.Name}': {ex.Message}"); }
        }
        foreach (var f in document.Flows ?? [])
        {
            try
            {
                if (_flows.Restore(f)) flowsAdded++;
                else flowsSkipped++;
            }
            catch (ArgumentException ex) { problems.Add($"Flow '{f.Name}': {ex.Message}"); }
        }
        var shared = _data.ImportShared(document.SharedData);
        return new RestoreResult(flowsAdded, flowsSkipped, connectionsAdded, connectionsSkipped, shared, needKey, problems);
    }

    public RestoreResult RestoreFile(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !FileNamePattern.IsMatch(fileName) || fileName.Contains(".."))
            throw new ArgumentException("That is not the name of a backup file.");
        var path = Path.Combine(Folder, fileName);
        if (!File.Exists(path)) throw new KeyNotFoundException($"There is no backup named '{fileName}' in the backups folder.");
        if (new FileInfo(path).Length > MaxFileBytes) throw new ArgumentException("That file is too large to be a backup.");
        return Restore(TryRead(path) ?? throw new ArgumentException($"'{fileName}' could not be read as a Tammoa backup."));
    }

    public void OpenFolder() => _opener.Open(Folder);

    private static BackupDocument? TryRead(string path)
    {
        try
        {
            var doc = JsonHelper.Deserialize<BackupDocument>(File.ReadAllText(path));
            return doc is { Format: DocumentFormat } ? doc : null;
        }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }
}
