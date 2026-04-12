using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SysDash.EndpointAgent;

public sealed class SystemMetricsProvider : IDisposable
{
    private readonly PerformanceCounter _cpuCounter;

    public sealed record NetworkChoice(string InterfaceName, string IpAddress);

    public SystemMetricsProvider()
    {
        _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        _ = _cpuCounter.NextValue();
    }

    public (string hostname, string ip, double uptime, double? cpuPercent, double? memoryPercent) GetSnapshot(AgentConfig config)
    {
        var hostname = Environment.MachineName;
        var ip = ResolveReportedIp(config.PreferredIp) ?? "127.0.0.1";
        var uptime = Environment.TickCount64 / 1000.0;

        double? cpu = null;
        try
        {
            cpu = Math.Round((double)_cpuCounter.NextValue(), 2);
        }
        catch
        {
            cpu = null;
        }

        double? memory = null;
        try
        {
            memory = GetMemoryUsagePercent();
        }
        catch
        {
            memory = null;
        }

        return (hostname, ip, uptime, cpu, memory);
    }

    public static IReadOnlyList<NetworkChoice> GetAvailableIpv4Choices()
    {
        var result = new List<NetworkChoice>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var props = nic.GetIPProperties();
            foreach (var unicast in props.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    result.Add(new NetworkChoice(nic.Name, unicast.Address.ToString()));
                }
            }
        }

        return result;
    }

    public void Dispose()
    {
        _cpuCounter.Dispose();
    }

    private static string? GetPrimaryIpv4Address()
    {
        return GetAvailableIpv4Choices().Select(x => x.IpAddress).FirstOrDefault();
    }

    private static string? ResolveReportedIp(string? preferredIp)
    {
        var allIps = GetAvailableIpv4Choices().Select(x => x.IpAddress).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(preferredIp) && allIps.Contains(preferredIp))
        {
            return preferredIp;
        }

        return GetPrimaryIpv4Address();
    }

    private static double GetMemoryUsagePercent()
    {
        var status = new MEMORYSTATUSEX();
        status.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        if (!GlobalMemoryStatusEx(ref status))
        {
            throw new InvalidOperationException("Unable to read memory status.");
        }

        return Math.Round((double)status.dwMemoryLoad, 2);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
