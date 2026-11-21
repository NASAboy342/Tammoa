using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Services.Cards;

/// <summary>Runs code in Shell, JavaScript (Node) or Python. The type id stays "script.shell" so existing flows keep working.
/// Reading the input:
///  - JavaScript: the variables <c>input</c> (the card input) and <c>runInput</c> (the value passed with the run) are ready to use.
///  - Python: <c>input_data</c> and <c>run_input</c> (the built-in <c>input()</c> is left alone).
///  - JavaScript and Python also get <c>data</c>: the Data cards of the flow (name -> text, or parsed JSON).
///  - Every language: env TAMMOA_INPUT / TAMMOA_RUN_INPUT / TAMMOA_DATA (JSON text, set when at most 64 KB) and TAMMOA_INPUT_FILE /
///    TAMMOA_RUN_INPUT_FILE / TAMMOA_DATA_FILE (paths of temp files with the JSON, always set). Shell code can also use {{ }} templates.
/// JS and Python run through a tiny wrapper that defines the variables and then loads your code from its own file, so error line numbers
/// are those of your code.</summary>
public class ScriptCard : ICardType
{
    public const string TypeId = "script.shell";
    public const int DefaultTimeoutSeconds = 30;
    public const int MaxTimeoutSeconds = 600;
    public const int MaxEnvJsonLength = 64 * 1024;

    // The wrappers define the ready-made variables, then load the user's file untouched (CommonJS require / Python exec of the compiled file).
    private const string JavaScriptWrapper = """
        const fs = require('fs');
        globalThis.input = JSON.parse(fs.readFileSync(process.env.TAMMOA_INPUT_FILE, 'utf8'));
        globalThis.runInput = JSON.parse(fs.readFileSync(process.env.TAMMOA_RUN_INPUT_FILE, 'utf8'));
        globalThis.data = JSON.parse(fs.readFileSync(process.env.TAMMOA_DATA_FILE, 'utf8'));
        require(process.env.TAMMOA_SCRIPT_FILE);
        """;

    private const string PythonWrapper = """
        import json, os
        _script = os.environ['TAMMOA_SCRIPT_FILE']
        with open(os.environ['TAMMOA_INPUT_FILE'], encoding='utf-8') as _f:
            input_data = json.load(_f)
        with open(os.environ['TAMMOA_RUN_INPUT_FILE'], encoding='utf-8') as _f:
            run_input = json.load(_f)
        with open(os.environ['TAMMOA_DATA_FILE'], encoding='utf-8') as _f:
            data = json.load(_f)
        with open(_script, encoding='utf-8') as _f:
            _code = compile(_f.read(), _script, 'exec')
        exec(_code, {'__name__': '__main__', '__file__': _script, 'input_data': input_data, 'run_input': run_input, 'data': data})
        """;

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
        "Runs Shell, JavaScript (Node) or Python code. JavaScript gets the ready-made variables input and runInput, Python input_data and run_input. Print JSON and it is available to the next card as { json }. Output: { stdout, stderr, exitCode, json? }.",
        CardCategory.Action, "terminal",
        [
            new CardParamInfo("language", "Language", ParamKind.Select, Required: true, DefaultValue: "shell",
                Options: [new("shell", "Shell (sh / cmd)"), new("javascript", "JavaScript (Node)"), new("python", "Python")]),
            new CardParamInfo("command", "Code", ParamKind.Code, Required: true, CodeLanguageParam: "language",
                Placeholder: "echo hello",
                Help: "Shell code may use {{input.x | sh}} templates. JavaScript and Python are not templated: use their ready-made input variables (see the Guide tab).",
                CodeHelp: ScriptGuide.CodeHelp, CodePlaceholders: ScriptGuide.CodePlaceholders),
            new CardParamInfo("interpreter", "Interpreter (optional)", ParamKind.Text,
                Help: "Default: node for JavaScript, python3 for Python (python on Windows). Set a full path or a command like python3.12 if needed."),
            new CardParamInfo("workingDirectory", "Working directory", ParamKind.Text, Help: "Empty = your home folder."),
            new CardParamInfo("stdin", "Standard input", ParamKind.Textarea, Help: "Optional text piped to the program."),
            new CardParamInfo("timeoutSeconds", "Timeout (seconds)", ParamKind.Number, DefaultValue: DefaultTimeoutSeconds, Help: "1 to 600."),
            new CardParamInfo("failOnError", "Fail on non-zero exit", ParamKind.Bool, DefaultValue: true),
        ])
    {
        Guide = ScriptGuide.Create(),
    };

    public async Task<CardResult> ExecuteAsync(CardExecutionContext context, CancellationToken ct)
    {
        var ctx = context.TemplateContext;
        var language = ParamReader.GetString(context.Params, "language").Trim().ToLowerInvariant();
        if (language.Length == 0) language = "shell";
        if (!Languages.Contains(language)) throw new ArgumentException($"Unknown language '{language}' (use shell, javascript or python).");

        var code = ParamReader.GetString(context.Params, "command");
        if (code.Trim().Length == 0) throw new ArgumentException("Code is empty. Write some, or open the Guide tab and press \"Use this example\".");
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
            var runJson = (context.RunInput ?? JValue.CreateNull()).ToString(Formatting.None);
            var runInputFile = Path.Combine(scratch, "run-input.json");
            await File.WriteAllTextAsync(runInputFile, runJson, ct);
            var dataJson = (context.Data ?? new JObject()).ToString(Formatting.None);
            var dataFile = Path.Combine(scratch, "data.json");
            await File.WriteAllTextAsync(dataFile, dataJson, ct);
            var env = new Dictionary<string, string> { ["TAMMOA_INPUT_FILE"] = inputFile, ["TAMMOA_RUN_INPUT_FILE"] = runInputFile, ["TAMMOA_DATA_FILE"] = dataFile };
            if (dataJson.Length <= MaxEnvJsonLength) env["TAMMOA_DATA"] = dataJson;
            if (inputJson.Length <= MaxEnvJsonLength) env["TAMMOA_INPUT"] = inputJson;
            if (runJson.Length <= MaxEnvJsonLength) env["TAMMOA_RUN_INPUT"] = runJson;

            string command, interpreter = "";
            if (language == "shell") command = code;
            else
            {
                var js = language == "javascript";
                var file = Path.Combine(scratch, js ? "script.js" : "script.py");
                var wrapper = Path.Combine(scratch, js ? "wrapper.js" : "wrapper.py");
                await File.WriteAllTextAsync(file, code, ct);
                await File.WriteAllTextAsync(wrapper, (js ? JavaScriptWrapper : PythonWrapper).Replace("\r\n", "\n"), ct);
                env["TAMMOA_SCRIPT_FILE"] = file;
                interpreter = ParamReader.GetString(context.Params, "interpreter").Trim();
                if (interpreter.Length == 0) interpreter = js ? "node" : OperatingSystem.IsWindows() ? "python" : "python3";
                command = $"{interpreter} {QuotePath(wrapper)}";
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
            // `json` = the output parsed as JSON. If the whole output is not JSON (e.g. log lines first), the last line is tried,
            // so a script can print progress and finish with its JSON result.
            var trimmed = result.Stdout.Trim();
            if (trimmed.Length > 0 && !result.Truncated)
            {
                var parsed = TryParseJson(trimmed);
                if (parsed is null && trimmed.Contains('\n')) parsed = TryParseJson(trimmed[(trimmed.LastIndexOf('\n') + 1)..].Trim());
                if (parsed is not null) output["json"] = parsed;
            }
            return output;
        }
        finally
        {
            try { Directory.Delete(scratch, true); } catch (IOException) { /* best effort */ }
        }
    }

    private static JToken? TryParseJson(string text)
    {
        try { return JToken.Parse(text); }
        catch (JsonReaderException) { return null; }
    }

    private static string QuotePath(string path) =>
        OperatingSystem.IsWindows() ? "\"" + path + "\"" : "'" + path.Replace("'", "'\\''") + "'";
}
