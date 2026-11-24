using Tammoa.Models;

namespace Tammoa.Repositories;

public interface ISettingsRepository
{
    /// <summary>null when nothing was saved yet (or the file is damaged).</summary>
    AppSettings? Load();
    void Save(AppSettings settings);
}
