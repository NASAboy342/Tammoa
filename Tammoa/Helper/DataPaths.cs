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

    public static string DatabaseFile() => Path.Combine(DataDir(), "tammoa.db");
}
