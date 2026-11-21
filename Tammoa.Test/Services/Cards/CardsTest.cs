using Newtonsoft.Json.Linq;
using NSubstitute;
using Tammoa.Models;
using Tammoa.Services;
using Tammoa.Services.Cards;

namespace Tammoa.Test.Services.Cards;

public class CardsTest
{
    private static CardExecutionContext Ctx(string paramsJson, JToken? input = null, JToken? runInput = null) =>
        new(JObject.Parse(paramsJson), input, runInput);

    // --- manual trigger
    [Test]
    public async Task ManualTrigger_OutputsRunInput()
    {
        var r = (await new ManualTriggerCard().ExecuteAsync(Ctx("{}", runInput: "hello"), default)).Output;
        Assert.That((string?)r["input"], Is.EqualTo("hello"));
    }

    [Test]
    public async Task ManualTrigger_NoRunInput_OutputsNull()
    {
        var r = (await new ManualTriggerCard().ExecuteAsync(Ctx("{}"), default)).Output;
        Assert.That(r["input"]!.Type, Is.EqualTo(JTokenType.Null));
    }

    [Test]
    public void ManualTrigger_IsATrigger() => Assert.That(new ManualTriggerCard().Info.IsTrigger, Is.True);

    // --- set value
    private static SetValueCard Set() => new(new TemplateEngine());

    [Test]
    public async Task SetValue_ResolvesTemplatesAgainstInputAndRunInput()
    {
        var input = JObject.Parse("{\"stdout\":\"out\",\"n\":3}");
        var r = (await Set().ExecuteAsync(Ctx("{\"values\":\"{\\\"a\\\":\\\"{{input.stdout}}\\\",\\\"n\\\":\\\"{{input.n}}\\\",\\\"r\\\":\\\"{{run.input}}\\\"}\"}", input, "ri"), default)).Output;
        Assert.That((string?)r["a"], Is.EqualTo("out"));
        Assert.That(r["n"]!.Type, Is.EqualTo(JTokenType.Integer));
        Assert.That((string?)r["r"], Is.EqualTo("ri"));
    }

    [TestCase("{}")]
    [TestCase("{\"values\":\"\"}")]
    [TestCase("{\"values\":\"{not json\"}")]
    [TestCase("{\"values\":\"[1,2]\"}")]
    public void SetValue_BadValues_ThrowsArgumentException(string p) =>
        Assert.ThrowsAsync<ArgumentException>(() => Set().ExecuteAsync(Ctx(p), default));

    // --- shell script (process runner substituted)
    private static (ScriptCard Card, IProcessRunner Runner) Shell(ProcessResult? result = null)
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(default!, default!, default, default, default, default)
            .ReturnsForAnyArgs(Task.FromResult(result ?? new ProcessResult(0, "out", "", false, false)));
        return (new ScriptCard(new TemplateEngine(), runner), runner);
    }

    [Test]
    public async Task Shell_RendersCommandWithTemplatesAndReturnsOutput()
    {
        var (card, runner) = Shell();
        var r = (await card.ExecuteAsync(Ctx("{\"command\":\"echo {{input.x | sh}}\",\"workingDirectory\":\"" + Path.GetTempPath().Replace("\\", "\\\\") + "\",\"stdin\":\"in {{input.x}}\",\"timeoutSeconds\":\"5\"}", JObject.Parse("{\"x\":\"a b\"}")), default)).Output;
        await runner.Received(1).RunAsync("echo 'a b'", Arg.Any<string>(), "in a b", TimeSpan.FromSeconds(5), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyDictionary<string, string>?>());
        Assert.That((string?)r["stdout"], Is.EqualTo("out"));
        Assert.That((int)r["exitCode"]!, Is.EqualTo(0));
    }

    [Test]
    public async Task Shell_EmptyWorkingDirectory_UsesHomeFolder()
    {
        var (card, runner) = Shell();
        await card.ExecuteAsync(Ctx("{\"command\":\"ls\"}"), default);
        await runner.Received(1).RunAsync("ls", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), null, TimeSpan.FromSeconds(30), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyDictionary<string, string>?>());
    }

    [Test]
    public void Shell_NonZeroExit_FailsWithStderr()
    {
        var (card, _) = Shell(new ProcessResult(2, "", "boom", false, false));
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => card.ExecuteAsync(Ctx("{\"command\":\"x\"}"), default));
        Assert.That(ex!.Message, Does.Contain("code 2").And.Contain("boom"));
    }

    [Test]
    public async Task Shell_NonZeroExitWithFailOnErrorOff_Succeeds()
    {
        var (card, _) = Shell(new ProcessResult(2, "", "boom", false, false));
        var r = (await card.ExecuteAsync(Ctx("{\"command\":\"x\",\"failOnError\":false}"), default)).Output;
        Assert.That((int)r["exitCode"]!, Is.EqualTo(2));
    }

    [Test]
    public void Shell_TimedOut_Fails()
    {
        var (card, _) = Shell(new ProcessResult(-1, "", "", true, false));
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => card.ExecuteAsync(Ctx("{\"command\":\"x\",\"timeoutSeconds\":1}"), default));
        Assert.That(ex!.Message, Does.Contain("Timed out"));
    }

    [TestCase("{}")]
    [TestCase("{\"command\":\"  \"}")]
    [TestCase("{\"command\":\"x\",\"timeoutSeconds\":0}")]
    [TestCase("{\"command\":\"x\",\"timeoutSeconds\":601}")]
    [TestCase("{\"command\":\"x\",\"workingDirectory\":\"/definitely/not/here\"}")]
    public void Shell_BadConfig_ThrowsArgumentExceptionAndRunsNothing(string p)
    {
        var (card, runner) = Shell();
        Assert.ThrowsAsync<ArgumentException>(() => card.ExecuteAsync(Ctx(p), default));
        runner.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, default, default, default, default);
    }

    // --- registry
    [Test]
    public void Registry_FindsByTypeAndListsInfos()
    {
        var registry = new CardRegistry([new ManualTriggerCard(), Set()]);
        Assert.That(registry.Find("data.set"), Is.Not.Null);
        Assert.That(registry.Find("nope"), Is.Null);
        Assert.That(registry.All().Select(i => i.Type), Is.EquivalentTo(new[] { "trigger.manual", "data.set" }));
    }
}

[Platform(Exclude = "Win")]
public class ProcessRunnerTest
{
    private readonly ProcessRunner _sut = new();
    private static readonly string Dir = Path.GetTempPath();

    [Test]
    public async Task RunAsync_Echo_CapturesStdoutAndExitCode()
    {
        var r = await _sut.RunAsync("echo hello", Dir, null, TimeSpan.FromSeconds(10), default);
        Assert.That(r.Stdout.Trim(), Is.EqualTo("hello"));
        Assert.That(r.ExitCode, Is.EqualTo(0));
    }

    [Test]
    public async Task RunAsync_PipesStdinAndCapturesStderrAndExit()
    {
        var r = await _sut.RunAsync("cat; echo err 1>&2; exit 3", Dir, "piped", TimeSpan.FromSeconds(10), default);
        Assert.That(r.Stdout, Is.EqualTo("piped"));
        Assert.That(r.Stderr.Trim(), Is.EqualTo("err"));
        Assert.That(r.ExitCode, Is.EqualTo(3));
    }

    [Test]
    public async Task RunAsync_Timeout_KillsAndFlags()
    {
        var r = await _sut.RunAsync("sleep 30", Dir, null, TimeSpan.FromMilliseconds(300), default);
        Assert.That(r.TimedOut, Is.True);
    }

    [Test]
    public void RunAsync_Cancelled_Throws()
    {
        using var cts = new CancellationTokenSource(200);
        Assert.CatchAsync<OperationCanceledException>(() => _sut.RunAsync("sleep 30", Dir, null, TimeSpan.FromSeconds(60), cts.Token));
    }

    [Test]
    public async Task RunAsync_HugeOutput_IsCappedAndFlagged()
    {
        var r = await _sut.RunAsync("yes x | head -c 200000", Dir, null, TimeSpan.FromSeconds(20), default);
        Assert.That(r.Stdout.Length, Is.EqualTo(ProcessRunner.OutputCap));
        Assert.That(r.Truncated, Is.True);
    }
}
