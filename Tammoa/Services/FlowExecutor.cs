using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Models;

namespace Tammoa.Services;

public class FlowExecutor : IFlowExecutor
{
    /// <summary>At most one "live" update per card per interval (what a running card has produced so far).</summary>
    public static readonly TimeSpan LiveInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>How many times one card may run in one run before the engine stops a loop that never ends (per flow: settings.loopLimit).</summary>
    public const int DefaultLoopLimit = 50;

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

        // ---- loops: a connection that goes back to an earlier card is a loop-back. Without the loop-backs the graph is a plain
        // acyclic graph and the scheduling below works as it always did; a loop-back re-runs its target and the cards of that loop.
        var loops = LoopAnalysis.Analyze(nodes.Keys.ToList(), edges.Select(e => (e.Source, e.Target)).ToList());
        bool IsBack((string Source, string Handle, string Target) e) => loops.BackEdges.Contains((e.Source, e.Target));
        var forwardIncoming = incoming.ToDictionary(k => k.Key, k => k.Value.Where(e => !IsBack(e)).ToList());
        var backOut = nodes.Keys.ToDictionary(k => k, k => edges.Where(e => e.Source == k && IsBack(e)).ToList());

        // ---- side branches: a card that hangs off a NON-branching card inside a loop (e.g. a Write file after a JSON card) is not the
        // loop's exit, it is something done every round, so it runs each round (it is reset with the loop) instead of waiting for the end.
        // A card reached through a branching card's output that leaves the loop (IF "false") is the exit and still waits.
        bool Branches(string id) => _cards.Find((string)nodes[id]["type"]!) is { } c && c.Info.Outputs.Count > 1;
        HashSet<string> Reach(IEnumerable<string> from)
        {
            var seen = new HashSet<string>();
            var todo = new Stack<string>(from);
            while (todo.Count > 0)
            {
                var u = todo.Pop();
                if (!seen.Add(u)) continue;
                foreach (var e in edges.Where(e => e.Source == u && !IsBack(e))) todo.Push(e.Target);
            }
            return seen;
        }
        var sideLoop = new Dictionary<string, int>(); // side-branch card -> the loop whose rounds it follows
        foreach (var (loopId, members) in loops.LoopMembers)
        {
            bool Leaves(string s, string t) => loops.LoopOf.GetValueOrDefault(t, -1) != loopId && members.Contains(s);
            var exits = Reach(edges.Where(e => !IsBack(e) && Leaves(e.Source, e.Target) && Branches(e.Source)).Select(e => e.Target));
            var sides = Reach(edges.Where(e => !IsBack(e) && Leaves(e.Source, e.Target) && !Branches(e.Source)).Select(e => e.Target));
            foreach (var sd in sides.Where(x => !exits.Contains(x) && !loops.LoopOf.ContainsKey(x))) sideLoop[sd] = loopId;
        }
        int? LoopIdOf(string id) => loops.LoopOf.TryGetValue(id, out var l) ? l : sideLoop.TryGetValue(id, out var sl) ? sl : null;
        var loopLimit = (int?)(definition["settings"]?["loopLimit"]) is { } l and >= 1 ? Math.Min(l, 1000) : DefaultLoopLimit;
        var visits = new Dictionary<string, int>();                      // how many times each card has started in this run
        var history = new Dictionary<string, List<NodeIteration>>();     // earlier rounds of a card (newest NodeIteration.Max kept)
        var retrigger = new Dictionary<string, List<JToken>>();          // loop-back targets waiting to run again, with their new input
        var forced = new Dictionary<string, JToken?>();                  // a re-run card: starts with this input, whatever its other parents say

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
        NodeRun Make(string id, NodeRunStatus st, DateTimeOffset? started = null, DateTimeOffset? finished = null, JToken? output = null, string? error = null, JToken? input = null) =>
            new(id, st, started, finished, output, error, input, Iteration: Math.Max(1, visits.GetValueOrDefault(id)),
                Previous: history.TryGetValue(id, out var h) && h.Count > 0 ? h.ToList() : null);

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

        static bool Terminal(NodeRunStatus st) => st is NodeRunStatus.Succeeded or NodeRunStatus.Failed or NodeRunStatus.Skipped or NodeRunStatus.Cancelled;
        bool SameLoop(string a, string b) => LoopIdOf(a) is { } la && LoopIdOf(b) is { } lb && la == lb;
        bool LoopAlive(string p) => LoopIdOf(p) is { } l && (loops.LoopMembers[l].Any(m => status[m] is NodeRunStatus.Pending or NodeRunStatus.Running) || retrigger.Keys.Any(t => loops.LoopOf[t] == l)); // a queued loop-back means another round is coming
        // a parent is settled when it has finished, and (if it is part of ANOTHER loop) that loop has stopped repeating:
        // a card after a loop waits for the loop to end instead of being skipped on the first round
        bool Settled(string parent, string node) => Terminal(status[parent]) && (SameLoop(parent, node) || !LoopAlive(parent));

        void Archive(string id) // keep how a card ran in this round before the next round replaces it
        {
            NodeRun current;
            lock (gate) current = run.Nodes.First(n => n.NodeId == id);
            if (current.Status is not (NodeRunStatus.Succeeded or NodeRunStatus.Failed or NodeRunStatus.Cancelled)) return;
            if (!history.TryGetValue(id, out var list)) history[id] = list = new List<NodeIteration>();
            list.Add(new NodeIteration(current.Iteration, current.Status, current.StartedAt, current.FinishedAt, current.Input, current.Output, current.Error));
            while (list.Count > NodeIteration.Max) list.RemoveAt(0);
        }

        while (true)
        {
            bool changed;
            do
            {
                changed = false;

                // ---- loop-backs that fired: when no card of that loop is still running, start the next round
                foreach (var target in retrigger.Keys.ToList())
                {
                    var loopId = loops.LoopOf[target];
                    var region = loops.LoopMembers[loopId].Concat(sideLoop.Where(s => s.Value == loopId).Select(s => s.Key)).ToList();
                    if (region.Any(m => status[m] == NodeRunStatus.Running)) continue;
                    var newInputs = retrigger[target];
                    retrigger.Remove(target);
                    if (ct.IsCancellationRequested || firstError is not null) continue;
                    foreach (var m in region)
                    {
                        Archive(m);
                        outputs.Remove(m);
                        branches.Remove(m);
                        pendingLive.Remove(m);
                        lock (gate) lastLive.Remove(m);
                        Set(m, Make(m, NodeRunStatus.Pending));
                    }
                    forced[target] = newInputs.Count == 1 ? newInputs[0] : new JArray(newInputs);
                    changed = true;
                }

                foreach (var id in nodes.Keys.Where(k => status[k] == NodeRunStatus.Pending).ToList())
                {
                    var card = _cards.Find((string)nodes[id]["type"]!);
                    var inc = forwardIncoming[id];
                    var now = _time.GetUtcNow();

                    if (ct.IsCancellationRequested) { Set(id, Make(id, NodeRunStatus.Cancelled, finished: now)); changed = true; continue; }
                    if (firstError is not null) { Set(id, Make(id, NodeRunStatus.Skipped, finished: now)); changed = true; continue; }

                    JToken? input;
                    if (forced.Remove(id, out var forcedInput))
                    {
                        input = forcedInput; // a loop-back brought this card its new input; its other parents already ran
                    }
                    else
                    {
                        List<(string Source, string Handle, string Target)> active;
                        if (inc.Count == 0)
                        {
                            // a card with nothing connected runs only if it is a trigger, or a source (e.g. a Data card: it needs no trigger)
                            if (card is null || !(card.Info.IsTrigger || card.Info.IsSource)) { Set(id, Make(id, NodeRunStatus.Skipped, finished: now)); changed = true; continue; }
                            active = [];
                        }
                        else
                        {
                            // wait until every parent is settled, then run on the connections that are active:
                            // the parent succeeded and (it is not a branching card, or it fired this connection's handle)
                            if (inc.Any(e => !Settled(e.Source, id))) continue;
                            active = inc.Where(e => status[e.Source] == NodeRunStatus.Succeeded && (branches[e.Source] is null || branches[e.Source] == e.Handle)).ToList();
                            if (active.Count == 0) { Set(id, Make(id, NodeRunStatus.Skipped, finished: now)); changed = true; continue; }
                        }
                        input = active.Count == 0 ? null : active.Count == 1 ? outputs[active[0].Source] : new JArray(active.Select(e => outputs[e.Source]));
                    }

                    if (card is null)
                    {
                        Set(id, Make(id, NodeRunStatus.Failed, now, now, error: $"Unknown card type '{(string)nodes[id]["type"]!}'."));
                        firstError ??= $"Unknown card type '{(string)nodes[id]["type"]!}'.";
                        changed = true;
                        continue;
                    }

                    var p = nodes[id]["params"] as JObject ?? new JObject();
                    var name = ((string?)nodes[id]["name"])?.Trim() is { Length: > 0 } n ? n : card.Info.Name;
                    visits[id] = visits.GetValueOrDefault(id) + 1;
                    if (visits[id] > loopLimit)
                    {
                        var message = $"Loop limit reached: '{name}' already ran {loopLimit} times in this run. A loop needs something that ends it (an IF card that sends the flow elsewhere), or raise the loop limit.";
                        visits[id]--;   // the refused start is not a run: the card shows the rounds it really made
                        Set(id, Make(id, NodeRunStatus.Failed, now, now, error: message));
                        firstError ??= message;
                        changed = true;
                        continue;
                    }
                    startedAt[id] = now;
                    inputs[id] = input;
                    Set(id, Make(id, NodeRunStatus.Running, now, input: input?.DeepClone()));
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
                Set(doneId, Make(doneId, NodeRunStatus.Succeeded, startedAt[doneId], finished, result.Output, input: inputs[doneId]?.DeepClone()));
                // a connection that goes back to an earlier card: if it is active (this card is not branching, or fired that output),
                // the earlier card will run again with this output as its input
                foreach (var back in backOut[doneId].Where(b => result.Branch is null || result.Branch == b.Handle))
                {
                    if (!retrigger.TryGetValue(back.Target, out var waiting)) retrigger[back.Target] = waiting = new List<JToken>();
                    waiting.Add(result.Output.DeepClone());
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Set(doneId, Make(doneId, NodeRunStatus.Cancelled, startedAt[doneId], finished, input: inputs[doneId]?.DeepClone()));
            }
            catch (Exception ex)
            {
                if (ex is not (ArgumentException or InvalidOperationException)) Console.Error.WriteLine(ex);
                Set(doneId, Make(doneId, NodeRunStatus.Failed, startedAt[doneId], finished, error: ex.Message, input: inputs[doneId]?.DeepClone()));
                firstError ??= ex.Message;
            }
        }

        // Anything still pending can only be a structural leftover; never leave a card Pending in a finished run.
        foreach (var id in status.Where(s => s.Value == NodeRunStatus.Pending).Select(s => s.Key).ToList())
            Set(id, Make(id, NodeRunStatus.Skipped, finished: _time.GetUtcNow()));

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
