using System.Collections.Concurrent;
using Tammoa.Enums;
using Tammoa.Models;

namespace Tammoa.Repositories;

public class RunRepository : IRunRepository
{
    public const int MaxRuns = 200;
    private readonly ConcurrentDictionary<string, FlowRun> _runs = new();
    private readonly object _pruneLock = new();

    public void Save(FlowRun run)
    {
        _runs[run.Id] = run;
        if (_runs.Count > MaxRuns) Prune();
    }

    public FlowRun? Get(string id) => _runs.GetValueOrDefault(id);

    public bool Remove(string id) => _runs.TryRemove(id, out _);

    public IReadOnlyList<FlowRun> ListForFlow(string flowId) =>
        _runs.Values.Where(r => r.FlowId == flowId).OrderByDescending(r => r.StartedAt).ToList();

    private void Prune() // drop the oldest finished runs; never a running one
    {
        lock (_pruneLock)
        {
            foreach (var old in _runs.Values.Where(r => r.Status != RunStatus.Running).OrderBy(r => r.StartedAt).Take(Math.Max(0, _runs.Count - MaxRuns)))
                _runs.TryRemove(old.Id, out _);
        }
    }
}
