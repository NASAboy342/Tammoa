using Newtonsoft.Json.Linq;
using Tammoa.Helper;
using Tammoa.Models;
using Tammoa.Repositories;
using Tammoa.Services.Cards;

namespace Tammoa.Services;

public class DataService : IDataService
{
    private readonly IDataRepository _repo;
    private readonly IFlowRepository _flows;
    private readonly IRunEventBus _bus;

    public DataService(IDataRepository repo, IFlowRepository flows, IRunEventBus bus)
    {
        _repo = repo;
        _flows = flows;
        _bus = bus;
    }

    public IReadOnlyList<DataCardSummary> List(string flowId)
    {
        var flow = GetFlow(flowId);
        return DataNodes(flow.Definition).Select(n =>
        {
            var loc = Location(flowId, n);
            var text = _repo.Read(loc) ?? "";
            var preview = text.Length > 200 ? text[..200] + "…" : text;
            return new DataCardSummary(loc.CardId, loc.Name, loc.Scope, preview, text.Length, _repo.Modified(loc), ToValue(text) is not JValue);
        }).ToList();
    }

    public DataEntry Read(string flowId, string cardId) => ReadAt(Resolve(flowId, cardId));

    public DataEntry Write(string flowId, string cardId, string? text) => WriteAt(Resolve(flowId, cardId), text ?? "");

    public DataEntry ReadAt(DataLocation l)
    {
        var text = _repo.Read(l);
        return new DataEntry(l.CardId, l.Name, l.Scope, text ?? "", _repo.PathFor(l), text?.Length ?? 0, _repo.Modified(l), text is not null);
    }

    public DataEntry WriteAt(DataLocation l, string text)
    {
        if (text.Length > IDataService.MaxLength) throw new ArgumentException($"Stored text is limited to {IDataService.MaxLength / 1024} KB.");
        _repo.Write(l, text);
        _bus.PublishDataChanged(l.FlowId, l.CardId);
        return ReadAt(l);
    }

    public JObject LoadContext(string flowId, JObject definition)
    {
        var context = new JObject();
        foreach (var n in DataNodes(definition))
        {
            var loc = Location(flowId, n);
            context[loc.Name] = ToValue(_repo.Read(loc) ?? "");
        }
        return context;
    }

    public JToken ToValue(string text)
    {
        var t = text.TrimStart();
        if (t.Length > 0 && (t[0] == '{' || t[0] == '['))
        {
            try { return JToken.Parse(text); } catch (Newtonsoft.Json.JsonReaderException) { /* just text */ }
        }
        return new JValue(text);
    }

    public void DeleteFlowData(string flowId) => _repo.DeleteFlow(flowId);

    public void CopyFlowData(string fromFlowId, string toFlowId) => _repo.CopyFlow(fromFlowId, toFlowId);

    public JObject ExportFlowData(string flowId, JObject definition)
    {
        var data = new JObject();
        foreach (var n in DataNodes(definition).Where(n => ScopeOf(n) == DataScope.Flow))
        {
            var text = _repo.Read(Location(flowId, n));
            if (!string.IsNullOrEmpty(text)) data[(string)n["id"]!] = text;
        }
        return data;
    }

    public JObject ExportShared()
    {
        var o = new JObject();
        foreach (var (name, text) in _repo.ListShared()) if (text.Length > 0) o[name] = text;
        return o;
    }

    public int ImportShared(JObject? texts)
    {
        if (texts is null) return 0;
        var added = 0;
        foreach (var p in texts.Properties())
        {
            if (DataNames.Problem(p.Name) is not null || p.Value.Type != JTokenType.String) continue;
            var text = (string)p.Value!;
            if (text.Length > IDataService.MaxLength) continue;
            var where = new DataLocation(DataScope.Shared, "", "", p.Name);
            if (_repo.Read(where) is not null) continue; // never overwrite what is already here
            _repo.Write(where, text);
            added++;
        }
        return added;
    }

    public void ImportFlowData(string flowId, JObject definition, JObject? data)
    {
        if (data is null) return;
        foreach (var n in DataNodes(definition).Where(n => ScopeOf(n) == DataScope.Flow))
        {
            var id = (string)n["id"]!;
            if (data[id]?.Type == JTokenType.String && (string)data[id]! is { Length: <= IDataService.MaxLength } text)
                _repo.Write(Location(flowId, n), text);
        }
    }

    // ---- helpers
    private Flow GetFlow(string flowId) => _flows.Get(flowId) ?? throw new KeyNotFoundException($"Flow '{flowId}' was not found.");

    private static IEnumerable<JObject> DataNodes(JObject definition) =>
        (definition["nodes"] as JArray ?? new JArray()).OfType<JObject>().Where(n => (string?)n["type"] == DataStoreCard.TypeId);

    private static string ScopeOf(JObject node) => node["params"] is JObject p && (string?)p["scope"] is { } s && DataScope.IsValid(s) ? s : DataScope.Flow;

    private static DataLocation Location(string flowId, JObject node) =>
        new(ScopeOf(node), flowId, (string)node["id"]!, ((string?)node["name"])?.Trim() ?? "");

    private DataLocation Resolve(string flowId, string cardId)
    {
        var flow = GetFlow(flowId);
        var node = DataNodes(flow.Definition).FirstOrDefault(n => (string?)n["id"] == cardId)
                   ?? throw new KeyNotFoundException($"'{cardId}' is not a Data card of this flow. Save the flow first if you just added it.");
        return Location(flowId, node);
    }
}
