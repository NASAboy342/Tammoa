using Tammoa.Models;

namespace Tammoa.Repositories;

/// <summary>The text of Data cards, one plain .txt file per card, so it can also be opened and edited in any editor.
/// Persistence only; rules (size, names) live in <c>IDataService</c>.</summary>
public interface IDataRepository
{
    string PathFor(DataLocation location);
    string? Read(DataLocation location);                 // null when nothing was saved yet
    DateTimeOffset? Modified(DataLocation location);
    void Write(DataLocation location, string text);      // atomic: a crash never leaves half a file
    IReadOnlyList<(string Name, string Text)> ListShared(); // file name without .txt -> text, for a backup
    void DeleteFlow(string flowId);                      // removes every flow-scoped file of the flow
    void CopyFlow(string fromFlowId, string toFlowId);   // for "duplicate flow"
}
