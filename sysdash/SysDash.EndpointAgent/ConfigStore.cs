using System.Text.Json;

namespace SysDash.EndpointAgent;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _configPath;

    public ConfigStore()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SysDashEndpointAgent");
        Directory.CreateDirectory(root);
        _configPath = Path.Combine(root, "config.json");
    }

    public AgentConfig? Load()
    {
        if (!File.Exists(_configPath))
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<AgentConfig>(text);
            if (config is null)
            {
                return null;
            }

            if (config.IntervalSeconds < 5)
            {
                config.IntervalSeconds = 5;
            }

            return config;
        }
        catch
        {
            return null;
        }
    }

    public void Save(AgentConfig config)
    {
        var text = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(_configPath, text);
    }
}
