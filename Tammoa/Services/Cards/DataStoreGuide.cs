using Newtonsoft.Json.Linq;
using Tammoa.Models;

namespace Tammoa.Services.Cards;

/// <summary>The Data card's tutorial. Its examples are executed by the test suite.</summary>
public static class DataStoreGuide
{
    public static CardGuide Create() => new(
        "Reusable data. Type some text once (a style guide, a list of settings, a template) and use it in any card, any number of times, without a trigger and without wiring it in. A flow can also write to it, for example to remember the last answer.",
        "A Data card needs no input. Other cards read it by its NAME: {{data.Name}} in text settings, `data.Name` in JavaScript, data[\"Name\"] in Python. If the text is a JSON object or list its fields work too: {{data.Settings.city}}. Only when you connect a card INTO a Data card (and choose Replace or Add) does it receive input, which it then saves.",
        new JObject { ["text"] = "Be brief and friendly.", ["name"] = "Style", ["scope"] = "flow", ["size"] = 22, ["changed"] = false },
        [
            new GuideExample("Keep a style guide for every AI card", "Name the card Style, type the text, press Save text. Any card can use it.",
                new JObject { ["mode"] = "keep" }, "Stored text:  Be brief and friendly.\n\nIn an Ollama card, System prompt:  {{data.Style}}", "text",
                SampleData: new JObject { ["Style"] = "Be brief and friendly." },
                Result: "{{data.Style}} is replaced by \"Be brief and friendly.\" wherever you write it. No trigger and no connection needed."),
            new GuideExample("Store settings as JSON and read single fields", "If the text is a JSON object, each field has its own reference.",
                new JObject { ["mode"] = "keep" }, "Stored text:\n{\n  \"city\": \"Phnom Penh\",\n  \"days\": 3\n}\n\nUse:  {{data.Settings.city}}  and  {{data.Settings.days}}", "json",
                SampleData: new JObject { ["Settings"] = new JObject { ["city"] = "Phnom Penh", ["days"] = 3 } },
                Result: "{{data.Settings.city}} is Phnom Penh; {{data.Settings.days}} is the number 3. Name this card Settings."),
            new GuideExample("Remember the last answer", "Connect a card into the Data card and choose Replace: the flow writes the data.",
                new JObject { ["mode"] = "replace", ["writeFrom"] = "{{input.text}}" }, "mode: Replace the text with what comes in\nWhat to save: {{input.text}}", "text",
                SampleInput: new JObject { ["text"] = "Two files were found." },
                Result: "The stored text becomes \"Two files were found.\" and the card outputs it. The next run (or another flow, if shared) can read {{data.Last answer}}."),
            new GuideExample("Keep a log", "Add makes a new line at the end every time the flow runs.",
                new JObject { ["mode"] = "append", ["writeFrom"] = "{{run.input}}" }, "mode: Add what comes in as a new line\nWhat to save: {{run.input}}", "text",
                SampleInput: new JObject { ["any"] = "value" }, RunInput: "checked at noon",
                Result: "\"checked at noon\" is added as a new line each run."),
            new GuideExample("Read it in a script", "Scripts get all Data cards in one variable: data.", Code: "// JavaScript\nconsole.log(JSON.stringify({ city: data.Settings.city }))\n\n# Python\nprint(data[\"Settings\"][\"city\"])", Language: "text",
                SampleData: new JObject { ["Settings"] = new JObject { ["city"] = "Phnom Penh" } },
                Result: "JavaScript: data.Settings.city  ·  Python: data[\"Settings\"][\"city\"]  ·  Shell: {{data.Settings.city | sh}}"),
        ],
        [
            "The card's NAME is the reference name: it must be unique in the flow and cannot contain . [ ] | { }. Renaming it changes how you refer to it.",
            "The text is a plain .txt file (shown under the editor), so you can also open it in any editor. Press Save text to store what you typed.",
            "Shared with All flows: every flow with a Data card of the same name uses the same text.",
            "Default is Keep: connecting a card to a Data card does nothing unless you choose Replace or Add, so you cannot overwrite it by accident.",
            "A Data card runs at the start of every run (no trigger needed) and outputs its text: connect it to another card if you prefer a wire to {{data.Name}}.",
            "Limit: 1 MB of text.",
        ]);
}
