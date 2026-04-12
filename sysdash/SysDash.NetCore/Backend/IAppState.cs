namespace SysDash.NetCore.Backend;

public interface IAppState
{
    string ApiBuild { get; }
    string RequiredKey { get; }
    string UnraidHost { get; }
    bool ServiceMonitorStarted { get; }

    void InitializeDatabase();
    void PrimeServiceCache();
    void StartServiceMonitor(CancellationToken token);
    void StartUnraidMonitor(CancellationToken token);
    void PrimeIdracCache();
    void StartIdracMonitor(CancellationToken token);

    void UpsertHostReport(string hostname, string ip, double uptime, double? cpu, double? memory);
    List<(string hostname, string ip, double? uptime, double? lastSeen, double? cpu, double? memory, string? friendlyName, string? rdpUrl)> GetHostsForStatus();
    void UpdatePing(string hostname, double? pingMs);
    long GetMostRecentCheckin();
    List<object> GetDockerStatus();

    Task<List<Dictionary<string, object?>>> CollectServiceStatusesAsync();
    (List<Dictionary<string, object?>> items, long updatedAt) GetServiceCacheSnapshot();
    void SetServiceCache(List<Dictionary<string, object?>> items);

    Dictionary<string, object?> GetUnraidSnapshot();
    void SetUnraidSnapshot(Dictionary<string, object?> snapshot);
    Task<(Dictionary<string, object?> normalized, int? statusCode, object? rawBody, string? error)> FetchUnraidSnapshotAsync();
    Dictionary<string, object?> GetIdracSnapshot();
    void SetIdracSnapshot(Dictionary<string, object?> snapshot);
    Task<Dictionary<string, object?>> FetchIdracSummaryAsync();

    List<object> GetAssets();
    void UpdateAsset(string hostname, string? friendlyName, string? newIp, string? rdpUrl);
    void DeleteAsset(string hostname);
    bool MoveAsset(string hostname, bool moveUp);

    object GetSummaryPayload();
    string? BuildRdpLaunchUrl(string hostname, string ip, string? configuredRdpUrl);
}
