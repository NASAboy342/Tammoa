using Tammoa.Models;

namespace Tammoa.Repositories;

public interface IAgentAccessRepository
{
    /// <summary>The saved settings, or null when there is no (readable) file yet.</summary>
    AgentSettings? Load();
    void Save(AgentSettings settings);
}
