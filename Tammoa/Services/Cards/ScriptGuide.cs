using Newtonsoft.Json.Linq;
using Tammoa.Models;

namespace Tammoa.Services.Cards;

/// <summary>The Script card's tutorial and per-language hints (kept apart so the card file stays readable).
/// Every example here is executed by the test suite, so the tutorial cannot silently go stale.</summary>
public static class ScriptGuide
{
    public static readonly IReadOnlyDictionary<string, string> CodeHelp = new Dictionary<string, string>
    {
        ["shell"] = "Read the input: {{input.stdout | sh}} (a template, filled in before the command runs) or $TAMMOA_INPUT (the whole input as JSON text). A Data card: {{data.Style | sh}}. Print your result.",
        ["javascript"] = "Read the input: input.json.name   ·   the Run input: runInput   ·   a Data card: data.Settings.city   ·   hand data on: console.log(JSON.stringify({ ok: true }))",
        ["python"] = "Read the input: input_data[\"json\"][\"name\"]   ·   the Run input: run_input   ·   a Data card: data[\"Settings\"][\"city\"]   ·   hand data on: print(json.dumps({\"ok\": True}))",
    };

    public static readonly IReadOnlyDictionary<string, string> CodePlaceholders = new Dictionary<string, string>
    {
        ["shell"] = "echo \"hello\"",
        ["javascript"] = """
// input = the output of the previous card, runInput = the Run input
const name = input.json?.name ?? 'world'
console.log(JSON.stringify({ greeting: 'Hello ' + name }))
""",
        ["python"] = """
# input_data = the output of the previous card, run_input = the Run input
import json
name = (input_data.get('json') or {}).get('name', 'world')
print(json.dumps({'greeting': 'Hello ' + name}))
""",
    };

    private const string JsGreeting = """
// 'input' is the output of the previous card
const name = input.json.name
console.log(JSON.stringify({ greeting: 'Hello ' + name }))
""";

    private const string JsRunInput = """
// 'runInput' is whatever was typed in the Run input box
const text = String(runInput)
console.log(JSON.stringify({ shout: text.toUpperCase(), length: text.length }))
""";

    private const string JsExplore = """
// Not sure what is in 'input'? Print it, then look at the Output tab.
console.log(Object.keys(input))
console.log(JSON.stringify(input, null, 2))
""";

    private const string JsLogs = """
// Progress lines are fine. The LAST line is used as JSON when the whole output is not JSON.
console.log('loading...')
const items = input.json.items
console.log('found ' + items.length + ' items')
console.log(JSON.stringify({ count: items.length, first: items[0] }))
""";

    private const string PyGreeting = """
import json
# 'input_data' is the output of the previous card
name = input_data["json"]["name"]
print(json.dumps({"greeting": "Hello " + name}))
""";

    private const string PyTotal = """
import json
items = input_data["json"]["items"]
total = sum(item["price"] * item["qty"] for item in items)
print(json.dumps({"total": total, "count": len(items)}))
""";

    private const string PyRunInput = """
import json
# 'run_input' is whatever was typed in the Run input box
print(json.dumps({"words": len(str(run_input).split())}))
""";

    private const string JsData = """
// 'data' holds all your Data cards: data.Settings.city (a JSON Data card) or data.Style (a text one)
const city = data.Settings.city
console.log(JSON.stringify({ message: 'Weather for ' + city }))
""";

    private const string PyData = """
import json
# 'data' holds all your Data cards: data["Settings"]["city"] (a JSON Data card) or data["Style"] (a text one)
print(json.dumps({"message": "Weather for " + data["Settings"]["city"]}))
""";

    private const string ShTemplate = "echo Hello {{input.json.name | sh}}";

    private const string ShEnv = """
# $TAMMOA_INPUT is the whole input as JSON text; $TAMMOA_INPUT_FILE is the same in a file
echo "$TAMMOA_INPUT" | wc -c
""";

    private const string ShStdin = "wc -l";

    private static JObject Js(string code) => new() { ["language"] = "javascript", ["command"] = code };
    private static JObject Py(string code) => new() { ["language"] = "python", ["command"] = code };
    private static JObject Sh(string code) => new() { ["language"] = "shell", ["command"] = code };

    public static CardGuide Create() => new(
        "Runs your own code: Shell, JavaScript (Node) or Python. Use it for anything the other cards cannot do: calculations, text processing, calling a program.",
        "The card receives the output of the previous card. In JavaScript it is the ready-made variable input, in Python input_data. The Run input is runInput / run_input. In shell use {{input.field | sh}} templates or $TAMMOA_INPUT. Open the Input tab to see exactly what your code receives.",
        new JObject
        {
            ["stdout"] = "{\"greeting\":\"Hello Sok\"}\n",
            ["stderr"] = "",
            ["exitCode"] = 0,
            ["timedOut"] = false,
            ["truncated"] = false,
            ["json"] = new JObject { ["greeting"] = "Hello Sok" },
        },
        [
            new GuideExample("JavaScript: read a value and hand data on", "input is the previous card's output. Print JSON and the next card gets it as fields.",
                Js(JsGreeting), JsGreeting, "javascript", new JObject { ["json"] = new JObject { ["name"] = "Sok" } },
                "Output has json: { greeting: \"Hello Sok\" }. The next card reads {{input.json.greeting}} (plain printed text is {{input.stdout}})."),
            new GuideExample("JavaScript: use the Run input", "runInput holds what was typed in the Run input box (text, or JSON turned into data).",
                Js(JsRunInput), JsRunInput, "javascript", RunInput: "hello",
                Result: "With Run input hello: json { shout: \"HELLO\", length: 5 }."),
            new GuideExample("JavaScript: see everything in the input", "Print the input to discover its fields, then use them.",
                Js(JsExplore), JsExplore, "javascript", new JObject { ["json"] = new JObject { ["name"] = "Sok" }, ["stdout"] = "x" },
                "stdout lists the names of the fields (json, stdout) followed by the whole input as indented JSON."),
            new GuideExample("JavaScript: progress logs, then a JSON result", "Log as much as you like; finish with one line of JSON.",
                Js(JsLogs), JsLogs, "javascript", new JObject { ["json"] = new JObject { ["items"] = new JArray("a", "b", "c") } },
                "json { count: 3, first: \"a\" } even though earlier lines are plain text."),
            new GuideExample("Python: read a value and hand data on", "input_data is the previous card's output. (Python's own input() is untouched.)",
                Py(PyGreeting), PyGreeting, "python", new JObject { ["json"] = new JObject { ["name"] = "Sok" } },
                "json { greeting: \"Hello Sok\" } → next card: {{input.json.greeting}}."),
            new GuideExample("Python: a calculation over a list", "Lists arrive as Python lists, objects as dicts.",
                Py(PyTotal), PyTotal, "python", new JObject { ["json"] = new JObject { ["items"] = new JArray(new JObject { ["price"] = 2.5, ["qty"] = 2 }, new JObject { ["price"] = 10, ["qty"] = 1 }) } },
                "json { total: 15.0, count: 2 }."),
            new GuideExample("Python: use the Run input", "run_input is what was typed in the Run input box.",
                Py(PyRunInput), PyRunInput, "python", RunInput: "the quick brown fox", Result: "json { words: 4 }."),
            new GuideExample("JavaScript: use a Data card", "Data cards (reusable stored text) are in the variable data, by the card's name. No wiring and no trigger needed.",
                Js(JsData), JsData, "javascript", SampleData: new JObject { ["Settings"] = new JObject { ["city"] = "Phnom Penh" } },
                Result: "json { message: \"Weather for Phnom Penh\" }. Name a Data card Settings and store {\"city\": \"Phnom Penh\"} in it."),
            new GuideExample("Python: use a Data card", "The same in Python: data[\"Name\"].",
                Py(PyData), PyData, "python", SampleData: new JObject { ["Settings"] = new JObject { ["city"] = "Phnom Penh" } },
                Result: "json { message: \"Weather for Phnom Penh\" }."),
            new GuideExample("Shell: put a value into a command", "{{ }} is filled in BEFORE the command runs. Add | sh so spaces and quotes cannot break it.",
                Sh(ShTemplate), ShTemplate, "shell", new JObject { ["json"] = new JObject { ["name"] = "Sok Dara" } },
                "The command that runs is: echo Hello 'Sok Dara'. stdout is \"Hello Sok Dara\\n\"."),
            new GuideExample("Shell: the whole input as JSON text", "$TAMMOA_INPUT holds the input as JSON text (and $TAMMOA_INPUT_FILE the same in a file), handy for tools such as jq.",
                Sh(ShEnv), ShEnv, "shell", new JObject { ["json"] = new JObject { ["a"] = 1 } },
                "stdout is the number of characters of the input JSON."),
            new GuideExample("Shell: feed text to a program", "Set Standard input (below the code) to {{input.stdout}}: the program reads it on stdin.",
                new JObject { ["language"] = "shell", ["command"] = ShStdin, ["stdin"] = "{{input.stdout}}" }, ShStdin + "\n# Standard input: {{input.stdout}}", "shell",
                new JObject { ["stdout"] = "one\ntwo\nthree\n" }, "stdout \"3\" (the number of lines)."),
        ],
        [
            "Names to remember: JavaScript input, runInput and data · Python input_data, run_input and data · Shell $TAMMOA_INPUT and {{input.x | sh}} (and {{data.Name | sh}}).",
            "Hand data to the next card by printing JSON (the last line is used if earlier lines are plain text): the next card reads {{input.json.field}}. Plain text is {{input.stdout}}.",
            "A script with no card connected before it has no input (null). Connect the previous card, run once, and open the Input tab to see the real fields.",
            "JavaScript is CommonJS: use require('fs'), not import. A top-level return is fine. Errors show the line number of YOUR code.",
            "Needs Node.js (JavaScript) or python3 (Python) installed; the Interpreter setting lets you point at a specific one.",
            "A non-zero exit code makes the card fail, unless you switch off \"Fail on non-zero exit\".",
        ]);
}
