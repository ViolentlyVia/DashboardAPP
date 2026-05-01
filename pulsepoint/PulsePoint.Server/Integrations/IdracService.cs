using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PulsePoint.Models;

namespace PulsePoint.Integrations;

public class IdracService
{
    // iDRAC 8 always uses HTTPS with a self-signed cert.
    // 30s per-request: iDRAC 8 can be very slow, especially under storage enumeration.
    private static readonly HttpClient _client = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    })
    { Timeout = TimeSpan.FromSeconds(30) };

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public async Task<IdracSnapshot> FetchAsync(string host, string username, string password)
    {
        var snap = new IdracSnapshot { FetchedAt = DateTime.UtcNow };
        // Auth header sent per-request to keep the static client thread-safe.
        var authHeader = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));

        try
        {
            var baseUrl = $"https://{host}";

            // ── Discover resource paths from service root ──────
            var root        = await GetAsync(baseUrl, "/redfish/v1", authHeader);
            var systemPath  = await ResolveFirstMemberAsync(baseUrl, root, "Systems",  authHeader);
            var managerPath = await ResolveFirstMemberAsync(baseUrl, root, "Managers", authHeader);
            var chassisPath = await ResolveFirstMemberAsync(baseUrl, root, "Chassis",  authHeader);

            // ── System info ────────────────────────────────────
            var sys = await GetAsync(baseUrl, systemPath, authHeader);
            snap.System = new SystemInfo
            {
                Manufacturer = sys.GetStringOrEmpty("Manufacturer"),
                Model        = sys.GetStringOrEmpty("Model"),
                ServiceTag   = sys.GetStringOrEmpty("SerialNumber"),
                BiosVersion  = sys.GetStringOrEmpty("BiosVersion"),
                PowerState   = sys.GetStringOrEmpty("PowerState"),
                HealthStatus = GetHealth(sys)
            };
            if (sys.TryGetProperty("ProcessorSummary", out var ps))
                snap.System.ProcessorCount = ps.GetInt32Safe("Count");
            if (sys.TryGetProperty("MemorySummary", out var ms) &&
                ms.TryGetProperty("TotalSystemMemoryGiB", out var memEl))
                snap.System.TotalMemoryGiB = memEl.TryGetInt64(out var g) ? g :
                    (memEl.TryGetDouble(out var d) ? (long)d : 0);

            snap.Connected = true;

            // ── Manager — iDRAC firmware version ──────────────
            try
            {
                var mgr = await GetAsync(baseUrl, managerPath, authHeader);
                snap.System.IdracFirmware = mgr.GetStringOrEmpty("FirmwareVersion");
            }
            catch { /* not critical */ }

            // ── Thermal ────────────────────────────────────────
            try
            {
                var thermal = await GetAsync(baseUrl, chassisPath + "/Thermal", authHeader);
                if (thermal.TryGetProperty("Temperatures", out var temps))
                    snap.Temperatures = [.. temps.EnumerateArray()
                        .Where(t => t.TryGetProperty("ReadingCelsius", out _))
                        .Select(t => new ThermalSensor
                        {
                            Name                   = t.GetStringOrEmpty("Name"),
                            ReadingCelsius         = t.GetDoubleSafe("ReadingCelsius"),
                            UpperThresholdCritical = t.GetDoubleNullable("UpperThresholdCritical"),
                            Status                 = GetHealth(t)
                        })];

                if (thermal.TryGetProperty("Fans", out var fans))
                    snap.Fans = [.. fans.EnumerateArray().Select(f => new FanInfo
                    {
                        Name = f.GetStringOrEmpty("FanName"),
                        // iDRAC 8 reports RPM as "ReadingRPM"; fall back to "Reading"
                        Rpm  = f.TryGetProperty("ReadingRPM", out var rpmEl)
                                   ? (rpmEl.TryGetInt32(out var r) ? r : 0)
                                   : f.GetInt32Safe("Reading"),
                        Status = GetHealth(f)
                    })];
            }
            catch { /* thermal not critical */ }

            // ── Power supplies ─────────────────────────────────
            try
            {
                var power = await GetAsync(baseUrl, chassisPath + "/Power", authHeader);
                if (power.TryGetProperty("PowerSupplies", out var psus))
                    snap.PowerSupplies = [.. psus.EnumerateArray().Select(p => new PowerSupplyInfo
                    {
                        Name               = p.GetStringOrEmpty("Name"),
                        Model              = p.GetStringOrEmpty("Model"),
                        // iDRAC 8 uses LastPowerOutputWatts; fall back to PowerInputWatts
                        LastOutputWatts    = p.GetDoubleNullable("LastPowerOutputWatts")
                                            ?? p.GetDoubleNullable("PowerInputWatts"),
                        PowerCapacityWatts = p.GetDoubleNullable("PowerCapacityWatts"),
                        Status             = GetHealth(p)
                    })];
            }
            catch { /* power not critical */ }

            // ── Storage: System.Storage → Controllers → Drives ─
            // Phase 1: collect all drive @odata.id paths across every controller.
            // Phase 2: fetch all drives in parallel so iDRAC latency doesn't multiply.
            try
            {
                if (sys.TryGetProperty("Storage", out var storageLinkEl) &&
                    storageLinkEl.TryGetProperty("@odata.id", out var storageId))
                {
                    var storageColl = await GetAsync(baseUrl, storageId.GetString()!, authHeader);
                    if (storageColl.TryGetProperty("Members", out var ctrls))
                    {
                        // Fetch all controllers in parallel
                        var ctrlPaths = ctrls.EnumerateArray()
                            .Where(m => m.TryGetProperty("@odata.id", out _))
                            .Select(m => m.GetProperty("@odata.id").GetString()!)
                            .ToList();

                        var ctrlResults = await Task.WhenAll(
                            ctrlPaths.Select(path => TryGetAsync(baseUrl, path, authHeader)));

                        // Collect every unique drive path mentioned by any controller
                        var drivePaths = new HashSet<string>();
                        foreach (var ctrl in ctrlResults.Where(r => r.HasValue))
                        {
                            if (!ctrl!.Value.TryGetProperty("Drives", out var driveLinks)) continue;
                            foreach (var link in driveLinks.EnumerateArray())
                            {
                                if (link.TryGetProperty("@odata.id", out var did))
                                    drivePaths.Add(did.GetString()!);
                            }
                        }

                        // Fetch all drives in parallel
                        var driveResults = await Task.WhenAll(
                            drivePaths.Select(path => TryGetAsync(baseUrl, path, authHeader)));

                        snap.Drives = [.. driveResults
                            .Where(r => r.HasValue)
                            .Select(r => r!.Value)
                            .Select(drv => new StorageDrive
                            {
                                Name          = drv.GetStringOrEmpty("Name"),
                                Model         = drv.GetStringOrEmpty("Model"),
                                Manufacturer  = drv.GetStringOrEmpty("Manufacturer"),
                                SerialNumber  = drv.GetStringOrEmpty("SerialNumber"),
                                MediaType     = drv.GetStringOrEmpty("MediaType"),
                                Protocol      = drv.GetStringOrEmpty("Protocol"),
                                CapacityBytes = drv.GetInt64Safe("CapacityBytes"),
                                Health        = GetHealth(drv),
                                State         = GetState(drv)
                            })
                            .OrderBy(d => d.Name)];
                    }
                }
            }
            catch { /* storage not critical */ }
        }
        catch (Exception ex)
        {
            snap.Connected = false;
            snap.Error = ex.Message;
        }
        return snap;
    }

    private async Task<JsonElement> GetAsync(string baseUrl, string path, AuthenticationHeaderValue auth)
    {
        var url = path.StartsWith("http") ? path : baseUrl + path;
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = auth;
        using var resp = await _client.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<JsonElement>(
            await resp.Content.ReadAsStringAsync(), _json);
    }

    // Returns null instead of throwing — used for parallel fetches where partial failure is acceptable.
    private async Task<JsonElement?> TryGetAsync(string baseUrl, string path, AuthenticationHeaderValue auth)
    {
        try { return await GetAsync(baseUrl, path, auth); }
        catch { return null; }
    }

    // Fetches the collection, then returns the @odata.id of the first member.
    private async Task<string> ResolveFirstMemberAsync(
        string baseUrl, JsonElement root, string collection, AuthenticationHeaderValue auth)
    {
        var fallback = $"/redfish/v1/{collection}/1";
        if (!root.TryGetProperty(collection, out var coll) ||
            !coll.TryGetProperty("@odata.id", out var collId))
            return fallback;

        try
        {
            var collData = await GetAsync(baseUrl, collId.GetString()!, auth);
            if (collData.TryGetProperty("Members", out var members))
            {
                var first = members.EnumerateArray().FirstOrDefault();
                if (first.ValueKind != JsonValueKind.Undefined &&
                    first.TryGetProperty("@odata.id", out var memberId))
                    return memberId.GetString() ?? fallback;
            }
        }
        catch { /* use fallback */ }
        return fallback;
    }

    private static string GetHealth(JsonElement el)
    {
        if (!el.TryGetProperty("Status", out var s)) return "";
        if (s.ValueKind == JsonValueKind.String) return s.GetString() ?? "";
        if (s.TryGetProperty("Health", out var h)) return h.GetString() ?? "";
        return "";
    }

    private static string GetState(JsonElement el)
    {
        if (!el.TryGetProperty("Status", out var s)) return "";
        if (s.TryGetProperty("State", out var st)) return st.GetString() ?? "";
        return "";
    }
}
