using Microsoft.Data.Sqlite;
using PulsePoint.Models;
using DbHost = PulsePoint.Models.Host;

namespace PulsePoint.Data;

public class Database
{
    private readonly string _connectionString;

    public Database(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    private void Initialize()
    {
        using var conn = Open();
        conn.Execute(@"
            CREATE TABLE IF NOT EXISTS hosts (
                hostname    TEXT PRIMARY KEY,
                ip          TEXT NOT NULL DEFAULT '',
                uptime      REAL NOT NULL DEFAULT 0,
                last_seen   REAL NOT NULL DEFAULT 0,
                ping        REAL,
                cpu         REAL NOT NULL DEFAULT 0,
                memory      REAL NOT NULL DEFAULT 0,
                disk        REAL,
                friendly_name TEXT,
                sort_order  INTEGER NOT NULL DEFAULT 0,
                rdp_url     TEXT,
                tags        TEXT
            )");

        conn.Execute(@"
            CREATE TABLE IF NOT EXISTS services (
                id   INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                url  TEXT NOT NULL
            )");

        conn.Execute(@"
            CREATE TABLE IF NOT EXISTS settings (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            )");
    }

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    // ── Hosts ────────────────────────────────────────────────

    public List<DbHost> GetAllHosts()
    {
        using var conn = Open();
        return conn.Query<DbHost>(@"
            SELECT hostname, ip, uptime, last_seen, ping, cpu, memory, disk,
                   friendly_name, sort_order, rdp_url, tags
            FROM hosts
            ORDER BY sort_order ASC, hostname ASC");
    }

    public DbHost? GetHost(string hostname)
    {
        using var conn = Open();
        return conn.QuerySingle<DbHost>(
            "SELECT * FROM hosts WHERE hostname = @h", hostname);
    }

    public void Upsert(CheckInPayload p)
    {
        using var conn = Open();
        conn.Execute(@"
            INSERT INTO hosts (hostname, ip, uptime, last_seen, cpu, memory, disk, sort_order)
            VALUES (@hostname, @ip, @uptime, @lastSeen, @cpu, @memory, @disk,
                    COALESCE((SELECT sort_order FROM hosts WHERE hostname = @hostname), 0))
            ON CONFLICT(hostname) DO UPDATE SET
                ip        = excluded.ip,
                uptime    = excluded.uptime,
                last_seen = excluded.last_seen,
                cpu       = excluded.cpu,
                memory    = excluded.memory,
                disk      = excluded.disk",
            new {
                p.Hostname, p.Ip, p.Uptime,
                lastSeen = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                p.Cpu, p.Memory, p.Disk
            });
    }

    public void UpdatePing(string hostname, double? ping)
    {
        using var conn = Open();
        conn.Execute("UPDATE hosts SET ping = @ping WHERE hostname = @h", ping, hostname);
    }

    public void UpdateAsset(string hostname, AssetUpdatePayload p)
    {
        using var conn = Open();
        conn.Execute(@"
            UPDATE hosts SET
                friendly_name = COALESCE(@fn, friendly_name),
                ip            = COALESCE(@ip, ip),
                rdp_url       = COALESCE(@rdp, rdp_url),
                tags          = COALESCE(@tags, tags)
            WHERE hostname = @h",
            new { fn = p.FriendlyName, ip = p.Ip, rdp = p.RdpUrl, tags = p.Tags, h = hostname });
    }

    public void UpdateFriendlyName(string hostname, string name)
    {
        using var conn = Open();
        conn.Execute("UPDATE hosts SET friendly_name = @fn WHERE hostname = @h",
            new { fn = name, h = hostname });
    }

    public void Delete(string hostname)
    {
        using var conn = Open();
        conn.Execute("DELETE FROM hosts WHERE hostname = @h", hostname);
    }

    public void MoveOrder(string hostname, int direction)
    {
        using var conn = Open();
        var hosts = GetAllHosts();
        var idx = hosts.FindIndex(h => h.Hostname == hostname);
        if (idx < 0) return;

        var target = idx + direction;
        if (target < 0 || target >= hosts.Count) return;

        var a = hosts[idx];
        var b = hosts[target];
        conn.Execute("UPDATE hosts SET sort_order = @o WHERE hostname = @h",
            new { o = b.SortOrder, h = a.Hostname });
        conn.Execute("UPDATE hosts SET sort_order = @o WHERE hostname = @h",
            new { o = a.SortOrder, h = b.Hostname });
    }

    // ── Services ─────────────────────────────────────────────

    public List<ServiceEntry> GetServices()
    {
        using var conn = Open();
        return conn.Query<ServiceEntry>("SELECT id, name, url FROM services ORDER BY id ASC");
    }

    public void AddService(string name, string url)
    {
        using var conn = Open();
        conn.Execute("INSERT INTO services (name, url) VALUES (@name, @url)",
            new { name, url });
    }

    public void DeleteService(int id)
    {
        using var conn = Open();
        conn.Execute("DELETE FROM services WHERE id = @id", new { id });
    }

    // ── Settings ──────────────────────────────────────────────

    public string? GetSetting(string key)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = @k";
        cmd.Parameters.AddWithValue("@k", key);
        var result = cmd.ExecuteScalar();
        return result as string;
    }

    public void SetSetting(string key, string value)
    {
        using var conn = Open();
        conn.Execute(
            "INSERT INTO settings (key, value) VALUES (@k, @v) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            new { k = key, v = value });
    }
}

// ── Minimal SQLite helpers (no Dapper) ───────────────────────

internal static class SqliteExtensions
{
    public static void Execute(this SqliteConnection conn, string sql, object? param = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        BindParams(cmd, param);
        cmd.ExecuteNonQuery();
    }

    public static void Execute(this SqliteConnection conn, string sql, double? p1, string p2)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@ping", p1.HasValue ? p1.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@h", p2);
        cmd.ExecuteNonQuery();
    }

    public static List<T> Query<T>(this SqliteConnection conn, string sql, object? param = null)
        where T : new()
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        BindParams(cmd, param);

        var result = new List<T>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(Map<T>(reader));
        return result;
    }

    public static T? QuerySingle<T>(this SqliteConnection conn, string sql, object? param = null)
        where T : new()
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        BindParams(cmd, param);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return default;
        return Map<T>(reader);
    }

    private static void BindParams(SqliteCommand cmd, object? param)
    {
        if (param == null) return;
        if (param is string s) { cmd.Parameters.AddWithValue("@h", s); return; }

        foreach (var prop in param.GetType().GetProperties())
        {
            var val = prop.GetValue(param);
            cmd.Parameters.AddWithValue("@" + ToCamel(prop.Name),
                val ?? (object)DBNull.Value);
        }
    }

    private static string ToCamel(string name) =>
        char.ToLower(name[0]) + name[1..];

    private static T Map<T>(SqliteDataReader r) where T : new()
    {
        var obj = new T();
        var cols = new HashSet<string>(Enumerable.Range(0, r.FieldCount).Select(r.GetName));
        foreach (var prop in typeof(T).GetProperties())
        {
            var col = ToSnake(prop.Name);
            if (!cols.Contains(col) || r.IsDBNull(r.GetOrdinal(col))) continue;
            var ord = r.GetOrdinal(col);
            var val = prop.PropertyType == typeof(double) || prop.PropertyType == typeof(double?)
                ? (object)r.GetDouble(ord)
                : prop.PropertyType == typeof(int) || prop.PropertyType == typeof(int?)
                ? (object)r.GetInt32(ord)
                : r.GetString(ord);
            prop.SetValue(obj, val);
        }
        return obj;
    }

    private static string ToSnake(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && sb.Length > 0) sb.Append('_');
            sb.Append(char.ToLower(c));
        }
        return sb.ToString();
    }
}
