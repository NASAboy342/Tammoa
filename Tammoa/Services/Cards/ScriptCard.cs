using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Services.Cards;

/// <summary>Runs code in Shell, JavaScript (Node) or Python. The type id stays "script.shell" so existing flows keep working.
/// Input contract for every language: env TAMMOA_INPUT (JSON of the card input, when it is at most 64 KB), TAMMOA_RUN_INPUT
/// (JSON of the value passed with the run, same limit) and TAMMOA_INPUT_FILE (path of a temp file holding the input JSON, always set).</summary>
public class ScriptCard : ICardType
{
    public const string TypeId = "script.shell";
    public const int DefaultTimeoutSeconds = 30;
    public const int MaxTimeoutSeconds = 600;
    public const int MaxEnvJsonLength = 64 * 1024;

    private static readonly string[] Languages = ["shell", "javascript", "python"];
    private readonly ITemplateEngine _templates;
    private readonly IProcessRunner _runner;

    public ScriptCard(ITemplateEngine templates, IProcessRunner runner)
    {
        _templates = templates;
        _runner = runner;
    }

    public CardTypeInfo Info { get; } = new(
        TypeId, "Script",
        "Runs Shell, JavaScript (Node) or Python code. The code reads its input from $TAMMOA_INPUT (JSON) and prints its result; if the output is JSON it is also available as { json }. Output: { stdout, stderr, exitCode, json? }.",
        CardCategory.Action, "terminal",
        [
            new CardParamInfo("language", "Language", ParamKind.Select, Required: true, DefaultValue: "shell",
                Options: [new("shell", "Shell (sh / cmd)"), new("javascript", "JavaScript (Node)"), new("python", "Python")]),
            new CardParamInfo("command", "Code", ParamKind.Code, Required: true, CodeLanguageParam: "language",
                Placeholder: "echo hello",
                Help: "Shell code may use {{input.x | sh}} templates. JavaScript and Python are not templated: read $TAMMOA_INPUT instead."),
            new CardParamInfo("interpreter", "Interpreter (optional)", ParamKind.Text,
                Help: "Default: node for JavaScript, python3 for Python (python on Windows). Set a full path or a command like python3.12 if needed."),
            new CardParamInfo("workingDirectory", "Working directory", ParamKind.Text, Help: "Empty = your home folder."),
            new CardParamInfo("stdin", "Standard input", ParamKind.Textarea, Help: "Optional text piped to the program."),
            new CardParamInfo("timeoutSeconds", "Timeout (seconds)", ParamKind.Number, DefaultValue: DefaultTimeoutSeconds, Help: "1 to 600."),
            new CardParamInfo("failOnError", "Fail on non-zero exit", ParamKind.Bool, DefaultValue: true),
        ]);

    public async Task<CardResult> ExecuteAsync(CardExecutionContext context, CancellationToken ct)
    {
        var ctx = context.TemplateContext;
        var language = ParamReader.GetString(context.Params, "language").Trim().ToLowerInvariant();
        if (language.Length == 0) language = "shell";
        if (!Languages.Contains(language)) throw new ArgumentException($"Unknown language '{language}' (use shell, javascript or python).");

        var code = ParamReader.GetString(context.Params, "command");
        if (code.Trim().Length == 0) throw new ArgumentException("Code is empty.");
        if (language == "shell") code = _templates.Render(code, ctx);

        var wdParam = _templates.Render(ParamReader.GetString(context.Params, "workingDirectory"), ctx).Trim();
        var workingDirectory = wdParam.Length > 0 ? FilePaths.Expand(wdParam) : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Directory.Exists(workingDirectory)) throw new ArgumentException($"Working directory does not exist: {workingDirectory}");

        var stdinText = ParamReader.GetString(context.Params, "stdin");
        var stdin = stdinText.Length > 0 ? _templates.Render(stdinText, ctx) : null;

        var seconds = ParamReader.GetInt(context.Params, "timeoutSeconds", DefaultTimeoutSeconds);
        if (seconds < 1 || seconds > MaxTimeoutSeconds) throw new ArgumentException($"Timeout must be between 1 and {MaxTimeoutSeconds} seconds.");

        var scratch = Path.Combine(Path.GetTempPath(), "tammoa-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var inputJson = (context.Input ?? JValue.CreateNull()).ToString(Formatting.None);
            var inputFile = Path.Combine(scratch, "input.json");
            await File.WriteAllTextAsync(inputFile, inputJson, ct);
            var env = new Dictionary<string, string> { ["TAMMOA_INPUT_FILE"] = inputFile };
            if (inputJson.Length <= MaxEnvJsonLength) env["TAMMOA_INPUT"] = inputJson;
            var runJson = (context.RunInput ?? JValue.CreateNull()).ToString(Formatting.None);
            if (runJson.Length <= MaxEnvJsonLength) env["TAMMOA_RUN_INPUT"] = runJson;

            string command, interpreter = "";
            if (language == "shell") command = code;
            else
            {
                var file = Path.Combine(scratch, language == "javascript" ? "script.js" : "script.py");
                await File.WriteAllTextAsync(file, code, ct);
                interpreter = ParamReader.GetString(context.Params, "interpreter").Trim();
                if (interpreter.Length == 0) interpreter = language == "javascript" ? "node" : OperatingSystem.IsWindows() ? "python" : "python3";
                command = $"{interpreter} {QuotePath(file)}";
            }

            var result = await _runner.RunAsync(command, workingDirectory, stdin, TimeSpan.FromSeconds(seconds), ct, env);

            if (result.TimedOut) throw new InvalidOperationException($"Timed out after {seconds} s.");
            if (language != "shell" && result.ExitCode is 127 or 9009)
                throw new InvalidOperationException($"'{interpreter}' was not found. Install it or set the Interpreter setting.");
            if (result.ExitCode != 0 && ParamReader.GetBool(context.Params, "failOnError", true))
            {
                var detail = result.Stderr.Trim();
                if (detail.Length > 500) detail = detail[..500] + "…";
                throw new InvalidOperationException($"Command exited with code {result.ExitCode}" + (detail.Length > 0 ? $": {detail}" : "."));
            }

            var output = new JObject
            {
                ["stdout"] = result.Stdout,
                ["stderr"] = result.Stderr,
                ["exitCode"] = result.ExitCode,
                ["timedOut"] = false,
                ["truncated"] = result.Truncated,
            };
            var trimmed = result.Stdout.Trim();
            if (trimmed.Length > 0 && !result.Truncated)
            {
                try { output["json"] = JToken.Parse(trimmed); } catch (JsonReaderException) { /* plain text output */ }
            }
            return output;
        }
        finally
        {
            try { Directory.Delete(scratch, true); } catch (IOException) { /* best effort */ }
        }
    }

    private static string QuotePath(string path) =>
        OperatingSystem.IsWindows() ? "\"" + path + "\"" : "'" + path.Replace("'", "'\\''") + "'";
}
