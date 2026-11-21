using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Repositories;

public class DataRepository : IDataRepository
{
    private readonly string _root;
    private readonly object _lock = new();

    /// <param name="dataDir">The app data folder; files go under &lt;dataDir&gt;/data/.</param>
    public DataRepository(string dataDir) => _root = Path.Combine(dataDir, "data");

    public string PathFor(DataLocation l) => l.Scope == DataScope.Shared
        ? Path.Combine(_root, "shared", DataNames.FileName(l.Name))
        : Path.Combine(_root, "flows", Safe(l.FlowId), Safe(l.CardId) + ".txt");

    public string? Read(DataLocation l)
    {
        var path = PathFor(l);
        lock (_lock) return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public DateTimeOffset? Modified(DataLocation l)
    {
        var path = PathFor(l);
        return File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero) : null;
    }

    public void Write(DataLocation l, string text)
    {
        var path = PathFor(l);
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temp, path, overwrite: true);
        }
    }

    public void DeleteFlow(string flowId)
    {
        var dir = Path.Combine(_root, "flows", Safe(flowId));
        lock (_lock) if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    public void CopyFlow(string fromFlowId, string toFlowId)
    {
        var from = Path.Combine(_root, "flows", Safe(fromFlowId));
        var to = Path.Combine(_root, "flows", Safe(toFlowId));
        lock (_lock)
        {
            if (!Directory.Exists(from)) return;
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from, "*.txt")) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        }
    }

    /// <summary>Ids come from our own code, but a path must never be built from something that could climb out of the folder.</summary>
    private static string Safe(string id) => string.Concat(id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
}
