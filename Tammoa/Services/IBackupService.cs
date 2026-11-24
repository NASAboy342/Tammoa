using Tammoa.Models;

namespace Tammoa.Services;

/// <summary>One file with everything the user made: flows (with their Data card texts), connections WITHOUT keys, shared Data texts.
/// Not in it: keys (the user types them again), run history, the agent token. Restoring never overwrites.</summary>
public interface IBackupService
{
    /// <summary>The folder the backup files live in (inside the data folder).</summary>
    string Folder { get; }

    /// <summary>Writes a new backup file into <see cref="Folder"/> and returns it.</summary>
    BackupFileInfo Create();

    /// <summary>Backup files in the folder, newest first. Files that are not backups are left out.</summary>
    IReadOnlyList<BackupFileInfo> List();

    /// <summary>Puts back what is missing. Flows and connections keep their ids; what already exists is skipped.</summary>
    /// <exception cref="ArgumentException">Not a Tammoa backup, or from a newer version of the app (400).</exception>
    RestoreResult Restore(BackupDocument document);

    /// <summary>Restores one file of <see cref="Folder"/> by its name.</summary>
    /// <exception cref="ArgumentException">Bad name, or not a readable backup (400).</exception>
    /// <exception cref="KeyNotFoundException">No such file (404).</exception>
    RestoreResult RestoreFile(string? fileName);

    /// <summary>Shows <see cref="Folder"/> in the file manager.</summary>
    void OpenFolder();
}
