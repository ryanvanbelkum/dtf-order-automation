using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DtfOrderAutomation.Models;

namespace DtfOrderAutomation.Services;

public class LogService
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DtfOrderAutomation", "dtf_log.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private const int MaxEntries = 500;

    public List<RunResult> Load()
    {
        if (!File.Exists(LogPath)) return new();
        try
        {
            var json = File.ReadAllText(LogPath);
            return JsonSerializer.Deserialize<List<RunResult>>(json, Options) ?? new();
        }
        catch { return new(); }
    }

    public void Save(List<RunResult> log)
    {
        var trimmed = log.Count > MaxEntries ? log.GetRange(log.Count - MaxEntries, MaxEntries) : log;
        File.WriteAllText(LogPath, JsonSerializer.Serialize(trimmed, Options));
    }

    public void Append(List<RunResult> log, RunResult result)
    {
        log.Add(result);
        Save(log);
    }
}
