using System.Text;
using System.Text.Json;

namespace SysDash.EndpointAgent;

public sealed class AgentReporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
    };

    private readonly HttpClient _httpClient;
    private readonly SystemMetricsProvider _metricsProvider;

    public AgentReporter(SystemMetricsProvider metricsProvider)
    {
        _metricsProvider = metricsProvider;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8),
        };
    }

    public async Task<bool> SendReportAsync(AgentConfig config, CancellationToken token)
    {
        var (hostname, ip, uptime, cpuPercent, memoryPercent) = _metricsProvider.GetSnapshot(config);
        var payload = new Dictionary<string, object?>
        {
            ["hostname"] = hostname,
            ["ip"] = ip,
            ["uptime"] = uptime,
            ["cpu_percent"] = cpuPercent,
            ["memory_percent"] = memoryPercent,
        };

        var baseUri = new Uri(config.ServerUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var target = new Uri(baseUri, "api/report");
        var body = JsonSerializer.Serialize(payload, JsonOptions);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(target, content, token);
        return response.IsSuccessStatusCode;
    }
}
