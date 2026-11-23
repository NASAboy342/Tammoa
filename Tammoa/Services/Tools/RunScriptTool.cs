using Newtonsoft.Json.Linq;
using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Services.Tools;

/// <summary>Runs a script the model wrote, with the root folder as its working directory. The path check is best effort, see <see cref="ScriptPathGuard"/>.</summary>
public class RunScriptTool : ITool
{
    private static readonly HashSet<string> Languages = ["shell", "javascript", "python"];
    private readonly IProcessRunner _runner;

    public RunScriptTool(IProcessRunner runner) => _runner = runner;

    public ToolDefinition Definition { get; } = new(ToolIds.RunScript,
        "Runs a short script (shell, Node.js javascript or Python) in the allowed folder (it is the working directory; use relative paths only) and returns its output and exit code. It runs on the computer, NOT in a browser: never use it to run web pages or games, and do not use it to create files (use write_file for that).",
        ToolArgs.Schema(new JObject
        {
            ["language"] = new JObject { ["type"] = "string", ["enum"] = new JArray("shell", "javascript", "python"), ["description"] = "shell (default), javascript (node) or python." },
            ["code"] = ToolArgs.Prop("The script text."),
        }, "code"));

    public bool NeedsRoot => true;

    public void Check(JObject arguments, ToolContext context)
    {
        Language(arguments);
        ScriptPathGuard.Check(ToolArgs.Required(arguments, "code"), ToolArgs.RequireRoot(context));
    }

    public async Task<string> RunAsync(JObject arguments, ToolContext context, CancellationToken ct)
    {
        Check(arguments, context);
        var root = ToolArgs.RequireRoot(context);
        var language = Language(arguments);
        var code = ToolArgs.Required(arguments, "code");
        var scratch = Path.Combine(Path.GetTempPath(), "tammoa-tool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            string command;
            if (language == "shell") command = code;
            else
            {
                var js = language == "javascript";
                var file = Path.Combine(scratch, js ? "script.js" : "script.py");
                await File.WriteAllTextAsync(file, code, ct);
                var interpreter = js ? "node" : OperatingSystem.IsWindows() ? "python" : "python3";
                command = $"{interpreter} \"{file}\"";
            }
            var r = await _runner.RunAsync(command, root, null, TimeSpan.FromSeconds(30), ct);
            if (r.TimedOut) throw new InvalidOperationException("The script was stopped after 30 seconds.");
            return $"exit code {r.ExitCode}\nstdout:\n{ToolArgs.Limit(r.Stdout, 8000)}\nstderr:\n{ToolArgs.Limit(r.Stderr, 4000)}";
        }
        finally
        {
            try { Directory.Delete(scratch, true); } catch (IOException) { /* best effort */ }
        }
    }

    private static string Language(JObject a)
    {
        var l = ToolArgs.Optional(a, "language", "shell").ToLowerInvariant();
        return Languages.Contains(l) ? l : throw new ArgumentException($"Unknown language '{l}' (use shell, javascript or python).");
    }
}
