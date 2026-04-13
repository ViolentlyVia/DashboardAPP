using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SysDash.NetCore.Backend;

public sealed partial class AppState
{
    public void PrimeServiceCache()
    {
        if (_serviceUpdatedAt != 0)
        {
            return;
        }

        var initial = CollectServiceStatusesAsync().GetAwaiter().GetResult();
        SetServiceCache(initial);
    }

    public void StartServiceMonitor(CancellationToken token)
    {
        if (_serviceMonitorStarted)
        {
            return;
        }

        _serviceMonitorStarted = true;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var results = await CollectServiceStatusesAsync();
                    SetServiceCache(results);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Service monitor iteration failed");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public void StartUnraidMonitor(CancellationToken token)
    {
        if (_unraidMonitorStarted)
        {
            return;
        }

        _unraidMonitorStarted = true;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var fetched = await FetchUnraidSnapshotAsync();
                    SetUnraidSnapshot(fetched.normalized);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unraid monitor iteration failed");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(120), token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public void PrimeIdracCache()
    {
        lock (_idracLock)
        {
            if (_idracSnapshot.TryGetValue("fetched_at", out var fetched)
                && fetched is long fetchedAt
                && fetchedAt > 0)
            {
                return;
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var initial = await FetchIdracSummaryAsync();
                SetIdracSnapshot(initial);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Initial iDRAC cache prime failed");
            }
        });
    }

    public void StartIdracMonitor(CancellationToken token)
    {
        if (_idracMonitorStarted)
        {
            return;
        }

        _idracMonitorStarted = true;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var fetched = await FetchIdracSummaryAsync();
                    SetIdracSnapshot(fetched);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "iDRAC monitor iteration failed");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public void PrimeOmadaCache()
    {
        lock (_omadaLock)
        {
            if (_omadaSnapshot.TryGetValue("fetched_at", out var fetched)
                && fetched is long fetchedAt
                && fetchedAt > 0)
            {
                return;
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var initial = await FetchOmadaSnapshotAsync();
                SetOmadaSnapshot(initial);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Initial Omada cache prime failed");
            }
        });
    }

    public void StartOmadaMonitor(CancellationToken token)
    {
        if (_omadaMonitorStarted)
        {
            return;
        }

        _omadaMonitorStarted = true;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var fetched = await FetchOmadaSnapshotAsync();
                    SetOmadaSnapshot(fetched);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Omada monitor iteration failed");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public List<object> GetDockerStatus()
    {
        var output = new List<object>();
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "ps --format \"{{.Names}}||{{.State}}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);
            if (process.ExitCode == 0)
            {
                foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var parts = line.Split("||", 2, StringSplitOptions.TrimEntries);
                    if (parts.Length == 2)
                    {
                        var shortState = parts[1].StartsWith("up", StringComparison.OrdinalIgnoreCase) ? "ok" : "down";
                        output.Add(new { name = parts[0], state = shortState });
                    }
                }
            }
            else
            {
                output.Add(new { name = "Docker service", state = "down" });
            }
        }
        catch
        {
            output.Add(new { name = "Docker service", state = "down" });
        }

        return output;
    }

    public async Task<List<Dictionary<string, object?>>> CollectServiceStatusesAsync()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var result = new List<Dictionary<string, object?>>(_services.Count);

        foreach (var (name, ip) in _services)
        {
            var alive = await IsHostAliveAsync(ip);
            string status;
            string? offlineFor = null;

            if (alive)
            {
                _serviceMissed[name] = 0;
                _serviceOfflineSince[name] = null;
                status = "ok";
            }
            else
            {
                _serviceMissed[name] = _serviceMissed.GetValueOrDefault(name) + 1;
                if (_serviceOfflineSince[name] is null)
                {
                    _serviceOfflineSince[name] = now;
                }

                status = _serviceMissed[name] == 1 ? "warn" : "down";
                offlineFor = FormatDuration(now - (_serviceOfflineSince[name] ?? now));
            }

            result.Add(new Dictionary<string, object?>
            {
                ["name"] = name,
                ["ip"] = ip,
                ["status"] = status,
                ["offline_for"] = offlineFor,
            });
        }

        return result;
    }

    public (List<Dictionary<string, object?>> items, long updatedAt) GetServiceCacheSnapshot()
    {
        lock (_serviceLock)
        {
            return (new List<Dictionary<string, object?>>(_serviceCache), _serviceUpdatedAt);
        }
    }

    public void SetServiceCache(List<Dictionary<string, object?>> items)
    {
        lock (_serviceLock)
        {
            _serviceCache = items;
            _serviceUpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }

    private static async Task<bool> IsHostAliveAsync(string ip)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, 1000);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return await IsHostAliveSocketAsync(ip, 80, 1_000) || await IsHostAliveSocketAsync(ip, 443, 1_000);
        }
    }

    private static async Task<bool> IsHostAliveSocketAsync(string host, int port, int timeoutMs)
    {
        using var client = new TcpClient();
        try
        {
            var connectTask = client.ConnectAsync(host, port);
            var done = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
            return done == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatDuration(long seconds)
    {
        seconds = Math.Max(0, seconds);
        var hours = seconds / 3600;
        var remainder = seconds % 3600;
        var minutes = remainder / 60;
        var secs = remainder % 60;
        if (hours > 0)
        {
            return $"{hours}h {minutes}m";
        }

        if (minutes > 0)
        {
            return $"{minutes}m {secs}s";
        }

        return $"{secs}s";
    }
}
