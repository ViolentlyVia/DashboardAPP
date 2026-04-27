using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace PulsePoint.Agent;

public class Metrics
{
    public string Hostname { get; set; } = "";
    public string Ip { get; set; } = "";
    public double Uptime { get; set; }
    public double Cpu { get; set; }
    public double Memory { get; set; }
    public double? Disk { get; set; }
}

public class MetricsCollector : IDisposable
{
    private readonly PerformanceCounter _cpuCounter =
        new("Processor", "% Processor Time", "_Total");

    private bool _disposed;

    public MetricsCollector()
    {
        // Prime the counter — first read is always 0
        _cpuCounter.NextValue();
    }

    public Metrics Collect(string? preferredIp)
    {
        var cpu = Math.Round(_cpuCounter.NextValue(), 1);
        var mem = GetMemoryPercent();
        var disk = GetSystemDrivePercent();
        var ip = preferredIp ?? GetLocalIp();

        return new Metrics
        {
            Hostname = Environment.MachineName,
            Ip = ip,
            Uptime = Environment.TickCount64 / 1000.0,
            Cpu = cpu,
            Memory = mem,
            Disk = disk
        };
    }

    private static double GetMemoryPercent()
    {
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref mem)) return 0;
        var used = mem.ullTotalPhys - mem.ullAvailPhys;
        return mem.ullTotalPhys > 0
            ? Math.Round(used * 100.0 / mem.ullTotalPhys, 1) : 0;
    }

    private static double? GetSystemDrivePercent()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:");
            if (drive.DriveType != DriveType.Fixed) return null;
            var used = drive.TotalSize - drive.AvailableFreeSpace;
            return Math.Round(used * 100.0 / drive.TotalSize, 1);
        }
        catch { return null; }
    }

    public static string GetLocalIp()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    return addr.Address.ToString();
            }
        }
        return "127.0.0.1";
    }

    public static List<(string Name, string Ip)> GetAllNics()
    {
        var result = new List<(string, string)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    result.Add((nic.Name, addr.Address.ToString()));
            }
        }
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cpuCounter.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
