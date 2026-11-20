using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using Tammoa.Models;

namespace Tammoa.Repositories;

public class FlowRepository : IFlowRepository
{
    private readonly string _connectionString;

    public FlowRepository(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = true }.ToString();
        Migrate();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    private void Migrate() // idempotent
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS flow (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                description TEXT NOT NULL,
                enabled INTEGER NOT NULL,
                definition TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL);
            """;
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<Flow> List()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, name, description, enabled, definition, created_at, updated_at FROM flow ORDER BY updated_at DESC, name";
        using var r = cmd.ExecuteReader();
        var list = new List<Flow>();
        while (r.Read()) list.Add(Read(r));
        return list;
    }

    public Flow? Get(string id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, name, description, enabled, definition, created_at, updated_at FROM flow WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    public void Insert(Flow flow)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO flow (id, name, description, enabled, definition, created_at, updated_at)
            VALUES ($id, $name, $description, $enabled, $definition, $created, $updated)
            """;
        Bind(cmd, flow);
        cmd.ExecuteNonQuery();
    }

    public bool Update(Flow flow)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE flow SET name = $name, description = $description, enabled = $enabled,
                definition = $definition, created_at = $created, updated_at = $updated
            WHERE id = $id
            """;
        Bind(cmd, flow);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool Delete(string id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM flow WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    private static void Bind(SqliteCommand cmd, Flow f)
    {
        cmd.Parameters.AddWithValue("$id", f.Id);
        cmd.Parameters.AddWithValue("$name", f.Name);
        cmd.Parameters.AddWithValue("$description", f.Description);
        cmd.Parameters.AddWithValue("$enabled", f.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$definition", f.Definition.ToString(Newtonsoft.Json.Formatting.None));
        cmd.Parameters.AddWithValue("$created", f.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", f.UpdatedAt.ToString("O"));
    }

    private static Flow Read(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3) != 0,
        JObject.Parse(r.GetString(4)),
        DateTimeOffset.Parse(r.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
        DateTimeOffset.Parse(r.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind));
}
