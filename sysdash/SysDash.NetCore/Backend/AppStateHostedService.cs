namespace SysDash.NetCore.Backend;

public sealed class AppStateHostedService : IHostedService
{
    private readonly IAppState _state;
    private readonly IHostApplicationLifetime _lifetime;

    public AppStateHostedService(IAppState state, IHostApplicationLifetime lifetime)
    {
        _state = state;
        _lifetime = lifetime;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _state.InitializeDatabase();
        _state.PrimeServiceCache();
        _state.PrimeIdracCache();
        _state.StartServiceMonitor(_lifetime.ApplicationStopping);
        _state.StartUnraidMonitor(_lifetime.ApplicationStopping);
        _state.StartIdracMonitor(_lifetime.ApplicationStopping);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
