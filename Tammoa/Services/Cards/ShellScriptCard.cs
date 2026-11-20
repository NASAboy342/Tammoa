using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Helper;
using Tammoa.Models;

namespace Tammoa.Services.Cards;

public class ShellScriptCard : ICardType
{
    public const string TypeId = "script.shell";
    public const int DefaultTimeoutSeconds = 30;
    public const int MaxTimeoutSeconds = 600;

    private readonly ITemplateEngine _templates;
    private readonly IProcessRunner _runner;

    public ShellScriptCard(ITemplateEngine templates, IProcessRunner runner)
    {
        _templates = templates;
        _runner = runner;
    }

    public CardTypeInfo Info { get; } = new(
        TypeId, "Script",
        "Runs a shell command (sh on macOS/Linux, cmd on Windows). Output: { stdout, stderr, exitCode, timedOut }. Use {{input.x | sh}} to insert values safely.",
        CardCategory.Action, "terminal",
        [
            new CardParamInfo("command", "Command", ParamKind.Textarea, Required: true, Placeholder: "echo hello"),
            new CardParamInfo("workingDirectory", "Working directory", ParamKind.Text, Help: "Empty = your home folder."),
            new CardParamInfo("stdin", "Standard input", ParamKind.Textarea, Help: "Optional text piped to the command."),
            new CardParamInfo("timeoutSeconds", "Timeout (seconds)", ParamKind.Number, DefaultValue: DefaultTimeoutSeconds, Help: "1 to 600."),
            new CardParamInfo("failOnError", "Fail on non-zero exit", ParamKind.Bool, DefaultValue: true),
        ]);

    public async Task<JToken> ExecuteAsync(CardExecutionContext context, CancellationToken ct)
    {
        var ctx = context.TemplateContext;
        var command = _templates.Render(ParamReader.GetString(context.Params, "command"), ctx).Trim();
        if (command.Length == 0) throw new ArgumentException("Command is empty.");

        var wdParam = _templates.Render(ParamReader.GetString(context.Params, "workingDirectory"), ctx).Trim();
        var workingDirectory = wdParam.Length > 0 ? wdParam : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Directory.Exists(workingDirectory)) throw new ArgumentException($"Working directory does not exist: {workingDirectory}");

        var stdinText = ParamReader.GetString(context.Params, "stdin");
        var stdin = stdinText.Length > 0 ? _templates.Render(stdinText, ctx) : null;

        var seconds = ParamReader.GetInt(context.Params, "timeoutSeconds", DefaultTimeoutSeconds);
        if (seconds < 1 || seconds > MaxTimeoutSeconds) throw new ArgumentException($"Timeout must be between 1 and {MaxTimeoutSeconds} seconds.");

        var result = await _runner.RunAsync(command, workingDirectory, stdin, TimeSpan.FromSeconds(seconds), ct);

        if (result.TimedOut) throw new InvalidOperationException($"Timed out after {seconds} s.");
        if (result.ExitCode != 0 && ParamReader.GetBool(context.Params, "failOnError", true))
        {
            var detail = result.Stderr.Trim();
            if (detail.Length > 500) detail = detail[..500] + "…";
            throw new InvalidOperationException($"Command exited with code {result.ExitCode}" + (detail.Length > 0 ? $": {detail}" : "."));
        }

        return new JObject
        {
            ["stdout"] = result.Stdout,
            ["stderr"] = result.Stderr,
            ["exitCode"] = result.ExitCode,
            ["timedOut"] = false,
            ["truncated"] = result.Truncated,
        };
    }
}
