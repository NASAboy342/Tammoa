using Newtonsoft.Json.Linq;
using NSubstitute;
using Tammoa.Enums;
using Tammoa.Helper;
using Tammoa.Models;
using Tammoa.Services;
using Tammoa.Services.Cards;

namespace Tammoa.Test.Services;

/// <summary>The tutorials are data the UI shows verbatim, so they must be valid and TRUE: every local example is run for real.</summary>
public class CardGuidesTest
{
    private static readonly TemplateEngine T = new();

    private static ICardType[] AllCards() =>
    [
        new ManualTriggerCard(), new SetValueCard(T), new ScriptCard(T, new ProcessRunner()), new IfCard(T), new WaitCard(),
        new HttpRequestCard(T, Substitute.For<IHttpRequestRunner>()), new JsonCard(T), new MergeCard(), new ReadFileCard(T), new WriteFileCard(T),
        new OllamaCard(T, Substitute.For<IConnectionService>(), Substitute.For<IOllamaClient>()),
    ];

    private static bool Found(string exe)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/sh", ["-c", $"command -v {exe}"]) { RedirectStandardOutput = true, UseShellExecute = false })!;
        p.WaitForExit(3000);
        return p.ExitCode == 0;
    }

    private static IEnumerable<(ICardType Card, GuideExample Example)> Examples() =>
        AllCards().SelectMany(c => c.Info.Guide!.Examples.Select(e => (c, e)));

    // ---- structure
    [Test]
    public void EveryCard_HasACompleteGuide()
    {
        foreach (var c in AllCards())
        {
            var g = c.Info.Guide;
            Assert.That(g, Is.Not.Null, c.Info.Type);
            Assert.That(g!.Overview, Is.Not.Empty, $"{c.Info.Type} overview");
            Assert.That(g.InputNote, Is.Not.Empty, $"{c.Info.Type} input note");
            Assert.That(g.OutputExample, Is.Not.Null, $"{c.Info.Type} output example");
            Assert.That(g.Examples, Is.Not.Empty, $"{c.Info.Type} examples");
            Assert.That(g.Examples.Select(e => e.Title), Is.Unique, $"{c.Info.Type} example titles");
            foreach (var e in g.Examples) Assert.That(e.Description, Is.Not.Empty, $"{c.Info.Type}: {e.Title}");
            Assert.That(g.OutputExample, Is.InstanceOf<JObject>().Or.InstanceOf<JArray>(), $"{c.Info.Type}: the output example must be browsable data");
        }
    }

    [Test]
    public void ExampleSettings_AreRealSettingsOfThatCard_WithValidChoices()
    {
        foreach (var (card, ex) in Examples())
        {
            if (ex.Params is null) continue;
            foreach (var p in ex.Params.Properties())
            {
                var param = card.Info.Params.SingleOrDefault(x => x.Key == p.Name);
                Assert.That(param, Is.Not.Null, $"{card.Info.Type} / {ex.Title}: '{p.Name}' is not a setting of this card");
                if (param!.Kind == ParamKind.Select && param.Options is not null)
                    Assert.That(param.Options.Select(o => o.Value), Does.Contain((string?)p.Value), $"{card.Info.Type} / {ex.Title}: '{p.Value}' is not an option of {p.Name}");
                if (param.Kind == ParamKind.Bool) Assert.That(p.Value.Type, Is.EqualTo(JTokenType.Boolean), $"{ex.Title}: {p.Name}");
            }
        }
    }

    [Test]
    public void Examples_ThatShowCode_ShowTheCodeTheyApply()
    {
        foreach (var (card, ex) in Examples().Where(x => x.Card.Info.Type == "script.shell"))
            Assert.That(ex.Code, Does.Contain((string)ex.Params!["command"]!).Or.Contain((string)ex.Params["command"]!.ToString().Trim()), ex.Title);
    }

    [Test]
    public void TheScriptCard_CoversAllThreeLanguages_WithHintsAndStarters()
    {
        var script = AllCards().Single(c => c.Info.Type == "script.shell").Info;
        var code = script.Params.Single(p => p.Key == "command");
        foreach (var lang in new[] { "shell", "javascript", "python" })
        {
            Assert.That(code.CodeHelp![lang], Is.Not.Empty, lang);
            Assert.That(code.CodePlaceholders![lang], Is.Not.Empty, lang);
            Assert.That(script.Guide!.Examples.Count(e => (string?)e.Params?["language"] == lang), Is.GreaterThanOrEqualTo(2), $"examples for {lang}");
        }
        Assert.That(code.CodeHelp!["javascript"], Does.Contain("input.json").And.Contain("runInput"));
        Assert.That(code.CodeHelp["python"], Does.Contain("input_data").And.Contain("run_input"));
        Assert.That(code.CodeHelp["shell"], Does.Contain("TAMMOA_INPUT"));
    }

    [Test]
    public void TheGuide_TravelsToTheUi_AsCamelCaseJson()
    {
        var json = JObject.Parse(JsonHelper.Serialize(AllCards().Single(c => c.Info.Type == "script.shell").Info));
        Assert.That(json["guide"]!["overview"], Is.Not.Null);
        Assert.That(json["guide"]!["outputExample"]!["json"]!["greeting"], Is.Not.Null);
        Assert.That(json["guide"]!["examples"]![0]!["sampleInput"], Is.Not.Null);
        Assert.That(json["params"]!.Single(p => (string?)p["key"] == "command")["codeHelp"]!["javascript"], Is.Not.Null);
        Assert.That(json["params"]!.Single(p => (string?)p["key"] == "command")["codePlaceholders"]!["python"], Is.Not.Null);
    }

    // ---- the examples are TRUE: run them
    private static CardExecutionContext ContextFor(GuideExample ex, JObject? settings = null) =>
        new(settings ?? (ex.Params is null ? new JObject() : (JObject)ex.Params.DeepClone()), ex.SampleInput?.DeepClone(), ex.RunInput?.DeepClone());

    [Test]
    public async Task EveryScriptExample_RunsAndDoesWhatItSays()
    {
        var card = AllCards().Single(c => c.Info.Type == "script.shell");
        foreach (var ex in card.Info.Guide!.Examples)
        {
            var lang = (string)ex.Params!["language"]!;
            if (lang == "javascript" && !Found("node")) continue;
            if (lang == "python" && !Found("python3")) continue;
            var r = (await card.ExecuteAsync(ContextFor(ex), default)).Output;
            Assert.That((int)r["exitCode"]!, Is.EqualTo(0), ex.Title);
            switch (ex.Title)
            {
                case "JavaScript: read a value and hand data on":
                case "Python: read a value and hand data on":
                    Assert.That((string?)r["json"]!["greeting"], Is.EqualTo("Hello Sok"), ex.Title); break;
                case "JavaScript: use the Run input":
                    Assert.That((string?)r["json"]!["shout"], Is.EqualTo("HELLO")); Assert.That((int)r["json"]!["length"]!, Is.EqualTo(5)); break;
                case "JavaScript: see everything in the input":
                    Assert.That((string?)r["stdout"], Does.Contain("json").And.Contain("stdout")); break;
                case "JavaScript: progress logs, then a JSON result":
                    Assert.That((int)r["json"]!["count"]!, Is.EqualTo(3)); Assert.That((string?)r["json"]!["first"], Is.EqualTo("a")); break;
                case "Python: a calculation over a list":
                    Assert.That((double)r["json"]!["total"]!, Is.EqualTo(15)); Assert.That((int)r["json"]!["count"]!, Is.EqualTo(2)); break;
                case "Python: use the Run input":
                    Assert.That((int)r["json"]!["words"]!, Is.EqualTo(4)); break;
                case "Shell: put a value into a command":
                    Assert.That(((string?)r["stdout"])!.Trim(), Is.EqualTo("Hello Sok Dara")); break;
                case "Shell: the whole input as JSON text":
                    Assert.That(int.Parse(((string?)r["stdout"])!.Trim()), Is.GreaterThan(5)); break;
                case "Shell: feed text to a program":
                    Assert.That(((string?)r["stdout"])!.Trim(), Is.EqualTo("3")); break;
                default:
                    Assert.Fail($"No expectation written for the example '{ex.Title}'. Add one so the tutorial stays true.");
                    break;
            }
        }
    }

    [Test]
    public async Task IfExamples_TakeTheTrueBranch_AsTheyClaim()
    {
        var card = AllCards().Single(c => c.Info.Type == "logic.if");
        foreach (var ex in card.Info.Guide!.Examples)
        {
            var r = await card.ExecuteAsync(ContextFor(ex), default);
            Assert.That(r.Branch, Is.EqualTo("true"), ex.Title);
            Assert.That(JToken.DeepEquals(r.Output, ex.SampleInput), Is.True, $"{ex.Title}: the input is passed on unchanged");
        }
    }

    [Test]
    public async Task SetValueExamples_ProduceWhatTheyClaim()
    {
        var card = AllCards().Single(c => c.Info.Type == "data.set");
        var byTitle = card.Info.Guide!.Examples.ToDictionary(e => e.Title);
        var msg = (await card.ExecuteAsync(ContextFor(byTitle["Build a message"]), default)).Output;
        Assert.That((string?)msg["message"], Is.EqualTo("Hello Sok, you have 3 new items"));
        var typed = (await card.ExecuteAsync(ContextFor(byTitle["Keep a number a number"]), default)).Output;
        Assert.That(typed["total"]!.Type, Is.EqualTo(JTokenType.Integer));
        var run = (await card.ExecuteAsync(ContextFor(byTitle["Use the Run input"]), default)).Output;
        Assert.That((string?)run["question"], Is.EqualTo("What is the weather?"));
    }

    [Test]
    public async Task JsonAndMergeAndWaitExamples_ProduceWhatTheyClaim()
    {
        var json = AllCards().Single(c => c.Info.Type == "data.json");
        var examples = json.Info.Guide!.Examples.ToDictionary(e => e.Title);
        var parsed = (await json.ExecuteAsync(ContextFor(examples["Parse what a script printed"]), default)).Output;
        Assert.That((string?)parsed["name"], Is.EqualTo("Sok"));
        Assert.That(parsed["tags"], Is.InstanceOf<JArray>());
        var text = (await json.ExecuteAsync(ContextFor(examples["Stringify some data"]), default)).Output;
        Assert.That((string?)text["text"], Does.Contain("\"name\"").And.Contain("\n"));

        var merge = AllCards().Single(c => c.Info.Type == "data.merge");
        var merged = (await merge.ExecuteAsync(ContextFor(merge.Info.Guide!.Examples[0]), default)).Output;
        Assert.That(merged.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("{\"name\":\"Sok\",\"city\":\"Phnom Penh\"}"));

        var wait = AllCards().Single(c => c.Info.Type == "flow.wait");
        var ex = wait.Info.Guide!.Examples[0];
        var settings = new JObject { ["seconds"] = 0 }; // same shape as the example, without waiting 5 s in a test
        var passed = (await wait.ExecuteAsync(ContextFor(ex, settings), default)).Output;
        Assert.That((int)passed["json"]!["id"]!, Is.EqualTo(7));
        Assert.That((int)ex.Params!["seconds"]!, Is.EqualTo(5));
    }

    // ---- the documented output fields exist in the real output
    [Test]
    public async Task OutputExamples_OfTriggerAndScript_ListTheRealFields()
    {
        var trigger = AllCards().Single(c => c.Info.Type == "trigger.manual");
        var real = (await trigger.ExecuteAsync(new CardExecutionContext(new JObject(), null, "x"), default)).Output;
        Assert.That(((JObject)trigger.Info.Guide!.OutputExample!).Properties().Select(p => p.Name), Is.EquivalentTo(((JObject)real).Properties().Select(p => p.Name)));

        var script = AllCards().Single(c => c.Info.Type == "script.shell");
        var run = (await script.ExecuteAsync(new CardExecutionContext(JObject.Parse("{\"command\":\"echo '{\\\"greeting\\\":\\\"hi\\\"}'\"}"), null, null), default)).Output;
        var documented = ((JObject)script.Info.Guide!.OutputExample!).Properties().Select(p => p.Name);
        Assert.That(((JObject)run).Properties().Select(p => p.Name), Is.SupersetOf(documented), "every field the guide promises is really there");
    }

    [Test]
    public async Task DocumentedOutputFields_OfDataCards_MatchWhatTheyReturn()
    {
        var file = Path.Combine(Path.GetTempPath(), "tammoa-guide-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var write = AllCards().Single(c => c.Info.Type == "file.write");
            var written = (await write.ExecuteAsync(new CardExecutionContext(JObject.FromObject(new { path = file, content = "abc" }), null, null), default)).Output;
            Assert.That(((JObject)written).Properties().Select(p => p.Name), Is.EquivalentTo(((JObject)write.Info.Guide!.OutputExample!).Properties().Select(p => p.Name)));
            var read = AllCards().Single(c => c.Info.Type == "file.read");
            var got = (await read.ExecuteAsync(new CardExecutionContext(JObject.FromObject(new { path = file }), null, null), default)).Output;
            Assert.That(((JObject)got).Properties().Select(p => p.Name), Is.EquivalentTo(((JObject)read.Info.Guide!.OutputExample!).Properties().Select(p => p.Name)));
        }
        finally { File.Delete(file); }
    }
}
