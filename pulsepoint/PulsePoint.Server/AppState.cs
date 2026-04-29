using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using PulsePoint.Data;
using PulsePoint.Integrations;
using PulsePoint.Models;

namespace PulsePoint;

public class AppState
{
    public Database Db { get; }

    // ── Service health cache ──────────────────────────────────
    private List<ServiceStatus> _serviceCache = [];
    private DateTime _serviceCacheTime = DateTime.MinValue;
    private readonly TimeSpan _serviceTtl = TimeSpan.FromSeconds(60);
    private readonly Lock _serviceLock = new();

    // ── Sessions ──────────────────────────────────────────────
    private readonly Dictionary<string, DateTime> _sessions = new();
    private readonly Lock _sessionLock = new();
    public const string SessionCookie = "pp_session";
    private const string PasswordKey = "mgmt_password";

    // ── Integration snapshots ─────────────────────────────────
    private UnraidSnapshot? _unraidSnap;
    private IdracSnapshot?  _idracSnap;
    private readonly Lock _unraidLock = new();
    private readonly Lock _idracLock  = new();

    private OmadaSnapshot? _omadaSnap;
    private readonly Lock _omadaLock = new();

    private readonly UnraidService _unraidSvc = new();
    private readonly IdracService  _idracSvc  = new();
    private readonly OmadaService  _omadaSvc  = new();

    public AppState(Database db)
    {
        Db = db;
    }

    // ── Ping ──────────────────────────────────────────────────

    public async Task<double?> PingAsync(string ip)
    {
        try
        {
            using var ping = new Ping();
            var sw = Stopwatch.StartNew();
            var reply = await ping.SendPingAsync(ip, 2000);
            sw.Stop();
            return reply.Status == IPStatus.Success ? sw.Elapsed.TotalMilliseconds : null;
        }
        catch { return null; }
    }

    // ── Services ──────────────────────────────────────────────

    public void InvalidateServiceCache()
    {
        lock (_serviceLock) { _serviceCacheTime = DateTime.MinValue; }
    }

    public async Task<List<ServiceStatus>> GetServicesAsync(bool forceRefresh = false)
    {
        lock (_serviceLock)
        {
            if (!forceRefresh && DateTime.UtcNow - _serviceCacheTime < _serviceTtl)
                return _serviceCache;
        }

        var entries = Db.GetServices();
        if (entries.Count == 0)
        {
            lock (_serviceLock) { _serviceCache = []; _serviceCacheTime = DateTime.UtcNow; }
            return [];
        }

        var tasks = entries.Select(e => CheckServiceAsync(e.Name, NormalizeUrl(e.Url))).ToList();
        var results = await Task.WhenAll(tasks);

        lock (_serviceLock)
        {
            _serviceCache = [.. results];
            _serviceCacheTime = DateTime.UtcNow;
        }
        return [.. results];
    }

    private static string NormalizeUrl(string address)
    {
        if (address.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            address.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return address;
        return "http://" + address;
    }

    // Shared handler: bypasses SSL cert errors (common on LAN services) and
    // doesn't follow redirects so we report the actual status code returned.
    private static readonly HttpClient _checkClient = new(
        new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = false
        })
    { Timeout = TimeSpan.FromSeconds(5) };

    private async Task<ServiceStatus> CheckServiceAsync(string name, string url)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var resp = await _checkClient.GetAsync(url);
            sw.Stop();
            // Any HTTP response means the service is reachable — only a 5xx or
            // connection failure counts as offline.
            var code = (int)resp.StatusCode;
            return new ServiceStatus
            {
                Name = name,
                Url = url,
                Online = code < 500,
                StatusCode = code,
                ResponseMs = sw.Elapsed.TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            return new ServiceStatus
            {
                Name = name,
                Url = url,
                Online = false,
                Error = ex.Message,
                OfflineSince = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
        }
    }

    // ── Unraid ────────────────────────────────────────────────

    public UnraidSnapshot? GetUnraidSnapshot()
    {
        lock (_unraidLock) return _unraidSnap;
    }

    public async Task<UnraidSnapshot> RefreshUnraidAsync()
    {
        var host    = Db.GetSetting("unraid_host")        ?? "";
        var apiKey  = Db.GetSetting("unraid_api_key")     ?? "";
        var keyId   = Db.GetSetting("unraid_api_key_id");
        var bearer  = Db.GetSetting("unraid_bearer_token");

        UnraidSnapshot snap;
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(apiKey))
        {
            snap = new UnraidSnapshot { Connected = false, Error = "Not configured" };
        }
        else
        {
            snap = await _unraidSvc.FetchAsync(host, apiKey, keyId, bearer);
        }

        lock (_unraidLock) _unraidSnap = snap;
        return snap;
    }

    public Task<bool> UnraidDockerActionAsync(string containerId, string action)
    {
        var host   = Db.GetSetting("unraid_host")    ?? "";
        var apiKey = Db.GetSetting("unraid_api_key") ?? "";
        if (string.IsNullOrEmpty(host)) return Task.FromResult(false);
        return _unraidSvc.DockerActionAsync(host, apiKey, containerId, action);
    }

    public Task<bool> UnraidVmActionAsync(string vmName, string action)
    {
        var host   = Db.GetSetting("unraid_host")    ?? "";
        var apiKey = Db.GetSetting("unraid_api_key") ?? "";
        if (string.IsNullOrEmpty(host)) return Task.FromResult(false);
        return _unraidSvc.VmActionAsync(host, apiKey, vmName, action);
    }

    // ── iDRAC ─────────────────────────────────────────────────

    public IdracSnapshot? GetIdracSnapshot()
    {
        lock (_idracLock) return _idracSnap;
    }

    public async Task<IdracSnapshot> RefreshIdracAsync()
    {
        var host     = Db.GetSetting("idrac_host")     ?? "";
        var username = Db.GetSetting("idrac_username") ?? "";
        var password = Db.GetSetting("idrac_password") ?? "";

        IdracSnapshot snap;
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(username))
        {
            snap = new IdracSnapshot { Connected = false, Error = "Not configured" };
        }
        else
        {
            snap = await _idracSvc.FetchAsync(host, username, password);
        }

        lock (_idracLock) _idracSnap = snap;
        return snap;
    }

    // ── Omada ─────────────────────────────────────────────────

    public OmadaSnapshot? GetOmadaSnapshot()
    {
        lock (_omadaLock) return _omadaSnap;
    }

    public async Task<OmadaSnapshot> RefreshOmadaAsync(string? siteId = null)
    {
        var baseUrl      = Db.GetSetting("omada_base_url")      ?? "";
        var omadacId     = Db.GetSetting("omada_omadac_id")     ?? "";
        var clientId     = Db.GetSetting("omada_client_id")     ?? "";
        var clientSecret = Db.GetSetting("omada_client_secret") ?? "";
        var preferSite   = siteId ?? Db.GetSetting("omada_site_id");

        OmadaSnapshot snap;
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(omadacId) ||
            string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            snap = new OmadaSnapshot { Connected = false, Error = "Not configured" };
        }
        else
        {
            snap = await _omadaSvc.FetchAsync(baseUrl, omadacId, clientId, clientSecret, preferSite);
        }

        lock (_omadaLock) _omadaSnap = snap;
        return snap;
    }

    public async Task<OmadaSnapshot> RefreshOmadaSiteAsync(string siteId)
    {
        var baseUrl      = Db.GetSetting("omada_base_url")      ?? "";
        var omadacId     = Db.GetSetting("omada_omadac_id")     ?? "";
        var clientId     = Db.GetSetting("omada_client_id")     ?? "";
        var clientSecret = Db.GetSetting("omada_client_secret") ?? "";

        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(omadacId))
            return new OmadaSnapshot { Connected = false, Error = "Not configured" };

        // Site-specific refresh: update main cache with the result
        var snap = await _omadaSvc.FetchSiteAsync(baseUrl, omadacId, clientId, clientSecret, siteId);
        if (snap.Connected)
            lock (_omadaLock) _omadaSnap = snap;
        return snap;
    }

    // ── Password management ───────────────────────────────────

    public bool HasPassword() => Db.GetSetting(PasswordKey) != null;

    public void SetPassword(string plaintext)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPassword(plaintext, salt);
        Db.SetSetting(PasswordKey, Convert.ToHexString(salt) + ":" + hash);
    }

    public bool VerifyPassword(string plaintext)
    {
        var stored = Db.GetSetting(PasswordKey);
        if (stored == null) return false;
        var parts = stored.Split(':', 2);
        if (parts.Length != 2) return false;
        var salt = Convert.FromHexString(parts[0]);
        var expected = parts[1];
        return HashPassword(plaintext, salt) == expected;
    }

    private static string HashPassword(string password, byte[] salt)
    {
        var input = salt.Concat(Encoding.UTF8.GetBytes(password)).ToArray();
        return Convert.ToHexString(SHA256.HashData(input));
    }

    // ── Session management ────────────────────────────────────

    public string CreateSession()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        lock (_sessionLock)
            _sessions[token] = DateTime.UtcNow.AddHours(8);
        return token;
    }

    public bool ValidateSession(HttpContext ctx)
    {
        var token = ctx.Request.Cookies[SessionCookie];
        if (string.IsNullOrEmpty(token)) return false;
        lock (_sessionLock)
        {
            if (!_sessions.TryGetValue(token, out var expiry)) return false;
            if (expiry < DateTime.UtcNow) { _sessions.Remove(token); return false; }
            return true;
        }
    }

    public void RevokeSession(HttpContext ctx)
    {
        var token = ctx.Request.Cookies[SessionCookie];
        if (string.IsNullOrEmpty(token)) return;
        lock (_sessionLock) _sessions.Remove(token);
    }

    // ── Appearance ────────────────────────────────────────────

    public AppearanceSettings GetAppearance() => new(
        AccentColor:          Db.GetSetting("ui_accent_color")         ?? "#7c3aed",
        SiteName:             Db.GetSetting("ui_site_name")            ?? "PulsePoint",
        NavHidden:            Db.GetSetting("ui_nav_hidden")           ?? "",
        CardColumns:          Db.GetSetting("ui_card_columns")         ?? "auto",
        HiddenMetrics:        Db.GetSetting("ui_hidden_metrics")       ?? "",
        RefreshInterval:      int.TryParse(Db.GetSetting("ui_refresh_interval"), out var ri) ? ri : 15,
        OnlineThreshold:      int.TryParse(Db.GetSetting("ui_online_threshold"), out var ot) ? ot : 120,
        HideServicesWidget:   Db.GetSetting("ui_hide_services_widget") == "true"
    );
}

public record AppearanceSettings(
    string AccentColor,
    string SiteName,
    string NavHidden,
    string CardColumns,
    string HiddenMetrics,
    int    RefreshInterval,
    int    OnlineThreshold,
    bool   HideServicesWidget)
{
    public IReadOnlySet<string> NavHiddenSet =>
        NavHidden.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(s => s.ToLowerInvariant()).ToHashSet();

    public IReadOnlySet<string> HiddenMetricsSet =>
        HiddenMetrics.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(s => s.ToLowerInvariant()).ToHashSet();

    public string AccentHi
    {
        get
        {
            try
            {
                var hex = AccentColor.TrimStart('#');
                if (hex.Length == 6)
                {
                    var r = Convert.ToInt32(hex[0..2], 16);
                    var g = Convert.ToInt32(hex[2..4], 16);
                    var b = Convert.ToInt32(hex[4..6], 16);
                    return $"#{r + (int)((255 - r) * 0.4):X2}{g + (int)((255 - g) * 0.4):X2}{b + (int)((255 - b) * 0.4):X2}";
                }
            }
            catch { }
            return "#a78bfa";
        }
    }

    public string AccentGlow
    {
        get
        {
            try
            {
                var hex = AccentColor.TrimStart('#');
                if (hex.Length == 6)
                {
                    var r = Convert.ToInt32(hex[0..2], 16);
                    var g = Convert.ToInt32(hex[2..4], 16);
                    var b = Convert.ToInt32(hex[4..6], 16);
                    return $"rgba({r},{g},{b},.18)";
                }
            }
            catch { }
            return "rgba(124,58,237,.18)";
        }
    }

    public string CardColsCss =>
        int.TryParse(CardColumns, out var n)
            ? $"repeat({n}, 1fr)"
            : "repeat(auto-fill, minmax(240px, 1fr))";
}
