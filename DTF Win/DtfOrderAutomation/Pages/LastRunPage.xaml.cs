using System;
using System.Text;
using DtfOrderAutomation.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DtfOrderAutomation.Pages;

public sealed partial class LastRunPage : Page
{
    // Buffer lines received during an active run
    private readonly StringBuilder _liveBuffer = new();

    public LastRunPage()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        App.State.LogLine      += OnLogLine;
        App.State.RunCompleted += OnRunCompleted;
        App.State.RunStateChanged += OnRunStateChanged;

        // Show last run result from history, or indicate an active run
        if (App.State.IsRunning)
        {
            _liveBuffer.Clear();
            LogText.Text = "";
        }
        else
        {
            PopulateFromHistory();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.State.LogLine         -= OnLogLine;
        App.State.RunCompleted    -= OnRunCompleted;
        App.State.RunStateChanged -= OnRunStateChanged;
    }

    // ── Live log ───────────────────────────────────────────────────────────

    private void OnRunStateChanged(bool isRunning)
    {
        if (isRunning)
            DispatcherQueue.TryEnqueue(() =>
            {
                _liveBuffer.Clear();
                LogText.Text = "";
            });
    }

    private void OnLogLine(string line)
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _liveBuffer.AppendLine(line);
            LogText.Text = _liveBuffer.ToString();
            // Scroll to bottom
            LogScroll.ChangeView(null, LogScroll.ScrollableHeight, null);
        });
    }

    private void OnRunCompleted(RunResult _) =>
        DispatcherQueue.TryEnqueue(PopulateFromHistory);

    // ── Populate from last history entry ───────────────────────────────────

    private void PopulateFromHistory()
    {
        if (App.State.Log.Count == 0)
        {
            LogText.Text = "No runs yet.";
            return;
        }

        var r  = App.State.Log[^1];
        var sb = new StringBuilder();

        sb.AppendLine($"Run completed: {r.Timestamp}");
        sb.AppendLine($"Status:        {r.Status.ToUpperInvariant()}");
        sb.AppendLine();
        sb.AppendLine($"Orders processed:  {r.OrdersProcessed}");
        sb.AppendLine($"Files queued:      {r.FilesQueued}");
        sb.AppendLine($"Skipped:           {r.Skipped}");
        sb.AppendLine();
        sb.AppendLine("── Orders ──────────────────────────────────────");

        foreach (var o in r.OrderDetails)
        {
            var icon = o.Status == "ok" ? "✓" : "⚠";
            sb.AppendLine($"  {icon}  {o.OrderId}  ·  {o.Product}  ({o.Size})  →  {o.File ?? "—"}");
        }

        if (r.SkippedDetails.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("── Skipped ─────────────────────────────────────");
            foreach (var s in r.SkippedDetails)
                sb.AppendLine($"  ⚠  {s.OrderId}  ·  {s.Reason}");
        }

        if (r.HotFolderFiles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("── Files dropped into hot folder ───────────────");
            foreach (var f in r.HotFolderFiles)
                sb.AppendLine($"  →  {f}");
        }

        LogText.Text = sb.ToString();
    }

    private void ClearBtn_Click(object sender, RoutedEventArgs e) => LogText.Text = "";
}
