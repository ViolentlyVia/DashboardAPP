namespace PulsePoint.Services;

public class ServiceMonitor : BackgroundService
{
    private readonly AppState _state;
    private readonly ILogger<ServiceMonitor> _log;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    public ServiceMonitor(AppState state, ILogger<ServiceMonitor> log)
    {
        _state = state;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Brief startup delay so the server is fully ready before the first poll.
        await Task.Delay(TimeSpan.FromSeconds(3), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var results = await _state.GetServicesAsync(forceRefresh: true);
                var online = results.Count(s => s.Online);
                _log.LogDebug("Service poll: {Online}/{Total} online", online, results.Count);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Service poll error");
            }

            await Task.Delay(Interval, ct);
        }
    }
}
