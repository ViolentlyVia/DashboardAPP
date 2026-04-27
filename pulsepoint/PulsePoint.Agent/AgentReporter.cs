using System.Net.Http.Json;
using System.Text.Json;

namespace PulsePoint.Agent;

public class AgentReporter : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly JsonSerializerOptions _opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<bool> ReportAsync(string serverUrl, Metrics metrics)
    {
        try
        {
            var url = serverUrl.TrimEnd('/') + "/api/report";
            var resp = await _http.PostAsJsonAsync(url, metrics, _opts);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public void Dispose() => _http.Dispose();
}
