using Tammoa.Models;

namespace Tammoa.Repositories;

/// <summary>Persistence only; business rules live in <c>IFlowService</c>.</summary>
public interface IFlowRepository
{
    IReadOnlyList<Flow> List();            // newest-updated first
    Flow? Get(string id);                  // null when missing
    void Insert(Flow flow);
    bool Update(Flow flow);                // false when the id does not exist
    bool Delete(string id);                // false when the id does not exist
}
