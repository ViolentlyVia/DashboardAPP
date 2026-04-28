namespace PulsePoint.Models;

// ── Unraid ────────────────────────────────────────────────────

public class UnraidSnapshot
{
    public bool Connected { get; set; }
    public string? Error { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
    public ArrayInfo Array { get; set; } = new();
    public List<DiskInfo> Disks { get; set; } = [];
    public List<DiskInfo> Parities { get; set; } = [];
    public List<DockerContainer> Containers { get; set; } = [];
    public List<VmDomain> Vms { get; set; } = [];
    public List<ShareInfo> Shares { get; set; } = [];
}

public class ArrayInfo
{
    public string State { get; set; } = "";
    public long UsedBytes { get; set; }
    public long FreeBytes { get; set; }
    public long TotalBytes { get; set; }
}

public class DiskInfo
{
    public string Name { get; set; } = "";
    public string Device { get; set; } = "";
    public string Status { get; set; } = "";
    public int Temp { get; set; }
    public long Size { get; set; }
    public string Type { get; set; } = "";
}

public class DockerContainer
{
    public string Id { get; set; } = "";
    public string Names { get; set; } = "";
    public string Image { get; set; } = "";
    public string State { get; set; } = "";
    public string Status { get; set; } = "";
    // Unraid returns state as "RUNNING" or "STOPPED" (uppercase)
    public bool Running => State.Equals("running", StringComparison.OrdinalIgnoreCase);
}

public class VmDomain
{
    public string Name { get; set; } = "";
    public string State { get; set; } = "";
    public bool Running => State == "started";
}

public class ShareInfo
{
    public string Name { get; set; } = "";
    // Unraid returns free and size in kilobytes
    public long FreeKb { get; set; }
    public long SizeKb { get; set; }
    public long UsedKb => SizeKb - FreeKb;
}

// ── Omada ─────────────────────────────────────────────────────

public class OmadaSnapshot
{
    public bool Connected { get; set; }
    public string? Error { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
    public List<OmadaSite> Sites { get; set; } = [];
    public OmadaSite? SelectedSite { get; set; }
    public List<OmadaDevice> Devices { get; set; } = [];
    public List<OmadaClient> Clients { get; set; } = [];
}

public class OmadaSite
{
    public string SiteId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Scenario { get; set; } = "";
}

public class OmadaDevice
{
    public string Mac { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Model { get; set; } = "";
    public string FirmwareVersion { get; set; } = "";
    public int Status { get; set; }
    public bool Online => Status > 0;
    public long Uptime { get; set; }
    public int ClientCount { get; set; }
    public long Download { get; set; }
    public long Upload { get; set; }
}

public class OmadaClient
{
    public string Mac { get; set; } = "";
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public string NetworkName { get; set; } = "";
    public string Ssid { get; set; } = "";
    public bool Wireless { get; set; }
    public int SignalLevel { get; set; }
    public long RxRate { get; set; }
    public long TxRate { get; set; }
    public int WiredLinkSpeed { get; set; }
    public long Uptime { get; set; }
    public bool Active { get; set; }
    // Cumulative bytes for the session / 24h window (field varies by firmware)
    public long TrafficDown { get; set; }
    public long TrafficUp { get; set; }
    public long TrafficTotal => TrafficDown + TrafficUp;
}

// ── iDRAC 8 / Redfish ─────────────────────────────────────────

public class IdracSnapshot
{
    public bool Connected { get; set; }
    public string? Error { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
    public SystemInfo System { get; set; } = new();
    public List<ThermalSensor> Temperatures { get; set; } = [];
    public List<FanInfo> Fans { get; set; } = [];
    public List<PowerSupplyInfo> PowerSupplies { get; set; } = [];
    public List<StorageDrive> Drives { get; set; } = [];
}

public class SystemInfo
{
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string ServiceTag { get; set; } = "";
    public string BiosVersion { get; set; } = "";
    public string PowerState { get; set; } = "";
    public string HealthStatus { get; set; } = "";
    public int ProcessorCount { get; set; }
    public long TotalMemoryGiB { get; set; }
    public string IdracFirmware { get; set; } = "";
}

public class ThermalSensor
{
    public string Name { get; set; } = "";
    public double ReadingCelsius { get; set; }
    public double? UpperThresholdCritical { get; set; }
    public string Status { get; set; } = "";
}

public class FanInfo
{
    public string Name { get; set; } = "";
    public int Rpm { get; set; }
    public string Status { get; set; } = "";
}

public class PowerSupplyInfo
{
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public double? LastOutputWatts { get; set; }
    public double? PowerCapacityWatts { get; set; }
    public string Status { get; set; } = "";
}

public class StorageDrive
{
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string Protocol { get; set; } = "";
    public long CapacityBytes { get; set; }
    public string Health { get; set; } = "";
    public string State { get; set; } = "";
}
