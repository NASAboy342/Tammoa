using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Repositories;

/// <summary>settings.json in the data folder.</summary>
public class SettingsRepository : ISettingsRepository
{
    private readonly string _file;
    private readonly object _lock = new();

    public SettingsRepository(string file) => _file = file;

    public AppSettings? Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_file)) return null;
            try { return JsonHelper.Deserialize<AppSettings>(File.ReadAllText(_file)); }
            catch (Newtonsoft.Json.JsonException) { return null; }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var temp = _file + ".tmp";
            File.WriteAllText(temp, JsonHelper.Serialize(settings));
            File.Move(temp, _file, overwrite: true);
        }
    }
}
