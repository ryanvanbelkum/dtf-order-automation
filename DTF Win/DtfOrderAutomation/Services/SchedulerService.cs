using System;
using System.Threading;
using System.Threading.Tasks;

namespace DtfOrderAutomation.Services;

/// <summary>
/// Fires <see cref="RunRequested"/> on the configured interval.
/// Tracks <see cref="NextRunTime"/> so the dashboard can display a countdown.
/// </summary>
public class SchedulerService
{
    public DateTime? NextRunTime { get; private set; }
    public bool IsEnabled { get; private set; }

    public event EventHandler? RunRequested;

    private CancellationTokenSource? _cts;
    private int _intervalHours;

    public void SetSchedule(bool enabled, int intervalHours)
    {
        _cts?.Cancel();
        _cts = null;
        IsEnabled     = enabled;
        _intervalHours = intervalHours;

        if (!enabled)
        {
            NextRunTime = null;
            return;
        }

        NextRunTime = DateTime.Now.AddHours(intervalHours);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(intervalHours), token);
                while (!token.IsCancellationRequested)
                {
                    RunRequested?.Invoke(this, EventArgs.Empty);
                    NextRunTime = DateTime.Now.AddHours(intervalHours);
                    await Task.Delay(TimeSpan.FromHours(intervalHours), token);
                }
            }
            catch (OperationCanceledException) { }
        });
    }

    /// <summary>Reset the countdown after a manual run completes.</summary>
    public void ResetAfterRun() => SetSchedule(IsEnabled, _intervalHours);

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        IsEnabled   = false;
        NextRunTime = null;
    }
}
