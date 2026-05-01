using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PulsePoint.Models;

namespace PulsePoint.Integrations;

public class UnraidService
{
    private static readonly HttpClient _client = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    })
    { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    // Matches the SysDash GraphQL query structure — vms uses "domains", shares use "free"/"size".
    private const string Query = @"query DashboardData {
  array {
    state
    capacity { kilobytes { total free used } }
    disks { id name device type status temp size }
    parities { id name device status temp size }
  }
  docker {
    containers { id names state status image autoStart }
  }
  shares { name free size }
  vms {
    domains { uuid name state }
  }
}";

    public async Task<UnraidSnapshot> FetchAsync(string host, string apiKey, string? apiKeyId = null, string? bearer = null)
    {
        var snap = new UnraidSnapshot { FetchedAt = DateTime.UtcNow };
        try
        {
            var body = JsonSerializer.Serialize(new { query = Query });
            // Unraid GraphQL endpoint is at /graphql (no /api/ prefix)
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/graphql")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("x-api-key", apiKey);
            if (!string.IsNullOrEmpty(apiKeyId))
                req.Headers.Add("x-api-key-id", apiKeyId);
            if (!string.IsNullOrEmpty(bearer))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

            using var resp = await _client.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            var root = JsonSerializer.Deserialize<JsonElement>(
                await resp.Content.ReadAsStringAsync(), _json);

            if (!root.TryGetProperty("data", out var data))
            {
                snap.Error = root.TryGetProperty("errors", out var errs) ? errs.ToString() : "No data in response";
                return snap;
            }

            snap.Connected = true;

            // ── Array ──────────────────────────────────────────
            if (data.TryGetProperty("array", out var arr))
            {
                snap.Array.State = arr.GetStringOrEmpty("state");
                if (arr.TryGetProperty("capacity", out var cap) &&
                    cap.TryGetProperty("kilobytes", out var kb))
                {
                    snap.Array.UsedBytes  = kb.GetInt64Safe("used")  * 1024;
                    snap.Array.FreeBytes  = kb.GetInt64Safe("free")  * 1024;
                    snap.Array.TotalBytes = kb.GetInt64Safe("total") * 1024;
                }
                if (arr.TryGetProperty("disks", out var disks))
                    snap.Disks = ParseDisks(disks);
                if (arr.TryGetProperty("parities", out var parities))
                    snap.Parities = ParseDisks(parities);
            }

            // ── Docker ─────────────────────────────────────────
            if (data.TryGetProperty("docker", out var docker) &&
                docker.TryGetProperty("containers", out var containers))
            {
                snap.Containers = [.. containers.EnumerateArray().Select(c =>
                {
                    // names can be a JSON array or a plain string
                    var names = c.TryGetProperty("names", out var n)
                        ? (n.ValueKind == JsonValueKind.Array
                            ? string.Join(", ", n.EnumerateArray().Select(x => x.GetString() ?? ""))
                            : n.GetString() ?? "")
                        : "";
                    return new DockerContainer
                    {
                        Id     = c.GetStringOrEmpty("id"),
                        Names  = names.TrimStart('/'),
                        Image  = c.GetStringOrEmpty("image"),
                        State  = c.GetStringOrEmpty("state"),
                        Status = c.GetStringOrEmpty("status")
                    };
                })];
            }

            // ── VMs ────────────────────────────────────────────
            if (data.TryGetProperty("vms", out var vms) &&
                vms.TryGetProperty("domains", out var domains))
            {
                snap.Vms = [.. domains.EnumerateArray().Select(v => new VmDomain
                {
                    Name  = v.GetStringOrEmpty("name"),
                    State = v.GetStringOrEmpty("state")
                })];
            }

            // ── Shares ─────────────────────────────────────────
            if (data.TryGetProperty("shares", out var shares))
            {
                snap.Shares = [.. shares.EnumerateArray().Select(s => new ShareInfo
                {
                    Name   = s.GetStringOrEmpty("name"),
                    FreeKb = s.GetInt64Safe("free"),
                    SizeKb = s.GetInt64Safe("size")
                })];
            }
        }
        catch (Exception ex)
        {
            snap.Connected = false;
            snap.Error = ex.Message;
        }
        return snap;
    }

    private static List<DiskInfo> ParseDisks(JsonElement disks) =>
        [.. disks.EnumerateArray().Select(d => new DiskInfo
        {
            Name   = d.GetStringOrEmpty("name"),
            Device = d.GetStringOrEmpty("device"),
            Status = d.GetStringOrEmpty("status"),
            Temp   = d.GetInt32Safe("temp"),
            Size   = d.GetInt64Safe("size"),
            Type   = d.GetStringOrEmpty("type")
        })];

    // Docker/VM control via GraphQL mutations — best-effort
    public async Task<bool> DockerActionAsync(string host, string apiKey, string containerId, string action)
    {
        try
        {
            var mutation = $@"mutation {{ docker {{ container(id: ""{containerId}"") {{ {action} }} }} }}";
            return await PostMutationAsync(host, apiKey, mutation);
        }
        catch { return false; }
    }

    public async Task<bool> VmActionAsync(string host, string apiKey, string vmName, string action)
    {
        try
        {
            var mutation = $@"mutation {{ vms {{ domain(name: ""{vmName}"") {{ {action} }} }} }}";
            return await PostMutationAsync(host, apiKey, mutation);
        }
        catch { return false; }
    }

    private async Task<bool> PostMutationAsync(string host, string apiKey, string mutation)
    {
        var body = JsonSerializer.Serialize(new { query = mutation });
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://{host}/graphql")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-api-key", apiKey);
        using var resp = await _client.SendAsync(req);
        return resp.IsSuccessStatusCode;
    }
}
