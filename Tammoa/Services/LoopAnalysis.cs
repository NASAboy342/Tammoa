namespace Tammoa.Services;

/// <summary>What the engine needs to know about loops in a flow graph: which connections go BACK to an earlier card (loop-backs), and
/// which cards belong to the same loop (strongly connected components of more than one card).
/// A connection is a loop-back when, walking the graph depth-first from the start cards, it points at a card that is still being walked.
/// Take away the loop-backs and what remains is a plain acyclic graph, which the normal scheduling handles.</summary>
public record LoopInfo(
    IReadOnlySet<(string Source, string Target)> BackEdges,
    IReadOnlyDictionary<string, int> LoopOf,                       // card -> loop id, only for cards that are in a loop
    IReadOnlyDictionary<int, IReadOnlyList<string>> LoopMembers)   // loop id -> its cards
{
    public bool HasLoops => BackEdges.Count > 0;
}

public static class LoopAnalysis
{
    public static LoopInfo Analyze(IReadOnlyList<string> nodeIds, IReadOnlyList<(string Source, string Target)> edges)
    {
        var ids = nodeIds.ToHashSet();
        var next = nodeIds.ToDictionary(i => i, _ => new List<string>());
        var hasIncoming = new HashSet<string>();
        foreach (var (s, t) in edges.Where(e => ids.Contains(e.Source) && ids.Contains(e.Target)))
        {
            if (!next[s].Contains(t)) next[s].Add(t);
            hasIncoming.Add(t);
        }

        // ---- loop-backs: depth-first from the cards nothing points at (triggers, sources), then from any card not reached yet
        var state = new Dictionary<string, int>(); // 1 = being walked, 2 = done
        var back = new HashSet<(string, string)>();
        void Walk(string u)
        {
            state[u] = 1;
            foreach (var v in next[u])
            {
                if (!state.TryGetValue(v, out var sv)) Walk(v);
                else if (sv == 1) back.Add((u, v));
            }
            state[u] = 2;
        }
        foreach (var id in nodeIds.Where(i => !hasIncoming.Contains(i))) if (!state.ContainsKey(id)) Walk(id);
        foreach (var id in nodeIds) if (!state.ContainsKey(id)) Walk(id);

        // ---- loops: strongly connected components with more than one card (Tarjan)
        var index = new Dictionary<string, int>();
        var low = new Dictionary<string, int>();
        var onStack = new HashSet<string>();
        var stack = new Stack<string>();
        var counter = 0;
        var loopOf = new Dictionary<string, int>();
        var members = new Dictionary<int, IReadOnlyList<string>>();
        var nextLoopId = 0;
        void Connect(string v)
        {
            index[v] = low[v] = counter++;
            stack.Push(v);
            onStack.Add(v);
            foreach (var w in next[v])
            {
                if (!index.ContainsKey(w)) { Connect(w); low[v] = Math.Min(low[v], low[w]); }
                else if (onStack.Contains(w)) low[v] = Math.Min(low[v], index[w]);
            }
            if (low[v] != index[v]) return;
            var component = new List<string>();
            string x;
            do { x = stack.Pop(); onStack.Remove(x); component.Add(x); } while (x != v);
            if (component.Count < 2) return;
            var loopId = nextLoopId++;
            foreach (var m in component) loopOf[m] = loopId;
            members[loopId] = nodeIds.Where(component.Contains).ToList(); // in the flow's own order
        }
        foreach (var id in nodeIds) if (!index.ContainsKey(id)) Connect(id);

        return new LoopInfo(back, loopOf, members);
    }
}
