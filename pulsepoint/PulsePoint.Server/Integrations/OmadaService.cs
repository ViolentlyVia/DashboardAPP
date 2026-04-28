using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PulsePoint.Models;

namespace PulsePoint.Integrations;

public class OmadaService
{
    private static readonly HttpClient _client = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    })
    { Timeout = TimeSpan.FromSeconds(30) };

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    // ── Token cache ───────────────────────────────────────────
    private string? _token;
    private DateTime _tokenExpiry = DateTime.MinValue;
    private readonly Lock _tokenLock = new();

    // ── Public entry point ────────────────────────────────────

    public async Task<OmadaSnapshot> FetchAsync(
        string baseUrl, string omadacId, string clientId, string clientSecret,
        string? preferredSiteId = null)
    {
        var snap = new OmadaSnapshot { FetchedAt = DateTime.UtcNow };
        try
        {
            var token = await EnsureTokenAsync(baseUrl, omadacId, clientId, clientSecret);
            if (token is null)
            {
                snap.Error = "Authentication failed — check client ID and secret";
                return snap;
            }

            // Sites
            var sites = await GetSitesAsync(baseUrl, omadacId, token);
            if (sites.Count == 0)
            {
                snap.Error = "No sites found on this controller";
                return snap;
            }
            snap.Connected = true;
            snap.Sites = sites;

            // Select site
            snap.SelectedSite = sites.FirstOrDefault(s => s.SiteId == preferredSiteId)
                                ?? sites[0];

            // Devices + clients for selected site in parallel
            var (devices, clients) = await FetchSiteDataAsync(
                baseUrl, omadacId, snap.SelectedSite.SiteId, token);

            snap.Devices = devices;
            snap.Clients = clients;
        }
        catch (Exception ex)
        {
            snap.Connected = false;
            snap.Error = ex.Message;
        }
        return snap;
    }

    // Fetch devices + clients for a specific site — used by the per-site refresh endpoint.
    public async Task<OmadaSnapshot> FetchSiteAsync(
        string baseUrl, string omadacId, string clientId, string clientSecret, string siteId)
    {
        var snap = new OmadaSnapshot { FetchedAt = DateTime.UtcNow };
        try
        {
            var token = await EnsureTokenAsync(baseUrl, omadacId, clientId, clientSecret);
            if (token is null) { snap.Error = "Authentication failed"; return snap; }

            snap.Connected = true;

            // Try to get the real site name from the sites list
            var sites = await GetSitesAsync(baseUrl, omadacId, token);
            snap.Sites = sites;
            snap.SelectedSite = sites.FirstOrDefault(s => s.SiteId == siteId)
                                ?? new OmadaSite { SiteId = siteId, Name = siteId };

            var (devices, clients) = await FetchSiteDataAsync(baseUrl, omadacId, siteId, token);
            snap.Devices = devices;
            snap.Clients = clients;
        }
        catch (Exception ex)
        {
            snap.Connected = false;
            snap.Error = ex.Message;
        }
        return snap;
    }

    // ── Authentication ────────────────────────────────────────

    private async Task<string?> EnsureTokenAsync(
        string baseUrl, string omadacId, string clientId, string clientSecret,
        bool forceRefresh = false)
    {
        lock (_tokenLock)
        {
            if (!forceRefresh && _token != null && DateTime.UtcNow < _tokenExpiry)
                return _token;
        }

        // Four fallback attempts to handle different Omada firmware API variations.
        var attempts = new Func<Task<string?>>[]
        {
            () => TryAuthAsync(baseUrl,
                $"?grant_type=client_credentials",
                new { omadacId, client_id = clientId, client_secret = clientSecret }),

            () => TryAuthAsync(baseUrl,
                $"?grant_type=client_credentials",
                new { omadac_id = omadacId, client_id = clientId, client_secret = clientSecret }),

            () => TryAuthAsync(baseUrl,
                $"?grant_type=client_credentials&client_id={Uri.EscapeDataString(clientId)}&client_secret={Uri.EscapeDataString(clientSecret)}",
                new { omadacId }),

            () => TryAuthAsync(baseUrl,
                $"?grant_type=client_credentials",
                new { omadacId, clientId, clientSecret }),
        };

        foreach (var attempt in attempts)
        {
            var token = await attempt();
            if (token is null) continue;

            lock (_tokenLock)
            {
                _token = token;
                _tokenExpiry = DateTime.UtcNow.AddSeconds(7200 - 120); // 2h minus buffer
            }
            return token;
        }
        return null;
    }

    private async Task<string?> TryAuthAsync(string baseUrl, string query, object body)
    {
        try
        {
            var url  = baseUrl.TrimEnd('/') + "/openapi/authorize/token" + query;
            var json = JsonSerializer.Serialize(body);
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            var resp = await _client.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var root = JsonSerializer.Deserialize<JsonElement>(
                await resp.Content.ReadAsStringAsync(), _json);

            if (root.TryGetProperty("errorCode", out var ec) && ec.GetInt32() != 0) return null;
            if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object) return null;
            if (!result.TryGetProperty("accessToken", out var at)) return null;
            return at.GetString();
        }
        catch { return null; }
    }

    // ── API helpers ───────────────────────────────────────────

    private async Task<List<OmadaSite>> GetSitesAsync(string baseUrl, string omadacId, string token)
    {
        var data = await OmadaGetAsync(baseUrl, omadacId,
            $"/openapi/v1/{omadacId}/sites?page=1&pageSize=100", token);
        return data.EnumerateArray().Select(s => new OmadaSite
        {
            SiteId   = s.GetStringOrEmpty("siteId"),
            Name     = s.GetStringOrEmpty("name"),
            Scenario = s.GetStringOrEmpty("scenario")
        }).ToList();
    }

    private async Task<(List<OmadaDevice> devices, List<OmadaClient> clients)> FetchSiteDataAsync(
        string baseUrl, string omadacId, string siteId, string token)
    {
        var deviceTask = GetDevicesAsync(baseUrl, omadacId, siteId, token);
        var clientTask = GetClientsAsync(baseUrl, omadacId, siteId, token);
        await Task.WhenAll(deviceTask, clientTask);
        return (await deviceTask, await clientTask);
    }

    private async Task<List<OmadaDevice>> GetDevicesAsync(
        string baseUrl, string omadacId, string siteId, string token)
    {
        var data = await OmadaGetAsync(baseUrl, omadacId,
            $"/openapi/v1/{omadacId}/sites/{siteId}/devices?page=1&pageSize=200", token);
        return [.. data.EnumerateArray().Select(d => new OmadaDevice
        {
            Mac             = d.GetStringOrEmpty("mac"),
            Name            = d.GetStringOrEmpty("name"),
            Type            = d.GetStringOrEmpty("type"),
            Ip              = d.GetStringOrEmpty("ip"),
            Model           = d.GetStringOrEmpty("model"),
            FirmwareVersion = d.GetStringOrEmpty("firmwareVersion"),
            // "connStatus" (newer firmware) or "status" — both: 0=offline, 1=online
            Status          = d.TryGetProperty("connStatus", out var cst)
                                ? cst.GetInt32Safe("")
                                : d.GetInt32Safe("status"),
            // "uptimeLong" is milliseconds on some firmware; "uptime" is seconds
            Uptime          = d.TryGetProperty("uptimeLong", out var utl)
                                ? utl.GetInt64Safe("") / 1000
                                : d.GetInt64Safe("uptime"),
            ClientCount     = d.TryGetProperty("clientNum", out var cn)
                                ? (cn.ValueKind == JsonValueKind.Number && cn.TryGetInt32(out var cni) ? cni : 0)
                                : d.GetInt32Safe("clients"),
            Download        = d.GetInt64Safe("download"),
            Upload          = d.GetInt64Safe("upload")
        })];
    }

    private async Task<List<OmadaClient>> GetClientsAsync(
        string baseUrl, string omadacId, string siteId, string token)
    {
        var data = await OmadaGetAsync(baseUrl, omadacId,
            $"/openapi/v1/{omadacId}/sites/{siteId}/clients?page=1&pageSize=200", token);
        return [.. data.EnumerateArray().Select(c =>
        {
            var signalLevel = c.TryGetProperty("signalLevel", out var sl) ? sl.GetInt32Safe("") :
                              c.TryGetProperty("rssi", out var rs) ? rs.GetInt32Safe("") :
                              c.GetInt32Safe("signal");

            var rxRate = c.TryGetProperty("rxRate", out var rx) ? rx.GetInt64Safe("") :
                         c.TryGetProperty("download", out var dl) ? dl.GetInt64Safe("") :
                         c.GetInt64Safe("downRate");

            var txRate = c.TryGetProperty("txRate", out var tx) ? tx.GetInt64Safe("") :
                         c.TryGetProperty("upload", out var ul) ? ul.GetInt64Safe("") :
                         c.GetInt64Safe("upRate");

            var wiredSpeed = c.TryGetProperty("wiredLinkSpeed", out var ws) ? ws.GetInt32Safe("") :
                             c.TryGetProperty("linkSpeed", out var ls) ? ls.GetInt32Safe("") :
                             c.TryGetProperty("portSpeed", out var ps) ? ps.GetInt32Safe("") :
                             c.GetInt32Safe("speed");

            // Cumulative traffic bytes — field name varies across Omada firmware versions
            var trafficDown =
                c.TryGetProperty("trafficDown",        out var td1) ? td1.GetInt64Safe("") :
                c.TryGetProperty("trafficDownload",    out var td2) ? td2.GetInt64Safe("") :
                c.TryGetProperty("downTraffic",        out var td3) ? td3.GetInt64Safe("") :
                c.TryGetProperty("downTrafficBytes",   out var td4) ? td4.GetInt64Safe("") :
                c.TryGetProperty("totalDownload",      out var td5) ? td5.GetInt64Safe("") :
                0L;

            var trafficUp =
                c.TryGetProperty("trafficUp",          out var tu1) ? tu1.GetInt64Safe("") :
                c.TryGetProperty("trafficUpload",      out var tu2) ? tu2.GetInt64Safe("") :
                c.TryGetProperty("upTraffic",          out var tu3) ? tu3.GetInt64Safe("") :
                c.TryGetProperty("upTrafficBytes",     out var tu4) ? tu4.GetInt64Safe("") :
                c.TryGetProperty("totalUpload",        out var tu5) ? tu5.GetInt64Safe("") :
                0L;

            return new OmadaClient
            {
                Mac            = c.GetStringOrEmpty("mac"),
                Name           = c.GetStringOrEmpty("name"),
                Ip             = c.GetStringOrEmpty("ip"),
                NetworkName    = c.GetStringOrEmpty("networkName"),
                Ssid           = c.GetStringOrEmpty("ssid"),
                Wireless       = c.TryGetProperty("wireless", out var w)
                                     ? w.ValueKind == JsonValueKind.True
                                     : !string.IsNullOrEmpty(c.GetStringOrEmpty("ssid")),
                SignalLevel    = signalLevel,
                RxRate         = rxRate,
                TxRate         = txRate,
                WiredLinkSpeed = wiredSpeed,
                Uptime         = c.GetInt64Safe("uptime"),
                Active         = c.TryGetProperty("active", out var ac)
                                     ? ac.ValueKind == JsonValueKind.True
                                     : true,
                TrafficDown    = trafficDown,
                TrafficUp      = trafficUp
            };
        })];
    }

    // Calls an Omada OpenAPI endpoint, handles errorCode -44112/-44113 token expiry by
    // refreshing once and retrying, then returns the result.data array.
    private async Task<JsonElement> OmadaGetAsync(
        string baseUrl, string omadacId, string path, string token)
    {
        var result = await RawOmadaGetAsync(baseUrl, path, token);

        // -44112 / -44113 = token invalid/expired — shouldn't normally happen since we
        // track expiry, but Omada firmware sometimes invalidates tokens earlier.
        if (result.errorCode is -44112 or -44113)
        {
            lock (_tokenLock) { _token = null; _tokenExpiry = DateTime.MinValue; }
            throw new Exception($"Omada token expired (errorCode {result.errorCode}), retry scheduled");
        }

        if (result.errorCode != 0)
            throw new Exception($"Omada API error {result.errorCode}: {result.msg}");

        return result.data;
    }

    private async Task<(int errorCode, string msg, JsonElement data)> RawOmadaGetAsync(
        string baseUrl, string path, string token)
    {
        var url = baseUrl.TrimEnd('/') + path;
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"AccessToken={token}");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var resp = await _client.SendAsync(req);
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            return (-44112, "Unauthorized", default);

        var root = JsonSerializer.Deserialize<JsonElement>(
            await resp.Content.ReadAsStringAsync(), _json);

        var errorCode = root.TryGetProperty("errorCode", out var ec) ? ec.GetInt32() : 0;
        var msg       = root.TryGetProperty("msg", out var m) ? m.GetString() ?? "" : "";

        JsonElement data = default;
        if (root.TryGetProperty("result", out var res) && res.ValueKind == JsonValueKind.Object)
        {
            // Omada paginates via "dataList"; single-item responses use "data"
            if (res.TryGetProperty("dataList", out var dl) && dl.ValueKind == JsonValueKind.Array)
                data = dl;
            else if (res.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array)
                data = d;
        }

        return (errorCode, msg, data);
    }
}

