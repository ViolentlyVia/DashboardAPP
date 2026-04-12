using Microsoft.Data.Sqlite;

namespace SysDash.NetCore.Backend;

public sealed partial class AppState
{
    public void InitializeDatabase()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS hosts (
    hostname TEXT PRIMARY KEY,
    ip TEXT,
    uptime REAL,
    last_seen REAL,
    ping REAL,
    cpu REAL,
    memory REAL
);";
        cmd.ExecuteNonQuery();

        EnsureColumn(conn, "hosts", "friendly_name", "TEXT");
        EnsureColumn(conn, "hosts", "sort_order", "INTEGER");
        EnsureColumn(conn, "hosts", "rdp_url", "TEXT");
    }

    public void UpsertHostReport(string hostname, string ip, double uptime, double? cpu, double? memory)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO hosts (hostname, ip, uptime, last_seen, cpu, memory)
VALUES ($hostname, $ip, $uptime, $lastSeen, $cpu, $memory)
ON CONFLICT(hostname) DO UPDATE SET
  ip = excluded.ip,
  uptime = excluded.uptime,
  last_seen = excluded.last_seen,
  cpu = COALESCE(excluded.cpu, hosts.cpu),
  memory = COALESCE(excluded.memory, hosts.memory);";
        cmd.Parameters.AddWithValue("$hostname", hostname);
        cmd.Parameters.AddWithValue("$ip", ip);
        cmd.Parameters.AddWithValue("$uptime", uptime);
        cmd.Parameters.AddWithValue("$lastSeen", now);
        cmd.Parameters.AddWithValue("$cpu", (object?)cpu ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$memory", (object?)memory ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<(string hostname, string ip, double? uptime, double? lastSeen, double? cpu, double? memory, string? friendlyName, string? rdpUrl)> GetHostsForStatus()
    {
        var results = new List<(string, string, double?, double?, double?, double?, string?, string?)>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT hostname, ip, uptime, last_seen, cpu, memory, friendly_name, rdp_url FROM hosts";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add((
                reader.GetString(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDouble(2),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return results;
    }

    public void UpdatePing(string hostname, double? pingMs)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE hosts SET ping = $ping WHERE hostname = $hostname";
        cmd.Parameters.AddWithValue("$ping", (object?)pingMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hostname", hostname);
        cmd.ExecuteNonQuery();
    }

    public long GetMostRecentCheckin()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(last_seen) FROM hosts";
        var value = cmd.ExecuteScalar();
        if (value is null || value is DBNull)
        {
            return 0;
        }

        return Convert.ToInt64(Convert.ToDouble(value));
    }

    public List<object> GetAssets()
    {
        var output = new List<object>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT hostname, ip, uptime, last_seen, cpu, memory, friendly_name, sort_order, rdp_url
FROM hosts ORDER BY COALESCE(sort_order, 99999), hostname";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var hostname = reader.GetString(0);
            var ip = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var rdpUrl = reader.IsDBNull(8) ? null : reader.GetString(8);
            output.Add(new
            {
                hostname,
                ip,
                uptime = reader.IsDBNull(2) ? (double?)null : reader.GetDouble(2),
                last_seen = reader.IsDBNull(3) ? (double?)null : reader.GetDouble(3),
                cpu_percent = reader.IsDBNull(4) ? (double?)null : reader.GetDouble(4),
                memory_percent = reader.IsDBNull(5) ? (double?)null : reader.GetDouble(5),
                friendly_name = reader.IsDBNull(6) ? null : reader.GetString(6),
                sort_order = reader.IsDBNull(7) ? (long?)null : reader.GetInt64(7),
                rdp_url = rdpUrl,
                rdp_launch_url = BuildRdpLaunchUrl(hostname, ip, rdpUrl),
            });
        }

        return output;
    }

    public void UpdateAsset(string hostname, string? friendlyName, string? newIp, string? rdpUrl)
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();

        if (friendlyName is not null)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE hosts SET friendly_name = $friendly WHERE hostname = $hostname";
            cmd.Parameters.AddWithValue("$friendly", friendlyName);
            cmd.Parameters.AddWithValue("$hostname", hostname);
            cmd.ExecuteNonQuery();
        }

        if (newIp is not null)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE hosts SET ip = $ip WHERE hostname = $hostname";
            cmd.Parameters.AddWithValue("$ip", newIp);
            cmd.Parameters.AddWithValue("$hostname", hostname);
            cmd.ExecuteNonQuery();
        }

        if (rdpUrl is not null)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE hosts SET rdp_url = $rdp WHERE hostname = $hostname";
            cmd.Parameters.AddWithValue("$rdp", string.IsNullOrWhiteSpace(rdpUrl) ? DBNull.Value : rdpUrl.Trim());
            cmd.Parameters.AddWithValue("$hostname", hostname);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public void DeleteAsset(string hostname)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM hosts WHERE hostname = $hostname";
        cmd.Parameters.AddWithValue("$hostname", hostname);
        cmd.ExecuteNonQuery();
    }

    public bool MoveAsset(string hostname, bool moveUp)
    {
        using var conn = OpenConnection();

        using var getCmd = conn.CreateCommand();
        getCmd.CommandText = "SELECT sort_order FROM hosts WHERE hostname = $hostname";
        getCmd.Parameters.AddWithValue("$hostname", hostname);
        var currentVal = getCmd.ExecuteScalar();
        if (currentVal is null || currentVal is DBNull)
        {
            return false;
        }

        var current = Convert.ToInt64(currentVal);
        var compare = moveUp ? "<" : ">";
        var order = moveUp ? "DESC" : "ASC";
        using var neighborCmd = conn.CreateCommand();
        neighborCmd.CommandText = $@"SELECT hostname, COALESCE(sort_order, 99999)
FROM hosts
WHERE COALESCE(sort_order, 99999) {compare} $current
ORDER BY COALESCE(sort_order, 99999) {order}
LIMIT 1";
        neighborCmd.Parameters.AddWithValue("$current", current);
        using var reader = neighborCmd.ExecuteReader();
        if (!reader.Read())
        {
            return true;
        }

        var neighborHost = reader.GetString(0);
        var neighborOrder = reader.GetInt64(1);
        reader.Close();

        using var tx = conn.BeginTransaction();
        using (var cmdA = conn.CreateCommand())
        {
            cmdA.CommandText = "UPDATE hosts SET sort_order = $newOrder WHERE hostname = $hostname";
            cmdA.Parameters.AddWithValue("$newOrder", neighborOrder);
            cmdA.Parameters.AddWithValue("$hostname", hostname);
            cmdA.ExecuteNonQuery();
        }

        using (var cmdB = conn.CreateCommand())
        {
            cmdB.CommandText = "UPDATE hosts SET sort_order = $newOrder WHERE hostname = $hostname";
            cmdB.Parameters.AddWithValue("$newOrder", current);
            cmdB.Parameters.AddWithValue("$hostname", neighborHost);
            cmdB.ExecuteNonQuery();
        }

        tx.Commit();
        return true;
    }

    public object GetSummaryPayload()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        var hosts = new List<object>();

        using (var conn = OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT hostname, ip, uptime, last_seen, ping, cpu, memory, friendly_name, sort_order, rdp_url
FROM hosts ORDER BY COALESCE(sort_order, 99999), hostname";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var hostname = reader.GetString(0);
                var ip = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var lastSeen = reader.IsDBNull(3) ? (double?)null : reader.GetDouble(3);
                var lastSeenAgo = lastSeen.HasValue ? (int)(now - lastSeen.Value) : (int?)null;
                var rdpConfigured = reader.IsDBNull(9) ? null : reader.GetString(9);

                hosts.Add(new
                {
                    hostname,
                    friendly_name = reader.IsDBNull(7) ? null : reader.GetString(7),
                    ip,
                    online = lastSeen.HasValue && (now - lastSeen.Value) < 90,
                    last_seen = lastSeen,
                    last_seen_ago_s = lastSeenAgo,
                    uptime_s = reader.IsDBNull(2) ? (double?)null : reader.GetDouble(2),
                    ping_ms = reader.IsDBNull(4) ? (double?)null : reader.GetDouble(4),
                    cpu_percent = reader.IsDBNull(5) ? (double?)null : reader.GetDouble(5),
                    memory_percent = reader.IsDBNull(6) ? (double?)null : reader.GetDouble(6),
                    rdp_url = BuildRdpLaunchUrl(hostname, ip, rdpConfigured),
                });
            }
        }

        var serviceSnapshot = GetServiceCacheSnapshot();
        var unraid = GetUnraidSnapshot();
        var idrac = GetIdracSnapshot();
        unraid.Remove("sources");

        return new
        {
            generated_at = now,
            api_build = ApiBuild,
            hosts,
            services = new
            {
                updated_at = (double)serviceSnapshot.updatedAt,
                items = serviceSnapshot.items,
            },
            unraid,
            idrac,
        };
    }

    public string? BuildRdpLaunchUrl(string hostname, string ip, string? configuredRdpUrl)
    {
        if (!string.IsNullOrWhiteSpace(configuredRdpUrl))
        {
            return configuredRdpUrl.Trim();
        }

        if (string.IsNullOrWhiteSpace(GuacamoleTemplate))
        {
            return null;
        }

        try
        {
            return GuacamoleTemplate
                .Replace("{hostname}", Uri.EscapeDataString(hostname ?? string.Empty), StringComparison.Ordinal)
                .Replace("{ip}", Uri.EscapeDataString(ip ?? string.Empty), StringComparison.Ordinal);
        }
        catch
        {
            return null;
        }
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        return conn;
    }

    private static void EnsureColumn(SqliteConnection conn, string table, string column, string type)
    {
        using var check = conn.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table});";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
        alter.ExecuteNonQuery();
    }
}
