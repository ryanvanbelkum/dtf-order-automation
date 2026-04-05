using System;
using DtfOrderAutomation.Dialogs;
using DtfOrderAutomation.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DtfOrderAutomation.Pages;

public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        App.State.RunStateChanged += OnRunStateChanged;
        App.State.RunCompleted    += OnRunCompleted;

        RefreshLastRun();
        RefreshStats();

        // Reflect current run state (page may have been navigated to mid-run)
        ApplyRunState(App.State.IsRunning);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.State.RunStateChanged -= OnRunStateChanged;
        App.State.RunCompleted    -= OnRunCompleted;
    }

    // ── Run button ─────────────────────────────────────────────────────────

    private async void RunBtn_Click(object sender, RoutedEventArgs e)
    {
        if (App.State.IsRunning)
        {
            App.State.RequestStop();
            return;
        }

        var dialog = new DateRangeDialog(App.Config.LastRun)
        {
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        // Navigate to Last Run so the user sees live output
        App.Window.NavigateTo<LastRunPage>();
        _ = App.RunAutomationAsync(dialog.From, dialog.To);
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
        DispatcherQueue.TryEnqueue(() => { RefreshLastRun(); RefreshStats(); });

    // ── All-Time Stats card ────────────────────────────────────────────────

    private void RefreshStats()
    {
        var log = App.State.Log;
        if (log.Count == 0)
        {
            StatTotalRuns.Text    = "0";
            StatTotalOrders.Text  = "0";
            StatTotalFiles.Text   = "0";
            StatTotalSkipped.Text = "0";
            StatSuccessRate.Text  = "";
            return;
        }

        int totalOrders  = 0;
        int totalFiles   = 0;
        int totalSkipped = 0;
        int successCount = 0;

        foreach (var r in log)
        {
            totalOrders  += r.OrdersProcessed;
            totalFiles   += r.FilesQueued;
            totalSkipped += r.Skipped;
            if (r.Status == "success") successCount++;
        }

        StatTotalRuns.Text    = log.Count.ToString("N0");
        StatTotalOrders.Text  = totalOrders.ToString("N0");
        StatTotalFiles.Text   = totalFiles.ToString("N0");
        StatTotalSkipped.Text = totalSkipped.ToString("N0");

        var pct = (double)successCount / log.Count * 100;
        StatSuccessRate.Text = $"{pct:F0}% success rate across {log.Count} run{(log.Count == 1 ? "" : "s")}";
    }

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

}
