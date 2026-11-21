using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Models;

namespace Tammoa.Services;

public class FlowExecutor : IFlowExecutor
{
    private readonly ICardRegistry _cards;
    private readonly TimeProvider _time;

    public FlowExecutor(ICardRegistry cards, TimeProvider time)
    {
        _cards = cards;
        _time = time;
    }

    public async Task<FlowRun> ExecuteAsync(FlowRun initial, JObject definition, JToken? runInput, Action<FlowRun> onChange, CancellationToken ct)
    {
        var nodes = ((JArray)(definition["nodes"] ?? new JArray())).Cast<JObject>().ToDictionary(n => (string)n["id"]!);
        var edges = ((JArray)(definition["edges"] ?? new JArray())).Cast<JObject>()
            .Select(e => (Source: (string)e["source"]!, Handle: (string?)e["sourceHandle"] ?? "main", Target: (string)e["target"]!)).ToList();
        var incoming = nodes.Keys.ToDictionary(k => k, k => edges.Where(e => e.Target == k).ToList());

        var run = initial;
        var status = nodes.Keys.ToDictionary(k => k, _ => NodeRunStatus.Pending);
        var outputs = new Dictionary<string, JToken>();
        var branches = new Dictionary<string, string?>(); // which output handle a branching card fired (null = all)
        var running = new Dictionary<Task<CardResult>, string>();
        string? firstError = null;

        // Only this loop mutates state (card work runs on other threads but reports back through Task results).
        void Set(string id, NodeRun update)
        {
            status[id] = update.Status;
            run = run with { Nodes = run.Nodes.Select(n => n.NodeId == id ? update : n).ToList(), Revision = run.Revision + 1 };
            onChange(run);
        }
        var startedAt = new Dictionary<string, DateTimeOffset>();
        var inputs = new Dictionary<string, JToken?>(); // what each started card received, kept in its NodeRun

        while (true)
        {
            bool changed;
            do
            {
                changed = false;
                foreach (var id in nodes.Keys.Where(k => status[k] == NodeRunStatus.Pending).ToList())
                {
                    var card = _cards.Find((string)nodes[id]["type"]!);
                    var inc = incoming[id];
                    var now = _time.GetUtcNow();

                    if (ct.IsCancellationRequested) { Set(id, new NodeRun(id, NodeRunStatus.Cancelled, FinishedAt: now)); changed = true; continue; }
                    if (firstError is not null) { Set(id, new NodeRun(id, NodeRunStatus.Skipped, FinishedAt: now)); changed = true; continue; }

                    List<(string Source, string Handle, string Target)> active;
                    if (inc.Count == 0)
                    {
                        if (card is null || !card.Info.IsTrigger) { Set(id, new NodeRun(id, NodeRunStatus.Skipped, FinishedAt: now)); changed = true; continue; }
                        active = [];
                    }
                    else
                    {
                        // wait until every parent is finished, then run on the connections that are active:
                        // the parent succeeded and (it is not a branching card, or it fired this connection's handle)
                        if (inc.Any(e => status[e.Source] is NodeRunStatus.Pending or NodeRunStatus.Running)) continue;
                        active = inc.Where(e => status[e.Source] == NodeRunStatus.Succeeded && (branches[e.Source] is null || branches[e.Source] == e.Handle)).ToList();
                        if (active.Count == 0) { Set(id, new NodeRun(id, NodeRunStatus.Skipped, FinishedAt: now)); changed = true; continue; }
                    }
                    if (card is null)
                    {
                        Set(id, new NodeRun(id, NodeRunStatus.Failed, now, now, Error: $"Unknown card type '{(string)nodes[id]["type"]!}'."));
                        firstError ??= $"Unknown card type '{(string)nodes[id]["type"]!}'.";
                        changed = true;
                        continue;
                    }

                    JToken? input = active.Count == 0 ? null : active.Count == 1 ? outputs[active[0].Source] : new JArray(active.Select(e => outputs[e.Source]));
                    var p = nodes[id]["params"] as JObject ?? new JObject();
                    startedAt[id] = now;
                    inputs[id] = input;
                    Set(id, new NodeRun(id, NodeRunStatus.Running, now, Input: input?.DeepClone()));
                    var context = new CardExecutionContext((JObject)p.DeepClone(), input?.DeepClone(), runInput?.DeepClone());
                    running[Task.Run(() => card.ExecuteAsync(context, ct))] = id;
                    changed = true;
                }
            } while (changed);

            if (running.Count == 0) break;

            var done = await Task.WhenAny(running.Keys);
            var doneId = running[done];
            running.Remove(done);
            var finished = _time.GetUtcNow();
            try
            {
                var result = await done;
                outputs[doneId] = result.Output;
                branches[doneId] = result.Branch;
                Set(doneId, new NodeRun(doneId, NodeRunStatus.Succeeded, startedAt[doneId], finished, result.Output, Input: inputs[doneId]?.DeepClone()));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Set(doneId, new NodeRun(doneId, NodeRunStatus.Cancelled, startedAt[doneId], finished, Input: inputs[doneId]?.DeepClone()));
            }
            catch (Exception ex)
            {
                if (ex is not (ArgumentException or InvalidOperationException)) Console.Error.WriteLine(ex);
                Set(doneId, new NodeRun(doneId, NodeRunStatus.Failed, startedAt[doneId], finished, Error: ex.Message, Input: inputs[doneId]?.DeepClone()));
                firstError ??= ex.Message;
            }
        }

        // Anything still pending can only be a structural leftover; never leave a card Pending in a finished run.
        foreach (var id in status.Where(s => s.Value == NodeRunStatus.Pending).Select(s => s.Key).ToList())
            Set(id, new NodeRun(id, NodeRunStatus.Skipped, FinishedAt: _time.GetUtcNow()));

        var final = run with
        {
            Status = ct.IsCancellationRequested ? RunStatus.Cancelled : firstError is not null ? RunStatus.Failed : RunStatus.Succeeded,
            FinishedAt = _time.GetUtcNow(),
            Error = ct.IsCancellationRequested ? null : firstError,
            Revision = run.Revision + 1,
        };
        onChange(final);
        return final;
    }
}
