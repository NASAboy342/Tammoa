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
        var edges = ((JArray)(definition["edges"] ?? new JArray())).Cast<JObject>().Select(e => (Source: (string)e["source"]!, Target: (string)e["target"]!)).ToList();
        var parents = nodes.Keys.ToDictionary(k => k, k => edges.Where(e => e.Target == k).Select(e => e.Source).ToList());

        var run = initial;
        var status = nodes.Keys.ToDictionary(k => k, _ => NodeRunStatus.Pending);
        var outputs = new Dictionary<string, JToken>();
        var running = new Dictionary<Task<JToken>, string>();
        string? firstError = null;

        // Only this loop mutates state (card work runs on other threads but reports back through Task results).
        void Set(string id, NodeRun update)
        {
            status[id] = update.Status;
            run = run with { Nodes = run.Nodes.Select(n => n.NodeId == id ? update : n).ToList(), Revision = run.Revision + 1 };
            onChange(run);
        }
        var startedAt = new Dictionary<string, DateTimeOffset>();

        while (true)
        {
            bool changed;
            do
            {
                changed = false;
                foreach (var id in nodes.Keys.Where(k => status[k] == NodeRunStatus.Pending).ToList())
                {
                    var card = _cards.Find((string)nodes[id]["type"]!);
                    var ps = parents[id];
                    var now = _time.GetUtcNow();

                    if (ct.IsCancellationRequested) { Set(id, new NodeRun(id, NodeRunStatus.Cancelled, FinishedAt: now)); changed = true; continue; }
                    if (firstError is not null) { Set(id, new NodeRun(id, NodeRunStatus.Skipped, FinishedAt: now)); changed = true; continue; }

                    bool ready;
                    if (ps.Count == 0)
                    {
                        if (card is null || !card.Info.IsTrigger) { Set(id, new NodeRun(id, NodeRunStatus.Skipped, FinishedAt: now)); changed = true; continue; }
                        ready = true;
                    }
                    else if (ps.Any(p => status[p] is NodeRunStatus.Failed or NodeRunStatus.Skipped or NodeRunStatus.Cancelled))
                    {
                        Set(id, new NodeRun(id, NodeRunStatus.Skipped, FinishedAt: now)); changed = true; continue;
                    }
                    else ready = ps.All(p => status[p] == NodeRunStatus.Succeeded);

                    if (!ready) continue;
                    if (card is null)
                    {
                        Set(id, new NodeRun(id, NodeRunStatus.Failed, now, now, Error: $"Unknown card type '{(string)nodes[id]["type"]!}'."));
                        firstError ??= $"Unknown card type '{(string)nodes[id]["type"]!}'.";
                        changed = true;
                        continue;
                    }

                    JToken? input = ps.Count == 0 ? null : ps.Count == 1 ? outputs[ps[0]] : new JArray(ps.Select(p => outputs[p]));
                    var p = nodes[id]["params"] as JObject ?? new JObject();
                    startedAt[id] = now;
                    Set(id, new NodeRun(id, NodeRunStatus.Running, now));
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
                var output = await done;
                outputs[doneId] = output;
                Set(doneId, new NodeRun(doneId, NodeRunStatus.Succeeded, startedAt[doneId], finished, output));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Set(doneId, new NodeRun(doneId, NodeRunStatus.Cancelled, startedAt[doneId], finished));
            }
            catch (Exception ex)
            {
                if (ex is not (ArgumentException or InvalidOperationException)) Console.Error.WriteLine(ex);
                Set(doneId, new NodeRun(doneId, NodeRunStatus.Failed, startedAt[doneId], finished, Error: ex.Message));
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
