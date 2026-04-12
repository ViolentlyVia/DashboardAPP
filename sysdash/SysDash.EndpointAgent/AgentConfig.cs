namespace SysDash.EndpointAgent;

public sealed class AgentConfig
{
    public string ServerUrl { get; set; } = "http://127.0.0.1:5000";
    public int IntervalSeconds { get; set; } = 30;
    public string? PreferredIp { get; set; }
}
