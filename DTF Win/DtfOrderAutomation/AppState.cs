using System;
using System.Collections.Generic;
using System.Threading;
using DtfOrderAutomation.Models;

namespace DtfOrderAutomation;

/// <summary>
/// Shared runtime state and event bus used by all pages.
/// Lives on App as a static singleton.
/// </summary>
public class AppState
{
    // ── Run state ──────────────────────────────────────────────────────────

    public bool IsRunning { get; private set; }
    public CancellationTokenSource? CurrentCts { get; private set; }

    /// <summary>Fired on any thread when a log line is emitted during a run.</summary>
    public event Action<string>? LogLine;

    /// <summary>Fired on any thread when a run starts or finishes.</summary>
    public event Action<bool>? RunStateChanged;

    /// <summary>Fired on any thread when a run completes with a result.</summary>
    public event Action<RunResult>? RunCompleted;

    public void BeginRun(CancellationTokenSource cts)
    {
        IsRunning  = true;
        CurrentCts = cts;
        RunStateChanged?.Invoke(true);
    }

    public void EndRun(RunResult result)
    {
        IsRunning  = false;
        CurrentCts = null;
        RunStateChanged?.Invoke(false);
        RunCompleted?.Invoke(result);
    }

    public void EmitLog(string line) => LogLine?.Invoke(line);

    public void RequestStop() => CurrentCts?.Cancel();

    // ── In-memory log (mirrors LogService, kept for fast UI reads) ─────────

    public List<RunResult> Log { get; } = new();
}
