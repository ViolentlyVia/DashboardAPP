namespace SysDash.NetCore.Backend;

public sealed partial class AppState : IAppState
{
    private readonly object _serviceLock = new();
    private readonly object _unraidLock = new();
    private readonly object _idracLock = new();
    private readonly object _omadaLock = new();
    private readonly HttpClient _unraidClient;
    private readonly ILogger _logger;
    private readonly Dictionary<string, int> _serviceMissed = new();
    private readonly Dictionary<string, long?> _serviceOfflineSince = new();

    private List<Dictionary<string, object?>> _serviceCache = new();
    private long _serviceUpdatedAt;
    private bool _serviceMonitorStarted;

    private Dictionary<string, object?> _unraidSnapshot = new()
    {
        ["fetched_at"] = 0L,
        ["gql_available"] = false,
        ["array"] = new Dictionary<string, object?> { ["error"] = "Array data unavailable" },
        ["docker"] = new Dictionary<string, object?> { ["error"] = "Docker data unavailable" },
        ["shares"] = new List<object>(),
        ["sources"] = new List<object>(),
        ["error"] = "Unraid data unavailable",
    };
    private bool _unraidMonitorStarted;

    private Dictionary<string, object?> _idracSnapshot = new()
    {
      ["fetched_at"] = 0L,
      ["host"] = "",
      ["connected"] = false,
      ["error"] = "iDRAC data unavailable",
      ["system"] = null,
      ["manager"] = null,
      ["thermal"] = null,
      ["power"] = null,
      ["disks"] = new List<object>(),
    };
    private bool _idracMonitorStarted;

    private Dictionary<string, object?> _omadaSnapshot = new()
    {
      ["fetched_at"] = 0L,
      ["connected"] = false,
      ["error"] = "Omada data unavailable",
      ["controller"] = new Dictionary<string, object?>(),
      ["sites"] = new List<object>(),
      ["selected_site"] = null,
      ["device_count_total"] = null,
      ["client_count_total"] = null,
      ["sources"] = new List<object>(),
    };
    private bool _omadaMonitorStarted;
    private string _omadaAccessToken = string.Empty;
    private DateTimeOffset _omadaAccessTokenExpiryUtc = DateTimeOffset.MinValue;
    private string _omadaLastAuthError = string.Empty;

    private readonly Dictionary<string, string> _services = new()
    {
        ["iperf3"] = "192.168.0.111",
        ["Nginx"] = "192.168.0.2",
        ["omada controller"] = "192.168.0.122",
        ["Overseerr"] = "192.168.0.4",
        ["prowlarr"] = "192.168.0.200",
        ["Radarr"] = "192.168.0.3",
        ["Sonarr"] = "192.168.0.6",
    };

    public string ApiBuild { get; } = "2026-04-07-mobile-summary-v1";
    public string RequiredKey { get; }
    public string GuacamoleTemplate { get; }
    public string UnraidHost { get; }
    public string UnraidApiKeyId { get; }
    public string UnraidApiKey { get; }
    public string UnraidBearerToken { get; }
    public string UnraidSessionCookie { get; }
    public string IdracHost { get; }
    public string IdracUsername { get; }
    public string IdracPassword { get; }
    public string OmadaBaseUrl { get; }
    public string OmadaOmadacId { get; }
    public string OmadaClientId { get; }
    public string OmadaClientSecret { get; }
    public string OmadaSiteId { get; }
    public string DbPath { get; }
    public bool ServiceMonitorStarted => _serviceMonitorStarted;

    private const string UnraidGraphQuery = @"query DashboardData {
  array {
    state
    capacity { kilobytes { total free used } }
    disks { id name device type status temp size numReads numWrites numErrors }
    parities { id name device status temp size }
  }
  docker {
    containers { id names state status image autoStart }
  }
  shares { name free size allocator }
  vms {
    domains { uuid name state }
  }
  info {
    time
    os { platform distro release }
    cpu { brand speed cores threads packages { temp } }
  }
}";

    public AppState(string contentRootPath, IConfiguration config, HttpClient unraidClient, ILogger logger)
    {
        _unraidClient = unraidClient;
        _logger = logger;

        DbPath = Path.GetFullPath(Path.Combine(contentRootPath, "..", "hosts.db"));
        RequiredKey = Environment.GetEnvironmentVariable("SYSDASH_REQUIRED_KEY")?.Trim() ?? "herpderp";
        GuacamoleTemplate = Environment.GetEnvironmentVariable("GUACAMOLE_RDP_URL_TEMPLATE")?.Trim() ?? string.Empty;
        UnraidHost = Environment.GetEnvironmentVariable("UNRAID_HOST")?.Trim() ?? "192.168.0.101";
        UnraidApiKeyId = Environment.GetEnvironmentVariable("UNRAID_API_KEY_ID")?.Trim() ?? "9910be3a-feec-411a-b3f6-b94edf0bfc58";
        UnraidApiKey = Environment.GetEnvironmentVariable("UNRAID_API_KEY")?.Trim() ?? "b5ff054f4d93f1f5f7acc6939171987cefe4bfcbd828cea531169723bc53aba9";
        UnraidBearerToken = Environment.GetEnvironmentVariable("UNRAID_BEARER_TOKEN")?.Trim() ?? string.Empty;
        UnraidSessionCookie = Environment.GetEnvironmentVariable("UNRAID_SESSION_COOKIE")?.Trim() ?? string.Empty;
        IdracHost = Environment.GetEnvironmentVariable("IDRAC_HOST")?.Trim() ?? "192.168.0.120";
        IdracUsername = Environment.GetEnvironmentVariable("IDRAC_USERNAME")?.Trim() ?? "admin";
        IdracPassword = Environment.GetEnvironmentVariable("IDRAC_PASSWORD")?.Trim() ?? "Rjqwkr123!";
        OmadaBaseUrl = (Environment.GetEnvironmentVariable("OMADA_BASE_URL")?.Trim() ?? "https://192.168.0.122:18043").TrimEnd('/');
        OmadaOmadacId = Environment.GetEnvironmentVariable("OMADA_OMADAC_ID")?.Trim() ?? string.Empty;
        OmadaClientId = Environment.GetEnvironmentVariable("OMADA_CLIENT_ID")?.Trim() ?? string.Empty;
        OmadaClientSecret = Environment.GetEnvironmentVariable("OMADA_CLIENT_SECRET")?.Trim() ?? string.Empty;
        OmadaSiteId = Environment.GetEnvironmentVariable("OMADA_SITE_ID")?.Trim() ?? string.Empty;

        foreach (var name in _services.Keys)
        {
            _serviceMissed[name] = 0;
            _serviceOfflineSince[name] = null;
        }
    }
}
