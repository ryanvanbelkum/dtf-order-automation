using System;
using DtfOrderAutomation.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DtfOrderAutomation.Pages;

public sealed partial class DashboardPage : Page
{
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _suppressToggle;

    public DashboardPage()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Wire state events
        App.State.RunStateChanged += OnRunStateChanged;
        App.State.RunCompleted    += OnRunCompleted;

        // Sync controls from config
        _suppressToggle = true;
        IntervalDisplay.Text   = App.Config.IntervalHours.ToString();
        ScheduleSwitch.IsOn    = App.Config.ScheduleEnabled;
        _suppressToggle = false;

        RefreshLastRun();
        RefreshScheduleDisplay();

        _ticker.Tick += (_, _) => RefreshScheduleDisplay();
        _ticker.Start();

        // Reflect current run state (page may have been navigated to mid-run)
        ApplyRunState(App.State.IsRunning);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _ticker.Stop();
        App.State.RunStateChanged -= OnRunStateChanged;
        App.State.RunCompleted    -= OnRunCompleted;
    }

    // ── Run button ─────────────────────────────────────────────────────────

    private void RunBtn_Click(object sender, RoutedEventArgs e)
    {
        if (App.State.IsRunning)
        {
            App.State.RequestStop();
        }
        else
        {
            // Navigate to Last Run so the user sees live output
            App.Window.NavigateTo<LastRunPage>();
            _ = App.RunAutomationAsync();
        }
    }

    private void OnRunStateChanged(bool isRunning) =>
        DispatcherQueue.TryEnqueue(() => ApplyRunState(isRunning));

    private void ApplyRunState(bool isRunning)
    {
        RunBtnIcon.Glyph = isRunning ? "\uE71A" : "\uE768"; // Stop : Play
        RunBtnText.Text  = isRunning ? "Stop"    : "Run Now";
        RunBtn.Style     = isRunning
            ? (Style)Application.Current.Resources["DefaultButtonStyle"]
            : (Style)Application.Current.Resources["AccentButtonStyle"];
    }

    private void OnRunCompleted(RunResult _) =>
        DispatcherQueue.TryEnqueue(RefreshLastRun);

    // ── Last Run card ──────────────────────────────────────────────────────

    private void RefreshLastRun()
    {
        if (App.Config.LastRun is { } ts && DateTime.TryParse(ts, out var dt))
        {
            LastRunTime.Text = dt.ToString("MMM d, yyyy 'at' h:mm tt");

            if (App.State.Log.Count > 0)
            {
                var r = App.State.Log[^1];
                LastRunSummary.Text =
                    $"{r.OrdersProcessed} orders processed  ·  " +
                    $"{r.FilesQueued} files queued  ·  " +
                    $"{r.Skipped} skipped";
            }
        }
        else
        {
            LastRunTime.Text    = "Never";
            LastRunSummary.Text = "No runs yet";
        }
    }

    // ── Schedule card ──────────────────────────────────────────────────────

    private void RefreshScheduleDisplay()
    {
        if (!App.Config.ScheduleEnabled || App.SchedulerService.NextRunTime is not { } next)
        {
            NextRunTime.Text = "Paused";
            Countdown.Text   = "Schedule is disabled";
            return;
        }

        NextRunTime.Text = next.ToString("h:mm tt");

        var delta = next - DateTime.Now;
        var total = (int)delta.TotalSeconds;
        Countdown.Text = total > 0
            ? total >= 3600
                ? $"in {total / 3600}h {total % 3600 / 60}m {total % 60}s"
                : $"in {total / 60}m {total % 60}s"
            : "Running now…";
    }

    // ── Interval stepper ───────────────────────────────────────────────────

    private void DecInterval_Click(object sender, RoutedEventArgs e) => AdjustInterval(-1);
    private void IncInterval_Click(object sender, RoutedEventArgs e) => AdjustInterval(+1);

    private void AdjustInterval(int delta)
    {
        var val = Math.Clamp(App.Config.IntervalHours + delta, 1, 24);
        App.Config.IntervalHours = val;
        App.ConfigService.Save(App.Config);
        IntervalDisplay.Text = val.ToString();
        App.SchedulerService.SetSchedule(App.Config.ScheduleEnabled, val);
        RefreshScheduleDisplay();
    }

    // ── Schedule toggle ────────────────────────────────────────────────────

    private void ScheduleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;
        App.Config.ScheduleEnabled = ScheduleSwitch.IsOn;
        App.ConfigService.Save(App.Config);
        App.SchedulerService.SetSchedule(ScheduleSwitch.IsOn, App.Config.IntervalHours);
        RefreshScheduleDisplay();
    }
}
