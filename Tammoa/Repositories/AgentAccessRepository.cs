using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Repositories;

/// <summary>One small JSON file in the data folder (owner-only on macOS/Linux: it holds the token).</summary>
public class AgentAccessRepository : IAgentAccessRepository
{
    private readonly string _file;
    private readonly object _lock = new();

    public AgentAccessRepository(string file) => _file = file;

    public AgentSettings? Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_file)) return null;
            try { return JsonHelper.Deserialize<AgentSettings>(File.ReadAllText(_file)); }
            catch (Newtonsoft.Json.JsonException) { return null; } // a damaged file means "not set up": the service makes a new token and keeps access off
        }
    }

    public void Save(AgentSettings settings)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var temp = _file + ".tmp";
            File.WriteAllText(temp, JsonHelper.Serialize(settings));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temp, _file, overwrite: true);
        }
    }
}
