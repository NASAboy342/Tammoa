using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Services.Cards;

namespace Tammoa.Services;

public class FlowTemplateService : IFlowTemplateService
{
    private readonly IFlowService _flows;
    private readonly IConnectionService _connections;

    public FlowTemplateService(IFlowService flows, IConnectionService connections)
    {
        _flows = flows;
        _connections = connections;
    }

    /// <summary>One template: what the list shows and the flow it makes. The definitions are plain JSON, checked by the same validator as every saved flow (a test creates every template).</summary>
    public record Template(string Id, string Name, string Description, string Icon, IReadOnlyList<string> Needs, string Definition)
    {
        public bool NeedsAi => Definition.Contains(OllamaCard.TypeId);
    }

    public static readonly IReadOnlyList<Template> Templates =
    [
        new("hello", "Hello, flow", "The smallest flow: a script greets whatever you type in Run input. No AI, nothing to set up.", "zap", [],
            """
            { "schemaVersion": 1,
              "nodes": [
                { "id": "start", "type": "trigger.manual", "name": "Start", "position": { "x": 0, "y": 0 }, "params": {} },
                { "id": "greet", "type": "script.shell", "name": "Greet", "position": { "x": 280, "y": 0 },
                  "params": { "language": "javascript", "command": "console.log(JSON.stringify({ message: 'Hello, ' + (runInput || 'world') + '!' }))" } }
              ],
              "edges": [ { "id": "e1", "source": "start", "target": "greet" } ] }
            """),
        new("branch", "Choose a path (IF)", "Type a word in Run input: if it contains \"yes\" one path runs, otherwise the other. Shows how a flow splits in two. No AI.", "git-branch", [],
            """
            { "schemaVersion": 1,
              "nodes": [
                { "id": "start", "type": "trigger.manual", "name": "Start", "position": { "x": 0, "y": 80 }, "params": {} },
                { "id": "check", "type": "logic.if", "name": "Said yes?", "position": { "x": 280, "y": 80 },
                  "params": { "value1": "{{run.input}}", "operator": "contains", "value2": "yes" } },
                { "id": "yes", "type": "data.set", "name": "Yes path", "position": { "x": 560, "y": 0 }, "params": { "values": "{ \"answer\": \"You said yes.\" }" } },
                { "id": "no", "type": "data.set", "name": "Other path", "position": { "x": 560, "y": 160 }, "params": { "values": "{ \"answer\": \"No yes found in: {{run.input}}\" }" } }
              ],
              "edges": [
                { "id": "e1", "source": "start", "target": "check" },
                { "id": "e2", "source": "check", "sourceHandle": "true", "target": "yes" },
                { "id": "e3", "source": "check", "sourceHandle": "false", "target": "no" }
              ] }
            """),
        new("web-api", "Call a web API", "Fetches a short quote from a public web API and shows it. Change the address to call your own API. Needs the internet, no AI.", "globe", ["Internet"],
            """
            { "schemaVersion": 1,
              "nodes": [
                { "id": "start", "type": "trigger.manual", "name": "Start", "position": { "x": 0, "y": 0 }, "params": {} },
                { "id": "call", "type": "net.http", "name": "Get a quote", "position": { "x": 280, "y": 0 },
                  "params": { "method": "GET", "url": "https://api.github.com/zen", "timeoutSeconds": 30, "failOnError": true } }
              ],
              "edges": [ { "id": "e1", "source": "start", "target": "call" } ] }
            """),
        new("summarize-text", "Summarize text with AI", "Paste text in Run input; a local Ollama model answers with a short summary. Needs a connection under Connections.", "bot", ["Ollama"],
            """
            { "schemaVersion": 1,
              "nodes": [
                { "id": "start", "type": "trigger.manual", "name": "Start", "position": { "x": 0, "y": 0 }, "params": {} },
                { "id": "summary", "type": "ai.ollama", "name": "Summarize", "position": { "x": 280, "y": 0 },
                  "params": { "connection": "", "model": "", "prompt": "Summarize this in three short bullet points:\n\n{{run.input}}", "think": "off", "timeoutSeconds": 300 } }
              ],
              "edges": [ { "id": "e1", "source": "start", "target": "summary" } ] }
            """),
        new("summarize-file", "Summarize a file", "Type the path of a text file in Run input (for example ~/notes/today.txt); the AI reads it and summarizes it. Needs a connection under Connections.", "file-text", ["Ollama"],
            """
            { "schemaVersion": 1,
              "nodes": [
                { "id": "start", "type": "trigger.manual", "name": "Start", "position": { "x": 0, "y": 0 }, "params": {} },
                { "id": "read", "type": "file.read", "name": "Read the file", "position": { "x": 280, "y": 0 }, "params": { "path": "{{run.input}}", "maxKilobytes": 256 } },
                { "id": "summary", "type": "ai.ollama", "name": "Summarize", "position": { "x": 560, "y": 0 },
                  "params": { "connection": "", "model": "", "prompt": "Summarize this text in a short paragraph:\n\n{{input.text}}", "think": "off", "timeoutSeconds": 300 } }
              ],
              "edges": [ { "id": "e1", "source": "start", "target": "read" }, { "id": "e2", "source": "read", "target": "summary" } ] }
            """),
    ];

    public IReadOnlyList<FlowTemplateInfo> List() =>
        Templates.Select(t => new FlowTemplateInfo(t.Id, t.Name, t.Description, t.Icon, t.NeedsAi, t.Needs, ((JArray)JObject.Parse(t.Definition)["nodes"]!).Count)).ToList();

    public Flow Create(string id, CreateFromTemplateRequest request)
    {
        var template = Templates.FirstOrDefault(t => t.Id == id) ?? throw new KeyNotFoundException($"There is no template '{id}'.");
        var definition = JObject.Parse(template.Definition);

        var connectionId = request.ConnectionId?.Trim();
        if (template.NeedsAi && !string.IsNullOrEmpty(connectionId))
        {
            Connection connection;
            try { connection = _connections.GetForUse(connectionId); }
            catch (KeyNotFoundException) { throw new ArgumentException("That connection no longer exists. Choose another."); }
            if (connection.Provider != ConnectionProvider.Ollama) throw new ArgumentException($"'{connection.Name}' is not an Ollama connection.");
            foreach (var node in ((JArray)definition["nodes"]!).OfType<JObject>().Where(n => (string?)n["type"] == OllamaCard.TypeId))
            {
                var p = (JObject)node["params"]!;
                p["connection"] = connection.Id;
                if (!string.IsNullOrWhiteSpace(request.Model)) p["model"] = request.Model.Trim();
            }
        }

        var created = _flows.Import(new FlowDocument(FlowService.DocumentFormat, FlowService.DocumentVersion, template.Name, template.Description, definition));
        return created;
    }
}
