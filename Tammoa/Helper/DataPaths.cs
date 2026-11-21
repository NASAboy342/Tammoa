namespace Tammoa.Helper;

public static class DataPaths
{
    public const string DataDirEnvVar = "TAMMOA_DATA_DIR";

    /// <summary>Per-user data folder (never inside the repo or build output). TAMMOA_DATA_DIR overrides it,
    /// so dev runs and tests can use a throwaway location.</summary>
    public static string DataDir()
    {
        var overrideDir = Environment.GetEnvironmentVariable(DataDirEnvVar);
        if (!string.IsNullOrWhiteSpace(overrideDir)) return overrideDir;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tammoa");
    }

    /// <summary>Creates the data folder and, on macOS/Linux, makes it owner-only: it holds the database, which stores connection API keys in plaintext.</summary>
    public static void RestrictToOwner(string dir)
    {
        Directory.CreateDirectory(dir);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static string DatabaseFile() => Path.Combine(DataDir(), "tammoa.db");
}
