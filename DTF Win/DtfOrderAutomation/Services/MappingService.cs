using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DtfOrderAutomation.Services;

public class MappingService
{
    public static readonly string MappingPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DtfOrderAutomation", "dtf_mapping.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public Dictionary<string, string> Load()
    {
        if (!File.Exists(MappingPath)) return new();
        try
        {
            var json = File.ReadAllText(MappingPath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, Options) ?? new();
        }
        catch { return new(); }
    }

    public void Save(Dictionary<string, string> mapping)
    {
        File.WriteAllText(MappingPath, JsonSerializer.Serialize(mapping, Options));
    }
}
