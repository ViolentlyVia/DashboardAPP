namespace PulsePoint.Services;

public class IntegrationMonitor : BackgroundService
{
    private readonly AppState _state;
    private readonly ILogger<IntegrationMonitor> _log;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    public IntegrationMonitor(AppState state, ILogger<IntegrationMonitor> log)
    {
        _state = state;
        _log   = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Stagger startup so the server is ready and we don't slam APIs at boot.
        await Task.Delay(TimeSpan.FromSeconds(10), ct);

        while (!ct.IsCancellationRequested)
        {
            await PollUnraid(ct);
            await PollIdrac(ct);
            await PollOmada(ct);
            await Task.Delay(Interval, ct);
        }
    }

    private async Task PollUnraid(CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        try
        {
            var snap = await _state.RefreshUnraidAsync();
            if (snap.Connected)
                _log.LogDebug("Unraid: {Containers} containers, {Vms} VMs",
                    snap.Containers.Count, snap.Vms.Count);
            else
                _log.LogDebug("Unraid: {Error}", snap.Error);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Unraid poll error");
        }
    }

    private async Task PollIdrac(CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        try
        {
            var snap = await _state.RefreshIdracAsync();
            if (snap.Connected)
                _log.LogDebug("iDRAC: {Model} — {Health}",
                    snap.System.Model, snap.System.HealthStatus);
            else
                _log.LogDebug("iDRAC: {Error}", snap.Error);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "iDRAC poll error");
        }
    }

    private async Task PollOmada(CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        try
        {
            var snap = await _state.RefreshOmadaAsync();
            if (snap.Connected)
                _log.LogDebug("Omada: {Devices} devices, {Clients} clients on site '{Site}'",
                    snap.Devices.Count, snap.Clients.Count, snap.SelectedSite?.Name);
            else
                _log.LogDebug("Omada: {Error}", snap.Error);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Omada poll error");
        }
    }
}
