using System.Net;
using System.Net.Sockets;

namespace SysDash.EndpointAgent;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        ApplyCommandLineConfig(args);
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAgentContext());
    }

    private static void ApplyCommandLineConfig(string[] args)
    {
        if (args.Length == 0)
        {
            return;
        }

        string? serverUrl = null;
        int? intervalSeconds = null;
        string? preferredIp = null;

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (token.Equals("--server-url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                serverUrl = args[++i].Trim();
                continue;
            }

            if (token.Equals("--interval", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var parsedInterval))
                {
                    intervalSeconds = parsedInterval;
                }

                continue;
            }

            if (token.Equals("--report-ip", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                preferredIp = args[++i].Trim();
            }
        }

        if (serverUrl is null && intervalSeconds is null && preferredIp is null)
        {
            return;
        }

        var store = new ConfigStore();
        var existing = store.Load() ?? new AgentConfig();

        if (serverUrl is not null)
        {
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                MessageBox.Show(
                    "Invalid --server-url value. Expected a full http/https URL including port.",
                    "SysDash Endpoint Agent",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            else
            {
                existing.ServerUrl = serverUrl;
            }
        }

        if (intervalSeconds.HasValue)
        {
            existing.IntervalSeconds = Math.Max(5, intervalSeconds.Value);
        }

        if (preferredIp is not null)
        {
            if (IPAddress.TryParse(preferredIp, out var parsedIp) && parsedIp.AddressFamily == AddressFamily.InterNetwork)
            {
                existing.PreferredIp = preferredIp;
            }
            else if (preferredIp.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                existing.PreferredIp = null;
            }
            else
            {
                MessageBox.Show(
                    "Invalid --report-ip value. Use an IPv4 address or 'auto'.",
                    "SysDash Endpoint Agent",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        store.Save(existing);
    }
}