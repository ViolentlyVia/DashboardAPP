using System.Text.Json;

namespace PulsePoint.Agent;

public class AgentConfig
{
    public string ServerUrl { get; set; } = "http://localhost:5000";
    public int IntervalSeconds { get; set; } = 30;
    public string? PreferredIp { get; set; }
}

public static class ConfigStore
{
    private static readonly string _dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PulsePointAgent");

    private static readonly string _path = Path.Combine(_dir, "config.json");

    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    public static AgentConfig Load()
    {
        try
        {
            if (!File.Exists(_path)) return new AgentConfig();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<AgentConfig>(json) ?? new AgentConfig();
        }
        catch { return new AgentConfig(); }
    }

    public static void Save(AgentConfig config)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(config, _opts));
    }

    public static bool Exists() => File.Exists(_path);
}
