using System.Text.RegularExpressions;
using Tammoa.Services.Tools;

namespace Tammoa.Helper;

/// <summary>Best-effort check of a script a model wrote: every path-looking piece must stay inside the root folder.
/// This is NOT a sandbox: a script can build a path at run time, call another program, or use a network. It stops the obvious mistakes
/// (absolute paths, "..", "~", HOME variables); the approver model and the working directory are the other layers.</summary>
public static class ScriptPathGuard
{
    private static readonly Regex Home = new(@"\$\{?HOME\b|%USERPROFILE%|%HOMEPATH%|\$env:(USERPROFILE|HOME)\b|\.homedir\b|expanduser|Path\.home\(\)|process\.env\.HOME", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Parent = new(@"(?<![\w.])\.\.(?=[\\/\s'""`;|&)]|$)", RegexOptions.Compiled);
    private static readonly Regex Absolute = new(@"(?<![\w:/.$\-])(?:~|[A-Za-z]:[\\/]|\\\\|/(?![/\s*=]))[^\s'""`;|&<>(){}\[\],]*", RegexOptions.Compiled);
    private static readonly HashSet<string> Harmless = new(StringComparer.Ordinal) { "/dev/null", "/dev/stdin", "/dev/stdout", "/dev/stderr" };

    /// <exception cref="ToolRejectedException">A piece of the script points outside the root.</exception>
    public static void Check(string code, string root)
    {
        if (Home.IsMatch(code)) throw new ToolRejectedException($"The script reaches for the home folder. Scripts may only use paths inside {root} (use relative paths).");
        if (Parent.IsMatch(code)) throw new ToolRejectedException($"The script uses '..' to leave its folder. Scripts may only use paths inside {root} (use relative paths).");
        var realRoot = RootGuard.RealPath(root);
        foreach (Match m in Absolute.Matches(code))
        {
            var token = m.Value.TrimEnd('.', ':');
            if (token.Length == 0 || Harmless.Contains(token)) continue;
            string real;
            try { real = RootGuard.RealPath(Path.GetFullPath(FilePaths.Expand(token))); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { continue; } // not a path after all
            if (!RootGuard.IsInside(realRoot, real))
                throw new ToolRejectedException($"The script uses the path '{token}', which is outside the allowed folder. Scripts may only use paths inside {root} (use relative paths).");
        }
    }
}
