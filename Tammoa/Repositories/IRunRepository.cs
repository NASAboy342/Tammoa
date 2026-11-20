using Tammoa.Models;

namespace Tammoa.Repositories;

/// <summary>Holds run snapshots. TODO(persist runs): the implementation is in memory, so history is lost on restart.</summary>
public interface IRunRepository
{
    void Save(FlowRun run);                              // insert or replace by id
    FlowRun? Get(string id);
    IReadOnlyList<FlowRun> ListForFlow(string flowId);   // newest first
}
