using Tammoa.Services.Tools;

namespace Tammoa.Helper;

/// <summary>The restricted domain of the AI tools: a path is only usable when, after resolving "..", "~" and symbolic links, it is inside the root folder.
/// Callers must use the path this returns (never the text the model wrote), so what was checked is what is touched.</summary>
public static class RootGuard
{
    private const int MaxLinkDepth = 40;

    /// <summary>Resolves a path from a tool call. A relative path is taken inside the root.</summary>
    /// <exception cref="ToolRejectedException">The path leaves the root.</exception>
    public static string Resolve(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("The path is empty.");
        var realRoot = RealPath(root);
        var expanded = FilePaths.Expand(path);
        var real = RealPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(realRoot, expanded));
        if (!IsInside(realRoot, real))
            throw new ToolRejectedException($"'{path}' is outside the allowed folder. Only paths inside {root} may be used.");
        return real;
    }

    public static bool IsInside(string realRoot, string realPath)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootWithSeparator = realRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return string.Equals(realPath.TrimEnd(Path.DirectorySeparatorChar), rootWithSeparator.TrimEnd(Path.DirectorySeparatorChar), comparison)
            || realPath.StartsWith(rootWithSeparator, comparison);
    }

    /// <summary>The absolute path with every existing symbolic link on the way replaced by its target (the part that does not exist yet is kept as is).</summary>
    public static string RealPath(string path) => RealPath(path, 0);

    private static string RealPath(string path, int depth)
    {
        if (depth > MaxLinkDepth) throw new InvalidOperationException("Too many symbolic links.");
        var full = Path.GetFullPath(path);
        var start = Path.GetPathRoot(full)!;
        var parts = full[start.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var current = start;
        for (var i = 0; i < parts.Length; i++)
        {
            var next = Path.Combine(current, parts[i]);
            FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            if (info.LinkTarget is null)
            {
                if (!info.Exists) { current = Path.Combine([next, .. parts[(i + 1)..]]); break; }
                current = next;
                continue;
            }
            var target = Path.GetFullPath(info.LinkTarget, current);
            current = RealPath(Path.Combine([target, .. parts[(i + 1)..]]), depth + 1);
            break;
        }
        return current;
    }
}
