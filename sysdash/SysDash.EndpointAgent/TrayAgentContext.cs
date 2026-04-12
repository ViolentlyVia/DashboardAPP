namespace SysDash.EndpointAgent;

public sealed class TrayAgentContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ConfigStore _configStore;
    private readonly SystemMetricsProvider _metricsProvider;
    private readonly AgentReporter _reporter;

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private AgentConfig _config;

    public TrayAgentContext()
    {
        _configStore = new ConfigStore();
        _metricsProvider = new SystemMetricsProvider();
        _reporter = new AgentReporter(_metricsProvider);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "SysDash Endpoint Agent",
            Visible = true,
            ContextMenuStrip = BuildContextMenu(),
        };

        var loadedConfig = _configStore.Load();
        _config = loadedConfig ?? new AgentConfig();

        if (loadedConfig is null)
        {
            if (!ShowSettingsDialog(_config, true))
            {
                ExitThread();
                return;
            }
        }

        StartLoop();
        _notifyIcon.ShowBalloonTip(1500, "SysDash Endpoint Agent", "Agent started in system tray.", ToolTipIcon.Info);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopLoop();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _metricsProvider.Dispose();
        }

        base.Dispose(disposing);
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var settingsItem = new ToolStripMenuItem("Settings", null, (_, _) =>
        {
            _ = ShowSettingsDialog(_config, false);
        });

        var pickNicItem = new ToolStripMenuItem("Pick NIC/IP", null, (_, _) =>
        {
            _ = ShowNicPicker();
        });

        var sendNowItem = new ToolStripMenuItem("Send Report Now", null, async (_, _) =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var ok = await _reporter.SendReportAsync(_config, cts.Token);
                _notifyIcon.ShowBalloonTip(1200, "SysDash Endpoint Agent", ok ? "Report sent." : "Report failed.", ok ? ToolTipIcon.Info : ToolTipIcon.Warning);
            }
            catch
            {
                _notifyIcon.ShowBalloonTip(1200, "SysDash Endpoint Agent", "Report failed.", ToolTipIcon.Warning);
            }
        });

        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitThread());

        menu.Items.Add(settingsItem);
        menu.Items.Add(pickNicItem);
        menu.Items.Add(sendNowItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        return menu;
    }

    private bool ShowSettingsDialog(AgentConfig current, bool firstRun)
    {
        using var form = new SettingsForm(current);
        var result = form.ShowDialog();

        if (result != DialogResult.OK || form.SavedConfig is null)
        {
            return !firstRun;
        }

        _config = form.SavedConfig;
        _configStore.Save(_config);
        RestartLoop();
        return true;
    }

    private bool ShowNicPicker()
    {
        using var form = new NicPickerForm(_config.PreferredIp);
        var result = form.ShowDialog();
        if (result != DialogResult.OK)
        {
            return false;
        }

        _config.PreferredIp = form.SelectedPreferredIp;
        _configStore.Save(_config);
        RestartLoop();

        var text = _config.PreferredIp is null ? "Using auto NIC selection." : $"Reporting IP set to {_config.PreferredIp}.";
        _notifyIcon.ShowBalloonTip(1200, "SysDash Endpoint Agent", text, ToolTipIcon.Info);
        return true;
    }

    private void RestartLoop()
    {
        StopLoop();
        StartLoop();
    }

    private void StartLoop()
    {
        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Run(async () =>
        {
            while (!_loopCts.IsCancellationRequested)
            {
                try
                {
                    await _reporter.SendReportAsync(_config, _loopCts.Token);
                }
                catch
                {
                    // Suppress transient network errors; next cycle retries.
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _config.IntervalSeconds)), _loopCts.Token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }, _loopCts.Token);
    }

    private void StopLoop()
    {
        if (_loopCts is null)
        {
            return;
        }

        _loopCts.Cancel();
        _loopCts.Dispose();
        _loopCts = null;
        _loopTask = null;
    }
}
