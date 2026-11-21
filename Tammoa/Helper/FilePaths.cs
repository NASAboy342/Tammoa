namespace Tammoa.Helper;

public static class FilePaths
{
    /// <summary>Expands a leading ~ to the home folder. Everything else is returned as typed.</summary>
    public static string Expand(string path)
    {
        var p = path.Trim();
        if (p == "~") return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (p.StartsWith("~/") || p.StartsWith("~\\"))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), p[2..]);
        return p;
    }

    /// <summary>Expands ~ and requires an absolute path, so a card never depends on the app's working directory.</summary>
    /// <exception cref="ArgumentException">Empty or relative path.</exception>
    public static string RequireAbsolute(string path)
    {
        var expanded = Expand(path);
        if (expanded.Length == 0) throw new ArgumentException("Path is empty.");
        if (!Path.IsPathRooted(expanded)) throw new ArgumentException($"Use an absolute path (or start with ~/): {path}");
        return Path.GetFullPath(expanded);
    }
}
