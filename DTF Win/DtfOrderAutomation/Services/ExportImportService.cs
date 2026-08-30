using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DtfOrderAutomation.Models;

namespace DtfOrderAutomation.Services;

public class ExportImportService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public ExportBundle BuildBundle(AppConfig config, Dictionary<string, string> mapping)
    {
        return new ExportBundle
        {
            ExportedAt = DateTime.UtcNow.ToString("o"),
            AppVersion = DtfOrderAutomation.AppVersion.Current,
            Config     = config,
            Mapping    = mapping,
        };
    }

    public void Export(string path, ExportBundle bundle)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(bundle, Options));
    }

    public ExportBundle Import(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ExportBundle>(json, Options)
            ?? throw new InvalidDataException("This file doesn't look like a valid DTF Order Automation export.");
    }
}
