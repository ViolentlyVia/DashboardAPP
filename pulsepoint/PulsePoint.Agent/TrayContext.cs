using System.Runtime.InteropServices;
using PulsePoint.Agent.Forms;

namespace PulsePoint.Agent;

public class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly MetricsCollector _collector = new();
    private readonly AgentReporter _reporter = new();
    private AgentConfig _config;
    private CancellationTokenSource _cts = new();
    private Task _reportLoop = Task.CompletedTask;
    private int _consecutiveFailures;

    public TrayContext()
    {
        _config = ConfigStore.Load();

        var menu = new ContextMenuStrip();
        menu.Items.Add("PulsePoint Agent", null).Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Report Now", null, (_, _) => _ = ReportNow());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());

        _tray = new NotifyIcon
        {
            Text = "PulsePoint Agent",
            Icon = BuildIcon(Color.FromArgb(124, 58, 237)),
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => OpenSettings();

        if (!ConfigStore.Exists())
        {
            OpenSettings();
        }
        else
        {
            StartLoop();
        }
    }

    private void StartLoop()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _reportLoop = Task.Run(async () =>
        {
            // Prime CPU counter
            await Task.Delay(1000, token);
            while (!token.IsCancellationRequested)
            {
                await ReportNow();
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _config.IntervalSeconds)), token);
            }
        }, token);
    }

    private async Task ReportNow()
    {
        var metrics = _collector.Collect(_config.PreferredIp);
        var ok = await _reporter.ReportAsync(_config.ServerUrl, metrics);

        if (ok)
        {
            _consecutiveFailures = 0;
            UpdateIcon(Color.FromArgb(52, 211, 153));   // green = OK
            _tray.Text = $"PulsePoint Agent — {metrics.Ip} — OK";
        }
        else
        {
            _consecutiveFailures++;
            UpdateIcon(_consecutiveFailures >= 3
                ? Color.FromArgb(248, 113, 113)          // red = repeated failure
                : Color.FromArgb(251, 191, 36));         // yellow = transient
            _tray.Text = $"PulsePoint Agent — {metrics.Ip} — No contact";
        }
    }

    private void OpenSettings()
    {
        var form = new SettingsForm(_config);
        if (form.ShowDialog() != DialogResult.OK) return;
        _config = form.Result;
        ConfigStore.Save(_config);
        StartLoop();
    }

    private void Exit()
    {
        _cts.Cancel();
        _tray.Visible = false;
        _tray.Dispose();
        _collector.Dispose();
        _reporter.Dispose();
        Application.Exit();
    }

    private void UpdateIcon(Color color)
    {
        var old = _tray.Icon;
        _tray.Icon = BuildIcon(color);
        old?.Dispose();
    }

    // Icon.FromHandle does not take ownership of the HICON — we must clone and destroy manually.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static Icon BuildIcon(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        using var brush = new SolidBrush(color);
        g.FillRectangle(Brushes.Transparent, 0, 0, 16, 16);
        var pts = new PointF[] {
            new(0, 8), new(3, 8), new(5, 3), new(7, 13), new(9, 5), new(11, 8), new(16, 8)
        };
        using var pen = new Pen(color, 1.5f);
        g.DrawLines(pen, pts);
        var hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        DestroyIcon(hIcon);
        return icon;
    }
}
