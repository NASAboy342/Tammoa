using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Services.Tools;

/// <summary>Opens an HTML page of the allowed folder in a headless browser for a few seconds and reports what the model cannot see otherwise: errors on the console and whether a canvas was drawn.
/// The page can only reach the card's allowed hosts (anything else goes to a dead proxy). It cannot judge whether a game plays right.</summary>
public partial class CheckPageTool : ITool
{
    public const int RunSeconds = 10; // headless Chrome does not exit by itself after --dump-dom, so it is stopped after this
    private const int MaxLines = 12;
    private readonly IProcessRunner _runner;
    private readonly Func<string?> _findBrowser;

    public CheckPageTool(IProcessRunner runner) : this(runner, () => BrowserLocator.Find()) { }
    public CheckPageTool(IProcessRunner runner, Func<string?> findBrowser) { _runner = runner; _findBrowser = findBrowser; }

    public ToolDefinition Definition { get; } = new(ToolIds.CheckPage,
        "Opens an HTML file of the allowed folder in a headless browser for a few seconds and returns the errors it printed (console errors, uncaught exceptions, scripts that did not load) and whether a canvas was drawn. Call it right after write_file on any web page or game, fix every error it lists, write the file again, and call it again. It cannot tell whether the game plays correctly. Scripts from other web sites load only if their host is allowed.",
        ToolArgs.Schema(new JObject { ["path"] = ToolArgs.Prop("The .html file, relative to the allowed folder.") }, "path"));

    public bool NeedsRoot => true;

    public void Check(JObject arguments, ToolContext context)
    {
        var path = RootGuard.Resolve(ToolArgs.RequireRoot(context), ToolArgs.Required(arguments, "path"));
        if (!path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Give an .html file.");
    }

    public async Task<string> RunAsync(JObject arguments, ToolContext context, CancellationToken ct)
    {
        Check(arguments, context);
        var root = ToolArgs.RequireRoot(context);
        var shown = ToolArgs.Required(arguments, "path");
        var path = RootGuard.Resolve(root, shown);
        if (!File.Exists(path)) throw new InvalidOperationException($"File not found: {shown}");
        var browser = _findBrowser() ?? throw new InvalidOperationException("No Chrome, Edge, Chromium or Brave was found on this computer, so the page cannot be opened. Check the script by reading it carefully, or run a syntax check with run_script (node --check on the script text).");

        var profile = Path.Combine(Path.GetTempPath(), "tammoa-page-" + Guid.NewGuid().ToString("N"));
        try
        {
            var flags = new List<string>
            {
                "--headless=new", "--disable-gpu", $"--user-data-dir={Quote(profile)}", "--enable-logging=stderr", "--v=0",
                "--virtual-time-budget=4000", "--proxy-server=http://127.0.0.1:9", // everything off the computer goes to a dead proxy ...
            };
            if (context.AllowedHosts.Count > 0) flags.Add($"--proxy-bypass-list={Quote(string.Join(";", context.AllowedHosts.Select(h => h.Trim())))}"); // ... except the allowed hosts
            flags.Add("--dump-dom");
            flags.Add(Quote(new Uri(path).AbsoluteUri));
            var r = await _runner.RunAsync($"{Quote(browser)} {string.Join(" ", flags)}", root, null, TimeSpan.FromSeconds(RunSeconds), ct);
            var html = await File.ReadAllTextAsync(path, ct);
            return Summarize(r.Stderr, r.Stdout, html, context.AllowedHosts);
        }
        finally
        {
            try { Directory.Delete(profile, true); } catch (IOException) { /* best effort */ }
        }
    }

    /// <summary>Turns the browser's log and the page it dumped into a short report for the model.</summary>
    public static string Summarize(string browserLog, string dumpedDom, string sourceHtml, IReadOnlyList<string> allowedHosts)
    {
        var errors = new List<string>();
        var logs = new List<string>();
        foreach (var line in browserLog.Split('\n'))
        {
            var m = ConsoleLine().Match(line);
            if (!m.Success) continue;
            var level = m.Groups["level"].Value;
            var text = m.Groups["text"].Value.Trim();
            if (text.Contains("Canvas2D: Multiple readback", StringComparison.Ordinal)) continue; // a Chrome performance hint, not a bug
            var where = m.Groups["src"].Success ? $" (line {m.Groups["line"].Value})" : "";
            if (level == "ERROR" || text.StartsWith("Uncaught", StringComparison.Ordinal) || text.StartsWith("Failed to load", StringComparison.Ordinal)) errors.Add(text + where);
            else if (level == "INFO" && !text.Contains("%c", StringComparison.Ordinal)) logs.Add(text); // "%c" lines are library banners (Phaser prints one)
        }
        errors = errors.Distinct().ToList();

        var blocked = ExternalHosts(sourceHtml).Where(h => !allowedHosts.Any(a => HttpRequestTool.HostMatches(a, h))).Distinct().ToList();
        var canvas = dumpedDom.Contains("<canvas", StringComparison.OrdinalIgnoreCase);
        var parts = new List<string>();
        parts.Add(errors.Count == 0 ? "No errors on the console." : $"{errors.Count} error(s):\n" + string.Join("\n", errors.Take(MaxLines).Select(e => "- " + e)) + (errors.Count > MaxLines ? $"\n- … and {errors.Count - MaxLines} more" : ""));
        parts.Add(canvas ? "A <canvas> element is on the page." : "No <canvas> element on the page (fine for a plain page, a problem for a canvas game).");
        if (blocked.Count > 0) parts.Add($"The page uses {string.Join(", ", blocked)}, which {(blocked.Count == 1 ? "is" : "are")} not in this card's allowed hosts, so {(blocked.Count == 1 ? "it was" : "they were")} blocked. Either the host must be added to the allowed hosts, or the page must not need it.");
        if (logs.Count > 0) parts.Add("Console output: " + string.Join(" | ", logs.Take(5).Select(l => l.Length <= 120 ? l : l[..120] + "…")));
        return string.Join("\n", parts);
    }

    private static IEnumerable<string> ExternalHosts(string html) =>
        ExternalUrl().Matches(html).Select(m => m.Groups["host"].Value.ToLowerInvariant()).Where(h => h.Length > 0);

    private static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";

    // [pid:tid:date:LEVEL:source:line] "text", source: file (line)   (Chrome's own log format for console messages)
    [GeneratedRegex("""(?<level>INFO|WARNING|ERROR):CONSOLE:\d+\]\s*"(?<text>.*)"(?:, source: (?<src>.*) \((?<line>\d+)\))?\s*$""")]
    private static partial Regex ConsoleLine();

    [GeneratedRegex("""(?:src|href)\s*=\s*["']\s*(?:https?:)?//(?<host>[^/"':?#\s]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalUrl();
}
