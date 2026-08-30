using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DtfOrderAutomation.Models;

public class ExportBundle
{
    [JsonPropertyName("exported_at")]
    public string ExportedAt { get; set; } = "";

    [JsonPropertyName("app_version")]
    public string AppVersion { get; set; } = "";

    [JsonPropertyName("config")]
    public AppConfig Config { get; set; } = new();

    [JsonPropertyName("mapping")]
    public Dictionary<string, string> Mapping { get; set; } = new();
}
