using Newtonsoft.Json.Linq;

namespace Tammoa.Services;

public class FlowDefinitionValidator : IFlowDefinitionValidator
{
    public const int MaxNodes = 200;
    public const int MaxIdLength = 64;
    public const int MaxNameLength = 100;

    private readonly ICardRegistry _cards;

    public FlowDefinitionValidator(ICardRegistry cards) => _cards = cards;

    public void Validate(JObject definition)
    {
        var nodes = definition["nodes"] switch
        {
            null => new JArray(),
            JArray a => a,
            _ => throw new ArgumentException("definition.nodes must be an array."),
        };
        var edges = definition["edges"] switch
        {
            null => new JArray(),
            JArray a => a,
            _ => throw new ArgumentException("definition.edges must be an array."),
        };
        if (nodes.Count > MaxNodes) throw new ArgumentException($"A flow is limited to {MaxNodes} cards.");

        var triggers = new HashSet<string>();
        var handles = new Dictionary<string, HashSet<string>>(); // card id -> its output handle keys
        var ids = new HashSet<string>();
        foreach (var token in nodes)
        {
            if (token is not JObject node) throw new ArgumentException("Every card must be an object.");
            var id = node["id"]?.Type == JTokenType.String ? (string)node["id"]! : "";
            if (id.Length == 0 || id.Length > MaxIdLength) throw new ArgumentException($"Every card needs an id of 1 to {MaxIdLength} characters.");
            if (!ids.Add(id)) throw new ArgumentException($"Duplicate card id '{id}'.");

            var type = node["type"]?.Type == JTokenType.String ? (string)node["type"]! : "";
            var card = _cards.Find(type) ?? throw new ArgumentException($"Unknown card type '{type}'.");
            if (card.Info.IsTrigger) triggers.Add(id);
            handles[id] = card.Info.Outputs.Select(o => o.Key).ToHashSet();

            if (node["name"] is { Type: not JTokenType.Null } name && (name.Type != JTokenType.String || ((string)name!).Length > MaxNameLength))
                throw new ArgumentException($"A card name must be text of at most {MaxNameLength} characters.");
            if (node["params"] is { Type: not JTokenType.Null } p && p is not JObject)
                throw new ArgumentException($"Card '{id}': params must be an object.");
            if (node["position"] is { Type: not JTokenType.Null } pos &&
                !(pos is JObject po && po["x"]?.Type is JTokenType.Integer or JTokenType.Float && po["y"]?.Type is JTokenType.Integer or JTokenType.Float))
                throw new ArgumentException($"Card '{id}': position must be {{ x, y }} numbers.");
        }

        // A Data card is referred to by its name ({{data.Name}}): the name must be usable in a reference and unique in the flow.
        var dataNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes.OfType<JObject>().Where(n => (string?)n["type"] == Cards.DataStoreCard.TypeId))
        {
            var name = ((string?)node["name"])?.Trim();
            if (Helper.DataNames.Problem(name) is { } problem) throw new ArgumentException(problem);
            if (!dataNames.Add(name!)) throw new ArgumentException($"Two Data cards are called '{name}'. Each needs its own name.");
            if (node["params"] is JObject p && p["scope"] is { Type: not JTokenType.Null } scope && !Models.DataScope.IsValid((string?)scope))
                throw new ArgumentException($"Data card '{name}': unknown scope.");
        }

        var links = new HashSet<(string, string, string)>();
        var children = ids.ToDictionary(i => i, _ => new List<string>());
        foreach (var token in edges)
        {
            if (token is not JObject edge) throw new ArgumentException("Every connection must be an object.");
            var source = edge["source"]?.Type == JTokenType.String ? (string)edge["source"]! : "";
            var target = edge["target"]?.Type == JTokenType.String ? (string)edge["target"]! : "";
            if (!ids.Contains(source) || !ids.Contains(target)) throw new ArgumentException("A connection points at a card that does not exist.");
            if (source == target) throw new ArgumentException("A card cannot connect to itself.");
            var handle = edge["sourceHandle"] is { Type: not JTokenType.Null } h ? (h.Type == JTokenType.String ? (string)h! : "") : "main";
            if (!handles[source].Contains(handle)) throw new ArgumentException($"A connection starts from an output ('{handle}') that card '{source}' does not have.");
            if (!links.Add((source, handle, target))) throw new ArgumentException("Two cards are connected twice.");
            if (triggers.Contains(target)) throw new ArgumentException("A trigger card cannot have an incoming connection.");
            children[source].Add(target);
        }

        // TODO(loop card): cycles are rejected until the loop card defines how repetition works.
        if (HasCycle(children)) throw new ArgumentException("Loops are not supported yet: the connections form a cycle.");
    }

    private static bool HasCycle(Dictionary<string, List<string>> children)
    {
        var state = new Dictionary<string, int>(); // 1 = visiting, 2 = done
        bool Visit(string id)
        {
            if (state.TryGetValue(id, out var s)) return s == 1;
            state[id] = 1;
            foreach (var c in children[id]) if (Visit(c)) return true;
            state[id] = 2;
            return false;
        }
        return children.Keys.Any(Visit);
    }
}
