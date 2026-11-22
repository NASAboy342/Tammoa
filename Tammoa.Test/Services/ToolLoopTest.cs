using Newtonsoft.Json.Linq;
using NSubstitute;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Services;
using Tammoa.Services.Tools;

namespace Tammoa.Test.Services;

public class ToolLoopTest
{
    private string _base = null!;
    private string _root = null!;
    private IOllamaClient _ollama = null!;
    private ToolLoop _sut = null!;
    private readonly List<JArray> _messagesSeen = new();
    private Queue<OllamaTurnResult> _turns = null!;
    private static JObject A(object o) => JObject.FromObject(o);

    private static Connection Conn() => new("c1", "Home", ConnectionProvider.Ollama, "http://x", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static OllamaTurnResult Say(string text) => new(new OllamaChatResult(text, null, "m", true, "stop", 10, 5, 100), []);
    private static OllamaTurnResult Call(params (string Name, object Args)[] calls) =>
        new(new OllamaChatResult("", null, "m", true, "stop", 10, 5, 100), calls.Select(c => (c.Name, A(c.Args))).ToList());

    [SetUp]
    public void SetUp()
    {
        _base = Path.Combine(Path.GetTempPath(), "tammoa-loop-" + Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_base, "root");
        Directory.CreateDirectory(_root);
        _messagesSeen.Clear();
        _turns = new Queue<OllamaTurnResult>();
        _ollama = Substitute.For<IOllamaClient>();
        _ollama.ChatTurnAsync(default!, default!, default, default, default).ReturnsForAnyArgs(ci =>
        {
            _messagesSeen.Add((JArray)((OllamaTurnRequest)ci[1]).Messages.DeepClone());
            return Task.FromResult(_turns.Dequeue());
        });
        var http = Substitute.For<IHttpRequestRunner>();
        var proc = Substitute.For<IProcessRunner>();
        proc.RunAsync(default!, default!, default, default, default, default).ReturnsForAnyArgs(new ProcessResult(0, "ok", "", false, false));
        var registry = new ToolRegistry([new ReadFileTool(), new WriteFileTool(), new HttpRequestTool(http), new RunScriptTool(proc)]);
        _sut = new ToolLoop(_ollama, registry);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_base, true);

    private ToolLoopRequest Req(string[]? tools = null, string? approver = null, int maxSteps = 8, string[]? hosts = null) =>
        new(Conn(), "m", "be helpful", "write hello to a.txt", null, null, null, TimeSpan.FromSeconds(5), tools ?? [ToolIds.ReadFile, ToolIds.WriteFile],
            new ToolContext(_root, hosts ?? []), approver, maxSteps);

    [Test]
    public async Task TheModelCallsATool_TheResultGoesBack_AndTheFinalAnswerEndsTheLoop()
    {
        _turns.Enqueue(Call((ToolIds.WriteFile, new { path = "a.txt", content = "hello" })));
        _turns.Enqueue(Say("Done, I wrote a.txt."));
        var o = await _sut.RunAsync(Req(), null, default);

        Assert.That(File.ReadAllText(Path.Combine(_root, "a.txt")), Is.EqualTo("hello"));
        Assert.That((string)o["text"]!, Is.EqualTo("Done, I wrote a.txt."));
        Assert.That((int)o["steps"]!, Is.EqualTo(2));
        Assert.That((int)o["promptTokens"]!, Is.EqualTo(20), "tokens are summed over the steps");
        var call = (JObject)((JArray)o["toolCalls"]!).Single();
        Assert.That((string)call["name"]!, Is.EqualTo("write_file"));
        Assert.That((string)call["decision"]!, Is.EqualTo("ran"));
        Assert.That((int)call["step"]!, Is.EqualTo(1));
        Assert.That((string)call["result"]!, Does.Contain("5 bytes"));
        Assert.That(call["durationMs"], Is.Not.Null);

        // the second model call saw: system, user, the assistant's tool call and the tool result
        var second = _messagesSeen[1];
        Assert.That(second.Select(m => (string)m["role"]!), Is.EqualTo(new[] { "system", "user", "assistant", "tool" }));
        Assert.That((string)second[2]["tool_calls"]![0]!["function"]!["name"]!, Is.EqualTo("write_file"));
        Assert.That((string)second[3]["content"]!, Does.Contain("Wrote 5 bytes"));
        Assert.That((string)second[3]["tool_name"]!, Is.EqualTo("write_file"));
    }

    [Test]
    public async Task OnlyTheSelectedTools_AreOffered()
    {
        _turns.Enqueue(Say("hi"));
        await _sut.RunAsync(Req([ToolIds.ReadFile]), null, default);
        await _ollama.Received(1).ChatTurnAsync(Arg.Any<Connection>(),
            Arg.Is<OllamaTurnRequest>(q => q.Tools!.Count == 1 && (string)q.Tools[0]["function"]!["name"]! == "read_file" && q.Tools[0]["function"]!["parameters"]!["type"]!.ToString() == "object"),
            Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>(), Arg.Any<Action<string, string>?>());
    }

    [Test]
    public async Task AToolThatBreaksTheRules_IsRejected_TheModelIsTold_AndTheLoopGoesOn()
    {
        _turns.Enqueue(Call((ToolIds.WriteFile, new { path = "../evil.txt", content = "x" })));
        _turns.Enqueue(Call((ToolIds.WriteFile, new { path = "ok.txt", content = "x" })));
        _turns.Enqueue(Say("done"));
        var o = await _sut.RunAsync(Req(), null, default);

        Assert.That(File.Exists(Path.Combine(_base, "evil.txt")), Is.False);
        Assert.That(File.Exists(Path.Combine(_root, "ok.txt")), Is.True);
        var calls = ((JArray)o["toolCalls"]!).Cast<JObject>().ToList();
        Assert.That(calls.Select(c => (string)c["decision"]!), Is.EqualTo(new[] { "rejected", "ran" }));
        Assert.That((string)calls[0]["reason"]!, Does.Contain("outside the allowed folder"));
        Assert.That((string)_messagesSeen[1].Last()["content"]!, Does.StartWith("Refused by a safety rule, so it was NOT done: ").And.Contain("outside the allowed folder"), "the model sees who refused and that nothing happened");
        Assert.That((string)((JArray)o["toolCalls"]!)[0]["by"]!, Is.EqualTo("rule"));
    }

    [Test]
    public async Task AnUnknownOrUnselectedTool_IsAFailure_ListingWhatIsAvailable()
    {
        _turns.Enqueue(Call(("delete_everything", new { }), (ToolIds.RunScript, new { code = "echo hi" })));
        _turns.Enqueue(Say("ok"));
        var o = await _sut.RunAsync(Req(), null, default);
        var calls = ((JArray)o["toolCalls"]!).Cast<JObject>().ToList();
        Assert.That(calls.Select(c => (string)c["decision"]!), Is.EqualTo(new[] { "failed", "failed" }), "run_script was not selected");
        Assert.That((string)calls[0]["reason"]!, Does.Contain("read_file").And.Contain("write_file"));
    }

    [Test]
    public async Task BadArgumentsAndMissingFiles_AreFailuresTheModelCanFix()
    {
        _turns.Enqueue(Call((ToolIds.ReadFile, new { }), (ToolIds.ReadFile, new { path = "nope.txt" })));
        _turns.Enqueue(Say("ok"));
        var o = await _sut.RunAsync(Req(), null, default);
        var calls = ((JArray)o["toolCalls"]!).Cast<JObject>().ToList();
        Assert.That(calls.Select(c => (string)c["decision"]!), Is.EqualTo(new[] { "failed", "failed" }));
        Assert.That((string)_messagesSeen[1][^1]["content"]!, Does.StartWith("error: "));
        Assert.That(_messagesSeen[1].Count(m => (string)m["role"]! == "tool"), Is.EqualTo(2), "every call gets its own tool message");
    }

    [Test]
    public void ARunawayModel_StopsAtMaxSteps_WithAClearMessage()
    {
        for (var i = 0; i < 5; i++) _turns.Enqueue(Call((ToolIds.ReadFile, new { path = "x.txt" })));
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => _sut.RunAsync(Req(maxSteps: 3), null, default));
        Assert.That(ex!.Message, Does.Contain("after 3 steps").And.Contain("read_file"));
        Assert.That(_messagesSeen, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task AnAnswerOnTheLastStep_IsStillAccepted()
    {
        _turns.Enqueue(Call((ToolIds.ReadFile, new { path = "x.txt" })));
        _turns.Enqueue(Say("fine"));
        var o = await _sut.RunAsync(Req(maxSteps: 2), null, default);
        Assert.That((string)o["text"]!, Is.EqualTo("fine"));
    }

    [Test]
    public void AToolThatDoesNotExist_InTheSelection_IsAnArgumentError() =>
        Assert.ThrowsAsync<ArgumentException>(() => _sut.RunAsync(Req(["format_disk"]), null, default));

    // ---- the approver
    private void ApproverSays(string text) =>
        _ollama.ChatAsync(default!, default!, default, default, default).ReturnsForAnyArgs(Task.FromResult(new OllamaChatResult(text, null, "small", true, "stop", 1, 1, 1)));

    [Test]
    public async Task Approver_Approves_TheToolRuns_AndSeesTheTaskToolAndArguments()
    {
        ApproverSays("{\"approve\": true, \"reason\": \"fine\"}");
        _turns.Enqueue(Call((ToolIds.WriteFile, new { path = "a.txt", content = "hello" })));
        _turns.Enqueue(Say("done"));
        var o = await _sut.RunAsync(Req(approver: "small"), null, default);
        Assert.That((string)((JArray)o["toolCalls"]!)[0]["decision"]!, Is.EqualTo("ran"));
        await _ollama.Received(1).ChatAsync(Arg.Any<Connection>(),
            Arg.Is<OllamaChatRequest>(q => q.Model == "small" && q.JsonMode && q.Think == false && q.Prompt.Contains("write hello to a.txt") && q.Prompt.Contains("write_file") && q.Prompt.Contains("a.txt")),
            Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>(), Arg.Any<Action<string, string>?>());
    }

    [Test]
    public async Task Approver_Rejects_TheToolDoesNotRun_AndTheReasonGoesToTheModel()
    {
        ApproverSays("{\"approve\": false, \"reason\": \"not what the task asked\"}");
        _turns.Enqueue(Call((ToolIds.WriteFile, new { path = "a.txt", content = "hello" })));
        _turns.Enqueue(Say("ok, I will not write"));
        var o = await _sut.RunAsync(Req(approver: "small"), null, default);
        Assert.That(File.Exists(Path.Combine(_root, "a.txt")), Is.False);
        var call = (JObject)((JArray)o["toolCalls"]!)[0];
        Assert.That((string)call["decision"]!, Is.EqualTo("rejected"));
        Assert.That((string)call["reason"]!, Is.EqualTo("not what the task asked"), "the reason stays the raw text");
        Assert.That((string)_messagesSeen[1][^1]["content"]!, Does.StartWith("A reviewer refused this call, so it was NOT done.").And.Contain("not what the task asked").And.Contain("Do not say it was done"));
        Assert.That((string)call["by"]!, Is.EqualTo("approver"));
    }

    [TestCase("I think it is fine!")]
    [TestCase("")]
    [TestCase("{\"approve\": \"yes\"}")]
    [TestCase("{\"reason\": \"no verdict\"}")]
    [TestCase("[true]")]
    public async Task Approver_GarbledOrUnclearAnswer_CountsAsRejected(string answer)
    {
        ApproverSays(answer);
        _turns.Enqueue(Call((ToolIds.WriteFile, new { path = "a.txt", content = "hello" })));
        _turns.Enqueue(Say("done"));
        var o = await _sut.RunAsync(Req(approver: "small"), null, default);
        Assert.That(File.Exists(Path.Combine(_root, "a.txt")), Is.False);
        Assert.That((string)((JArray)o["toolCalls"]!)[0]["decision"]!, Is.EqualTo("rejected"));
    }

    [Test]
    public async Task Approver_NotReachable_CountsAsRejected()
    {
        _ollama.ChatAsync(default!, default!, default, default, default).ReturnsForAnyArgs<Task<OllamaChatResult>>(_ => throw new InvalidOperationException("down"));
        _turns.Enqueue(Call((ToolIds.WriteFile, new { path = "a.txt", content = "hello" })));
        _turns.Enqueue(Say("done"));
        var o = await _sut.RunAsync(Req(approver: "small"), null, default);
        Assert.That((string)((JArray)o["toolCalls"]!)[0]["decision"]!, Is.EqualTo("rejected"));
        Assert.That((string)((JArray)o["toolCalls"]!)[0]["reason"]!, Does.Contain("could not be asked"));
    }

    [Test]
    public async Task Approver_IsNeverAsked_AboutACallThatBreaksTheRules()
    {
        ApproverSays("{\"approve\": true}");
        _turns.Enqueue(Call((ToolIds.WriteFile, new { path = "../evil.txt", content = "x" })));
        _turns.Enqueue(Say("done"));
        await _sut.RunAsync(Req(approver: "small"), null, default);
        await _ollama.DidNotReceiveWithAnyArgs().ChatAsync(default!, default!, default, default, default);
        Assert.That(File.Exists(Path.Combine(_base, "evil.txt")), Is.False, "an approver that says yes cannot widen the rules");
    }

    [Test]
    public async Task NoApproverSet_NobodyIsAsked()
    {
        _turns.Enqueue(Call((ToolIds.WriteFile, new { path = "a.txt", content = "hello" })));
        _turns.Enqueue(Say("done"));
        await _sut.RunAsync(Req(), null, default);
        await _ollama.DidNotReceiveWithAnyArgs().ChatAsync(default!, default!, default, default, default);
    }

    [Test]
    public async Task Progress_ShowsTheToolCallsSoFar()
    {
        _turns.Enqueue(Call((ToolIds.WriteFile, new { path = "a.txt", content = "hello" })));
        _turns.Enqueue(Say("done"));
        var seen = new List<JToken>();
        await _sut.RunAsync(Req(), seen.Add, default);
        Assert.That(seen.Any(s => ((JArray)s["toolCalls"]!).Count == 1 && (string)((JArray)s["toolCalls"]!)[0]["decision"]! == "ran"), Is.True);
    }

    // ---- the approver's instructions and what it is shown
    private OllamaChatRequest ApproverRequestOf(Func<ToolLoopRequest, ToolLoopRequest>? change = null)
    {
        ApproverSays("{\"approve\": true}");
        _turns.Enqueue(Call((ToolIds.WriteFile, new { path = "a.txt", content = "hello" })));
        _turns.Enqueue(Say("done"));
        var req = Req(approver: "small");
        _sut.RunAsync(change?.Invoke(req) ?? req, null, default).GetAwaiter().GetResult();
        return (OllamaChatRequest)_ollama.ReceivedCalls().First(c => c.GetMethodInfo().Name == nameof(IOllamaClient.ChatAsync)).GetArguments()[1]!;
    }

    [Test]
    public void Approver_ALongBrief_IsCut_AndTheToolCallComesFirst()
    {
        var brief = "You are the Technical Manager of a big organisation. " + new string('x', 5000) + " THE-END-OF-THE-BRIEF";
        var q = ApproverRequestOf(r => r with { Prompt = brief });
        Assert.That(q.Prompt, Does.Not.Contain("THE-END-OF-THE-BRIEF"));
        Assert.That(q.Prompt, Does.Contain("You are the Technical Manager").And.Contain("…"));
        Assert.That(q.Prompt.Length, Is.LessThan(ToolLoop.TaskExcerptChars + 600), "short, whatever the brief");
        Assert.That(q.Prompt.IndexOf("Tool: write_file", StringComparison.Ordinal), Is.LessThan(q.Prompt.IndexOf("You are the Technical Manager", StringComparison.Ordinal)), "the call is shown before the task");
        Assert.That(q.Prompt, Does.Contain("Arguments: {\"path\":\"a.txt\"").And.Contain($"Allowed folder: {_root}"));
    }

    [Test]
    public void Approver_AShortTask_IsGivenWhole_WithoutAnEllipsis()
    {
        var q = ApproverRequestOf();
        Assert.That(q.Prompt, Does.Contain("write hello to a.txt"));
        Assert.That(q.Prompt, Does.Not.Contain("…"));
    }

    [Test]
    public void Approver_DefaultInstructions_AreSafetyOnly_WithBothExamples_AndTheReplyFormat()
    {
        var q = ApproverRequestOf();
        Assert.That(q.System, Does.StartWith(ToolLoop.DefaultApproverInstructions));
        Assert.That(q.System, Does.Contain("Judge ONLY").And.Contain("Do NOT judge whether the work is complete").And.Contain("If none of the three applies, approve"));
        Assert.That(q.System, Does.Contain("Example 1").And.Contain("\"approve\": true}").And.Contain("Example 2").And.Contain("\"approve\": false}"));
        Assert.That(q.System, Does.Contain("\"reason\": \"Writes the requested file inside the folder.\", \"approve\": true"), "the examples show the reason before the verdict");
        Assert.That(q.System, Does.EndWith("Reply with JSON only, the reason first: {\"reason\": \"why, in one short sentence\", \"approve\": true or false}."));
        Assert.That(q.JsonMode && q.Think == false && q.Temperature == 0, Is.True);
    }

    [Test]
    public void Approver_CustomInstructions_ReplaceThePolicy_ButTheReplyFormatIsStillAdded()
    {
        var q = ApproverRequestOf(r => r with { ApproverInstructions = "  Only approve writes to .txt files.  " });
        Assert.That(q.System, Does.StartWith("Only approve writes to .txt files."));
        Assert.That(q.System, Does.Not.Contain("Judge ONLY").And.Not.Contain("Example 1"));
        Assert.That(q.System, Does.EndWith("\"approve\": true or false}."));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   \n ")]
    public void Approver_EmptyCustomInstructions_MeanTheDefault(string? custom)
    {
        var q = ApproverRequestOf(r => r with { ApproverInstructions = custom });
        Assert.That(q.System, Does.StartWith(ToolLoop.DefaultApproverInstructions));
    }

    [Test]
    public async Task ToolFailure_KeepsTheErrorWording_AndHasNoBy()
    {
        _turns.Enqueue(Call((ToolIds.ReadFile, new { path = "nope.txt" })));
        _turns.Enqueue(Say("ok"));
        var o = await _sut.RunAsync(Req(), null, default);
        var call = (JObject)((JArray)o["toolCalls"]!)[0];
        Assert.That(call["by"], Is.Null);
        Assert.That((string)_messagesSeen[1][^1]["content"]!, Does.StartWith("error: "));
    }

    [Test]
    public async Task ARunCallThatWasRefusedByTheApprover_IsNotMarkedAsDone_AndANotApprovedUnreadableAnswerSaysSo()
    {
        ApproverSays("not json at all");
        _turns.Enqueue(Call((ToolIds.WriteFile, new { path = "a.txt", content = "x" })));
        _turns.Enqueue(Say("ok"));
        var o = await _sut.RunAsync(Req(approver: "small"), null, default);
        var call = (JObject)((JArray)o["toolCalls"]!)[0];
        Assert.That((string)call["by"]!, Is.EqualTo("approver"));
        Assert.That((string)call["reason"]!, Is.EqualTo("The approver's answer could not be read."));
        Assert.That((string)_messagesSeen[1][^1]["content"]!, Does.Contain("was NOT done"));
    }
}
