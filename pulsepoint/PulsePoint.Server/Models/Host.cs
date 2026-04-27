namespace PulsePoint.Models;

public class Host
{
    public string Hostname { get; set; } = "";
    public string Ip { get; set; } = "";
    public double Uptime { get; set; }
    public double LastSeen { get; set; }
    public double? Ping { get; set; }
    public double Cpu { get; set; }
    public double Memory { get; set; }
    public double? Disk { get; set; }
    public string? FriendlyName { get; set; }
    public int SortOrder { get; set; }
    public string? RdpUrl { get; set; }
    public string? Tags { get; set; }
}

public class CheckInPayload
{
    public string Hostname { get; set; } = "";
    public string Ip { get; set; } = "";
    public double Uptime { get; set; }
    public double Cpu { get; set; }
    public double Memory { get; set; }
    public double? Disk { get; set; }
}

public class ServiceStatus
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public bool Online { get; set; }
    public int? StatusCode { get; set; }
    public double? ResponseMs { get; set; }
    public double? OfflineSince { get; set; }
    public string? Error { get; set; }
}

public class AssetUpdatePayload
{
    public string? FriendlyName { get; set; }
    public string? Ip { get; set; }
    public string? RdpUrl { get; set; }
    public string? Tags { get; set; }
}

public class ServiceEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}

public class AddServicePayload
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
}
