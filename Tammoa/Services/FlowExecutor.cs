using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Models;

namespace Tammoa.Services;

public class FlowExecutor : IFlowExecutor
{
    /// <summary>At most one "live" update per card per interval (what a running card has produced so far).</summary>
    public static readonly TimeSpan LiveInterval = TimeSpan.FromMilliseconds(150);

    private readonly ICardRegistry _cards;
    private readonly TimeProvider _time;
    private readonly IDataService _data;

    public FlowExecutor(ICardRegistry cards, TimeProvider time, IDataService data)
    {
        _cards = cards;
        _time = time;
        _data = data;
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

        // The scheduling loop below is single-threaded, but running cards report progress from their own threads.
        // Everything that changes `run` or `status` happens under this lock, and onChange is called inside it so snapshots
        // reach the repository and the clients in revision order.
        var gate = new object();
        void Set(string id, NodeRun update)
        {
            lock (gate)
            {
                status[id] = update.Status;
                run = run with { Nodes = run.Nodes.Select(n => n.NodeId == id ? update : n).ToList(), Revision = run.Revision + 1 };
                onChange(run);
            }
        }

        // ---- live progress of running cards (throttled; the last value is always flushed shortly after)
        var lastLive = new Dictionary<string, long>();
        var pendingLive = new Dictionary<string, JToken>();
        var flushScheduled = new HashSet<string>();
        void ApplyLive(string id, JToken live) // caller holds gate
        {
            lastLive[id] = _time.GetTimestamp();
            run = run with { Nodes = run.Nodes.Select(n => n.NodeId == id ? n with { Live = live } : n).ToList(), Revision = run.Revision + 1 };
            onChange(run);
        }
        async Task FlushLater(string id, TimeSpan delay)
        {
            await Task.Delay(delay, _time);
            lock (gate)
            {
                flushScheduled.Remove(id);
                if (pendingLive.Remove(id, out var live) && status[id] == NodeRunStatus.Running) ApplyLive(id, live);
            }
        }
        void ReportLive(string id, JToken live)
        {
            lock (gate)
            {
                if (status[id] != NodeRunStatus.Running) return; // a late report from a card that already finished or was cancelled
                var since = lastLive.TryGetValue(id, out var last) ? _time.GetElapsedTime(last) : TimeSpan.MaxValue;
                if (since >= LiveInterval) { pendingLive.Remove(id); ApplyLive(id, live); return; }
                pendingLive[id] = live;
                if (flushScheduled.Add(id)) _ = FlushLater(id, LiveInterval - since);
            }
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
                        // a card with nothing connected runs only if it is a trigger, or a source (e.g. a Data card: it needs no trigger)
                        if (card is null || !(card.Info.IsTrigger || card.Info.IsSource)) { Set(id, new NodeRun(id, NodeRunStatus.Skipped, FinishedAt: now)); changed = true; continue; }
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
                    var name = ((string?)nodes[id]["name"])?.Trim() is { Length: > 0 } n ? n : card.Info.Name;
                    startedAt[id] = now;
                    inputs[id] = input;
                    Set(id, new NodeRun(id, NodeRunStatus.Running, now, Input: input?.DeepClone()));
                    var cardInput = input?.DeepClone();
                    var cardRunInput = runInput?.DeepClone();
                    var cardParams = (JObject)p.DeepClone();
                    var nodeId = id;
                    running[Task.Run(() =>
                    {
                        // the Data cards are read when the card starts, so a card updated earlier in this run is seen by the ones after it
                        var context = new CardExecutionContext(cardParams, cardInput, cardRunInput, _data.LoadContext(initial.FlowId, definition),
                            live => ReportLive(nodeId, live.DeepClone()), initial.FlowId, nodeId, name);
                        return card.ExecuteAsync(context, ct);
                    })] = id;
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

        lock (gate)
        {
            run = run with
            {
                Status = ct.IsCancellationRequested ? RunStatus.Cancelled : firstError is not null ? RunStatus.Failed : RunStatus.Succeeded,
                FinishedAt = _time.GetUtcNow(),
                Error = ct.IsCancellationRequested ? null : firstError,
                Revision = run.Revision + 1,
            };
            onChange(run);
            return run;
        }
    }
}
