using System;
using System.IO;
using System.Text.Json;
using DtfOrderAutomation.Models;

namespace DtfOrderAutomation.Services;

public class ConfigService
{
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DtfOrderAutomation");

    public static readonly string ConfigPath = Path.Combine(DataDir, "dtf_config.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public ConfigService()
    {
        Directory.CreateDirectory(DataDir);
    }

    public AppConfig Load()
    {
        if (!File.Exists(ConfigPath)) return new AppConfig();
        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
        }
        catch { return new AppConfig(); }
    }

    public void Save(AppConfig config)
    {
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, Options));
    }
}
