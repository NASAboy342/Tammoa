using Tammoa.Models;

namespace Tammoa.Repositories;

/// <summary>Persistence only. The API key is stored in plaintext in the database file; the data folder is made owner-only (see DataPaths.RestrictToOwner).</summary>
public interface IConnectionRepository
{
    IReadOnlyList<Connection> List();      // by name
    Connection? Get(string id);
    void Insert(Connection connection);
    bool Update(Connection connection);
    bool Delete(string id);
}
