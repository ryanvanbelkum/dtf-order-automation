using System;
using System.Linq;
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
        BuildChart();

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
        DispatcherQueue.TryEnqueue(() => { RefreshLastRun(); RefreshStats(); BuildChart(); });

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
            StatSuccessPct.Text   = "—";
            StatSuccessRate.Text  = "No runs yet";
            return;
        }

        int totalOrders  = 0;
        int totalFiles   = 0;
        int totalSkipped = 0;
        int successCount = 0;

        foreach (var r in log)
        {
            totalOrders  += r.OrdersProcessed;
            totalFiles   += r.FilesSent;
            totalSkipped += r.Skipped;
            if (r.Status == "success") successCount++;
        }

        StatTotalRuns.Text    = log.Count.ToString("N0");
        StatTotalOrders.Text  = totalOrders.ToString("N0");
        StatTotalFiles.Text   = totalFiles.ToString("N0");
        StatTotalSkipped.Text = totalSkipped.ToString("N0");

        var pct    = (double)successCount / log.Count * 100;
        var avg    = (double)totalOrders / log.Count;
        StatSuccessPct.Text  = $"{pct:F0}%";
        StatSuccessRate.Text = $"{successCount} of {log.Count} run{(log.Count == 1 ? "" : "s")} clean  ·  {avg:F1} orders/run avg";
    }

    // ── Recent Activity chart ──────────────────────────────────────────────

    private void BuildChart()
    {
        ChartGrid.Children.Clear();
        ChartGrid.ColumnDefinitions.Clear();

        var log = App.State.Log;
        if (log.Count == 0)
        {
            ChartGrid.Visibility   = Visibility.Collapsed;
            ChartLegend.Visibility = Visibility.Collapsed;
            ChartEmpty.Visibility  = Visibility.Visible;
            return;
        }

        ChartGrid.Visibility   = Visibility.Visible;
        ChartLegend.Visibility = Visibility.Visible;
        ChartEmpty.Visibility  = Visibility.Collapsed;

        var runs = log.Skip(Math.Max(0, log.Count - 14)).ToList();
        int max  = runs.Max(r => r.OrdersProcessed + r.Skipped);
        if (max <= 0) max = 1;

        const double maxBarHeight = 150;

        var accent  = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var caution = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
        var faint   = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
        var label2  = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        for (int i = 0; i < runs.Count; i++)
        {
            var r = runs[i];

            ChartGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var cell = new Grid();
            cell.RowDefinitions.Add(new RowDefinition());                                  // bars
            cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });       // label

            // Column of stacked bars, anchored to the baseline
            var bars = new StackPanel
            {
                VerticalAlignment   = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Width   = 28,
                Spacing = 0,
            };

            // Count label above the bar
            bars.Children.Add(new TextBlock
            {
                Text                = r.OrdersProcessed.ToString(),
                FontSize            = 10,
                Foreground          = label2,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(0, 0, 0, 2),
            });

            double oh = r.OrdersProcessed / (double)max * maxBarHeight;
            double sh = r.Skipped         / (double)max * maxBarHeight;

            bool hasSkipped = r.Skipped > 0;

            if (hasSkipped)
                bars.Children.Add(new Border
                {
                    Background    = caution,
                    Height        = Math.Max(sh, 3),
                    CornerRadius  = new CornerRadius(4, 4, 0, 0),
                });

            if (r.OrdersProcessed > 0)
                bars.Children.Add(new Border
                {
                    Background    = accent,
                    Height        = Math.Max(oh, 3),
                    CornerRadius  = hasSkipped ? new CornerRadius(0) : new CornerRadius(4, 4, 0, 0),
                });

            // Empty run — show a faint stub so the column is still visible
            if (!hasSkipped && r.OrdersProcessed == 0)
                bars.Children.Add(new Border
                {
                    Background    = faint,
                    Height        = 3,
                    CornerRadius  = new CornerRadius(2),
                    Opacity       = 0.5,
                });

            var when = DateTime.TryParse(r.Timestamp, out var dt)
                ? dt.ToString("M/d")
                : "";

            ToolTipService.SetToolTip(bars,
                $"{(DateTime.TryParse(r.Timestamp, out var t) ? t.ToString("MMM d 'at' h:mm tt") : r.Timestamp)}\n" +
                $"{r.OrdersProcessed} orders  ·  {r.FilesSent} sent  ·  {r.Skipped} skipped");

            Grid.SetRow(bars, 0);
            cell.Children.Add(bars);

            var lbl = new TextBlock
            {
                Text                = when,
                FontSize            = 10,
                Foreground          = faint,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(0, 6, 0, 0),
            };
            Grid.SetRow(lbl, 1);
            cell.Children.Add(lbl);

            Grid.SetColumn(cell, i);
            ChartGrid.Children.Add(cell);
        }
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
                    $"{r.FilesSent} files sent  ·  " +
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
