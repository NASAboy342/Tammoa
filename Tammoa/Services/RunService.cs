using System.Collections.Concurrent;
using Newtonsoft.Json.Linq;
using Tammoa.Enums;
using Tammoa.Models;
using Tammoa.Repositories;

namespace Tammoa.Services;

public class RunService : IRunService
{
    private readonly IFlowService _flows;
    private readonly ICardRegistry _cards;
    private readonly IFlowExecutor _executor;
    private readonly IRunRepository _runs;
    private readonly IRunEventBus _bus;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new();

    public RunService(IFlowService flows, ICardRegistry cards, IFlowExecutor executor, IRunRepository runs, IRunEventBus bus, TimeProvider time)
    {
        _flows = flows;
        _cards = cards;
        _executor = executor;
        _runs = runs;
        _bus = bus;
        _time = time;
    }

    public FlowRun Start(string flowId, JToken? input)
    {
        var flow = _flows.Get(flowId);
        var nodes = (flow.Definition["nodes"] as JArray ?? new JArray()).Cast<JObject>().ToList();
        if (!nodes.Any(n => _cards.Find((string?)n["type"] ?? "")?.Info.IsTrigger == true))
            throw new ArgumentException("Add a trigger card to run this flow.");

        var definition = (JObject)flow.Definition.DeepClone(); // the run uses the definition as it was when started
        var initial = new FlowRun(
            Guid.NewGuid().ToString("N"), flow.Id, flow.Name, RunStatus.Running, _time.GetUtcNow(), null,
            input?.DeepClone(), null,
            nodes.Select(n => new NodeRun((string)n["id"]!, NodeRunStatus.Pending)).ToList());

        Publish(initial);
        var cts = new CancellationTokenSource();
        _active[initial.Id] = cts;
        _ = Task.Run(() => RunAsync(initial, definition, input, cts));
        return initial;
    }

    public FlowRun Get(string runId) => _runs.Get(runId) ?? throw new KeyNotFoundException($"Run '{runId}' was not found.");

    public IReadOnlyList<FlowRun> ListForFlow(string flowId) => _runs.ListForFlow(flowId);

    public FlowRun Cancel(string runId)
    {
        var run = Get(runId);
        if (_active.TryGetValue(runId, out var cts)) cts.Cancel();
        return run;
    }

    private async Task RunAsync(FlowRun initial, JObject definition, JToken? input, CancellationTokenSource cts)
    {
        try
        {
            await _executor.ExecuteAsync(initial, definition, input, Publish, cts.Token);
        }
        catch (Exception ex) // the executor reports card failures itself; this is a safety net so a run never stays Running
        {
            Console.Error.WriteLine(ex);
            var last = _runs.Get(initial.Id) ?? initial;
            Publish(last with { Status = RunStatus.Failed, FinishedAt = _time.GetUtcNow(), Error = ex.Message, Revision = last.Revision + 1 });
        }
        finally
        {
            _active.TryRemove(initial.Id, out _);
            cts.Dispose();
        }
    }

    private void Publish(FlowRun run)
    {
        _runs.Save(run);
        _bus.Publish(run);
    }
}
