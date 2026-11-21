using Microsoft.Data.Sqlite;
using Tammoa.Enums;
using Tammoa.Models;

namespace Tammoa.Repositories;

public class ConnectionRepository : IConnectionRepository
{
    private readonly string _connectionString;

    public ConnectionRepository(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = true }.ToString();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS connection (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                provider TEXT NOT NULL,
                base_url TEXT NOT NULL,
                api_key TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    private const string Columns = "id, name, provider, base_url, api_key, created_at, updated_at";

    public IReadOnlyList<Connection> List()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM connection ORDER BY name COLLATE NOCASE, id";
        using var r = cmd.ExecuteReader();
        var list = new List<Connection>();
        while (r.Read()) list.Add(Read(r));
        return list;
    }

    public Connection? Get(string id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM connection WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    public void Insert(Connection x)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO connection (id, name, provider, base_url, api_key, created_at, updated_at) VALUES ($id, $name, $provider, $url, $key, $created, $updated)";
        Bind(cmd, x);
        cmd.ExecuteNonQuery();
    }

    public bool Update(Connection x)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE connection SET name = $name, provider = $provider, base_url = $url, api_key = $key, created_at = $created, updated_at = $updated WHERE id = $id";
        Bind(cmd, x);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool Delete(string id)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM connection WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    private static void Bind(SqliteCommand cmd, Connection x)
    {
        cmd.Parameters.AddWithValue("$id", x.Id);
        cmd.Parameters.AddWithValue("$name", x.Name);
        cmd.Parameters.AddWithValue("$provider", x.Provider.ToString());
        cmd.Parameters.AddWithValue("$url", x.BaseUrl);
        cmd.Parameters.AddWithValue("$key", (object?)x.ApiKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", x.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updated", x.UpdatedAt.ToString("O"));
    }

    private static Connection Read(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), Enum.Parse<ConnectionProvider>(r.GetString(2)), r.GetString(3),
        r.IsDBNull(4) ? null : r.GetString(4),
        DateTimeOffset.Parse(r.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind),
        DateTimeOffset.Parse(r.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind));
}
