using Newtonsoft.Json.Linq;
using NSubstitute;
using Tammoa.Models;
using Tammoa.Services;
using Tammoa.Services.Cards;

namespace Tammoa.Test.Services.Cards;

public class ScriptLanguagesTest
{
    private static CardExecutionContext Ctx(string p, JToken? input = null, JToken? run = null) => new(JObject.Parse(p), input, run);
    private static string Json(object o) => JObject.FromObject(o).ToString();

    /// <summary>A runner that records the command and environment, and checks the script file while it still exists.</summary>
    private sealed class Spy : IProcessRunner
    {
        public string? Command;
        public IReadOnlyDictionary<string, string>? Env;
        public string? ScriptText, InputFileText;
        public string? WorkingDirectory;
        public ProcessResult Result = new(0, "ok", "", false, false);

        public Task<ProcessResult> RunAsync(string command, string workingDirectory, string? stdin, TimeSpan timeout, CancellationToken ct, IReadOnlyDictionary<string, string>? environment = null)
        {
            Command = command;
            Env = environment;
            WorkingDirectory = workingDirectory;
            var m = System.Text.RegularExpressions.Regex.Match(command, "'([^']+script\\.(?:js|py))'");
            if (m.Success) ScriptText = File.ReadAllText(m.Groups[1].Value);
            if (environment is not null && environment.TryGetValue("TAMMOA_INPUT_FILE", out var f)) InputFileText = File.ReadAllText(f);
            return Task.FromResult(Result);
        }
    }

    private static (ScriptCard Card, Spy Spy) Make(ProcessResult? result = null)
    {
        var spy = new Spy();
        if (result is not null) spy.Result = result;
        return (new ScriptCard(new TemplateEngine(), spy), spy);
    }

    [Test]
    public async Task JavaScript_WritesTheCodeToAFile_RunsNode_AndPassesInputThroughTheEnvironment()
    {
        var (card, spy) = Make();
        var input = JObject.Parse("{\"x\":1}");
        await card.ExecuteAsync(Ctx(Json(new { language = "javascript", command = "console.log({{not a template}})" }), input, "run-value"), default);

        Assert.That(spy.Command, Does.StartWith("node '").And.Contain("script.js"));
        Assert.That(spy.ScriptText, Is.EqualTo("console.log({{not a template}})"), "JavaScript is not templated");
        Assert.That(spy.Env!["TAMMOA_INPUT"], Is.EqualTo("{\"x\":1}"));
        Assert.That(spy.Env["TAMMOA_RUN_INPUT"], Is.EqualTo("\"run-value\""));
        Assert.That(spy.InputFileText, Is.EqualTo("{\"x\":1}"));
    }

    [Test]
    public async Task Python_UsesPython3_AndAnInterpreterOverrideWins()
    {
        var (card, spy) = Make();
        await card.ExecuteAsync(Ctx(Json(new { language = "python", command = "print(1)" })), default);
        Assert.That(spy.Command, Does.StartWith(OperatingSystem.IsWindows() ? "python " : "python3 ").And.Contain("script.py"));
        await card.ExecuteAsync(Ctx(Json(new { language = "python", command = "print(1)", interpreter = "/opt/py/bin/python3.12" })), default);
        Assert.That(spy.Command, Does.StartWith("/opt/py/bin/python3.12 '"));
    }

    [Test]
    public async Task Shell_StillTemplated_AndGetsTheSameEnvironment()
    {
        var (card, spy) = Make();
        await card.ExecuteAsync(Ctx(Json(new { language = "shell", command = "echo {{input.x | sh}}" }), JObject.Parse("{\"x\":\"a b\"}")), default);
        Assert.That(spy.Command, Is.EqualTo("echo 'a b'"));
        Assert.That(spy.Env!.ContainsKey("TAMMOA_INPUT_FILE"), Is.True);
    }

    [Test]
    public async Task NoLanguage_MeansShell_ForFlowsSavedBeforeLanguagesExisted()
    {
        var (card, spy) = Make();
        await card.ExecuteAsync(Ctx(Json(new { command = "echo old" })), default);
        Assert.That(spy.Command, Is.EqualTo("echo old"));
    }

    [Test]
    public async Task LargeInput_IsOnlyAvailableThroughTheFile()
    {
        var (card, spy) = Make();
        var big = new JObject { ["blob"] = new string('x', ScriptCard.MaxEnvJsonLength + 10) };
        await card.ExecuteAsync(Ctx(Json(new { language = "javascript", command = "1" }), big), default);
        Assert.That(spy.Env!.ContainsKey("TAMMOA_INPUT"), Is.False);
        Assert.That(spy.InputFileText, Does.Contain("blob"));
    }

    [Test]
    public async Task OutputJson_IsParsedWhenStdoutIsJson()
    {
        var (card, _) = Make(new ProcessResult(0, "{\"a\":[1,2]}\n", "", false, false));
        var r = (await card.ExecuteAsync(Ctx(Json(new { language = "javascript", command = "1" })), default)).Output;
        Assert.That((int)r["json"]!["a"]![1]!, Is.EqualTo(2));
        var (card2, _) = Make(new ProcessResult(0, "plain text", "", false, false));
        var r2 = (await card2.ExecuteAsync(Ctx(Json(new { language = "javascript", command = "1" })), default)).Output;
        Assert.That(r2["json"], Is.Null);
    }

    [Test]
    public void MissingInterpreter_GivesAHelpfulMessage()
    {
        var (card, _) = Make(new ProcessResult(127, "", "sh: node: command not found", false, false));
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => card.ExecuteAsync(Ctx(Json(new { language = "javascript", command = "1" })), default));
        Assert.That(ex!.Message, Does.Contain("'node' was not found").And.Contain("Interpreter"));
    }

    [Test]
    public async Task TemporaryFiles_AreRemovedAfterTheRun()
    {
        var (card, spy) = Make();
        await card.ExecuteAsync(Ctx(Json(new { language = "python", command = "print(1)" })), default);
        var m = System.Text.RegularExpressions.Regex.Match(spy.Command!, "'([^']+)'");
        Assert.That(File.Exists(m.Groups[1].Value), Is.False);
        Assert.That(Directory.Exists(Path.GetDirectoryName(m.Groups[1].Value)), Is.False);
    }

    [TestCase("{\"language\":\"ruby\",\"command\":\"1\"}")]
    [TestCase("{\"language\":\"python\",\"command\":\"  \"}")]
    public void BadConfig_Throws(string p)
    {
        var (card, spy) = Make();
        Assert.ThrowsAsync<ArgumentException>(() => card.ExecuteAsync(Ctx(p), default));
        Assert.That(spy.Command, Is.Null);
    }

    [Test]
    public void Info_OffersThreeLanguages_AndTheCodeFieldFollowsThem()
    {
        var info = new ScriptCard(new TemplateEngine(), Substitute.For<IProcessRunner>()).Info;
        Assert.That(info.Params.Single(p => p.Key == "language").Options!.Select(o => o.Value), Is.EqualTo(new[] { "shell", "javascript", "python" }));
        Assert.That(info.Params.Single(p => p.Key == "command").CodeLanguageParam, Is.EqualTo("language"));
    }
}

/// <summary>The real thing: needs node / python3 on PATH, otherwise these are ignored (reported, not silently green).</summary>
[Platform(Exclude = "Win")]
public class ScriptRuntimesTest
{
    private static readonly ScriptCard Card = new(new TemplateEngine(), new ProcessRunner());
    private static string Json(object o) => JObject.FromObject(o).ToString();
    private static bool Found(string exe)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/sh", ["-c", $"command -v {exe}"]) { RedirectStandardOutput = true, UseShellExecute = false })!;
        p.WaitForExit(3000);
        return p.ExitCode == 0;
    }

    [Test]
    public async Task Node_ReadsInputAndPrintsJson()
    {
        if (!Found("node")) Assert.Ignore("node is not installed");
        var r = (await Card.ExecuteAsync(new CardExecutionContext(JObject.Parse(Json(new { language = "javascript", command = "const i = JSON.parse(process.env.TAMMOA_INPUT); console.log(JSON.stringify({ doubled: i.n * 2, run: JSON.parse(process.env.TAMMOA_RUN_INPUT) }))" })), JObject.Parse("{\"n\":21}"), "go"), default)).Output;
        Assert.That((int)r["json"]!["doubled"]!, Is.EqualTo(42));
        Assert.That((string?)r["json"]!["run"], Is.EqualTo("go"));
    }

    [Test]
    public async Task Python_ReadsInputFromTheFile()
    {
        if (!Found("python3")) Assert.Ignore("python3 is not installed");
        var code = "import json, os\nd = json.load(open(os.environ['TAMMOA_INPUT_FILE']))\nprint(json.dumps({'got': d['name'].upper()}))";
        var r = (await Card.ExecuteAsync(new CardExecutionContext(JObject.Parse(Json(new { language = "python", command = code })), JObject.Parse("{\"name\":\"tammoa\"}"), null), default)).Output;
        Assert.That((string?)r["json"]!["got"], Is.EqualTo("TAMMOA"));
    }

    [Test]
    public void Python_SyntaxError_FailsWithTheInterpretersMessage()
    {
        if (!Found("python3")) Assert.Ignore("python3 is not installed");
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => Card.ExecuteAsync(new CardExecutionContext(JObject.Parse(Json(new { language = "python", command = "def (" })), null, null), default));
        Assert.That(ex!.Message, Does.Contain("SyntaxError"));
    }

    [Test]
    public void UnknownInterpreter_IsReportedClearly()
    {
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => Card.ExecuteAsync(new CardExecutionContext(JObject.Parse(Json(new { language = "javascript", command = "1", interpreter = "definitely-not-a-runtime" })), null, null), default));
        Assert.That(ex!.Message, Does.Contain("was not found"));
    }
}
