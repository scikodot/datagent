using Microsoft.Extensions.Configuration;

namespace DatagentShared;

public class ConfigurationReader
{
    private readonly IConfiguration _config;

    public static string ConfigsPath => Path.Combine(Directory.GetCurrentDirectory(), "configs");

    public ConfigurationReader(string assemblyName)
    {
        _config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(ConfigsPath, $"appconfig.{assemblyName}.json"))
            .Build();
    }

    public IConfigurationSection GetSection(params string[] keys)
    {
        if (keys.Length == 0)
            throw new ArgumentException("No keys provided.");

        var section = _config.GetSection(keys[0]);
        foreach (var key in keys[1..])
            section = section.GetSection(key);

        return section;
    }

    public string? GetValue(params string[] keys)
    {
        if (keys.Length == 0)
            throw new ArgumentException("No keys provided.");

        return GetSection(keys[..^1]).GetSection(keys[^1]).Value;
    }
}
