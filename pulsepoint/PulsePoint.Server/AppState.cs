using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using PulsePoint.Data;
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
}
