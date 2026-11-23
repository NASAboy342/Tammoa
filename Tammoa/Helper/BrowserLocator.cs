namespace Tammoa.Helper;

/// <summary>Finds a Chromium-based browser (Chrome, Edge, Chromium, Brave) already installed on the computer, to open a page headless. Nothing is bundled or downloaded.</summary>
public static class BrowserLocator
{
    public const string EnvVar = "TAMMOA_BROWSER";

    /// <summary>The first browser found, or null. `TAMMOA_BROWSER` (a full path) wins. `exists` and `env` are for tests.</summary>
    public static string? Find(Func<string, bool>? exists = null, Func<string, string?>? env = null)
    {
        exists ??= File.Exists;
        env ??= Environment.GetEnvironmentVariable;
        if (env(EnvVar) is { Length: > 0 } custom) return exists(custom) ? custom : null;
        return Candidates(env).FirstOrDefault(exists);
    }

    private static IEnumerable<string> Candidates(Func<string, string?> env)
    {
        if (OperatingSystem.IsMacOS())
        {
            foreach (var app in new[] { "Google Chrome", "Microsoft Edge", "Chromium", "Brave Browser" })
                yield return $"/Applications/{app}.app/Contents/MacOS/{app}";
        }
        else if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[] { env("ProgramFiles"), env("ProgramFiles(x86)"), env("LocalAppData") })
            {
                if (string.IsNullOrEmpty(root)) continue;
                yield return Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe");
                yield return Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
                yield return Path.Combine(root, "BraveSoftware", "Brave-Browser", "Application", "brave.exe");
            }
        }
        else
        {
            foreach (var bin in new[] { "google-chrome", "google-chrome-stable", "chromium", "chromium-browser", "microsoft-edge", "brave-browser" })
            foreach (var dir in new[] { "/usr/bin", "/usr/local/bin", "/snap/bin" })
                yield return $"{dir}/{bin}";
        }
    }
}
