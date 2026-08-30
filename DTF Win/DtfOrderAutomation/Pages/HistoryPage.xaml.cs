using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DtfOrderAutomation.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DtfOrderAutomation.Pages;

// ── One order line item within a run ───────────────────────────────────────
public class HistoryDetailRow
{
    public string Product          { get; set; } = "";
    public string Size             { get; set; } = "";
    public string File             { get; set; } = "";
    public string StatusText       { get; set; } = "";
    public Brush  StatusColor      { get; set; } = new SolidColorBrush(Colors.Gray);
    public Brush  StatusBackground { get; set; } = new SolidColorBrush(Colors.Transparent);
}

// ── One run (expandable) ───────────────────────────────────────────────────
public class HistoryItem
{
    public string DateDisplay      { get; set; } = "";
    public string SummaryLine      { get; set; } = "";
    public string StatusDisplay    { get; set; } = "";
    public Brush  StatusColor      { get; set; } = new SolidColorBrush(Colors.White);
    public Brush  StatusBackground { get; set; } = new SolidColorBrush(Colors.Transparent);

    public List<HistoryDetailRow> Details { get; set; } = new();

    public Visibility EmptyHintVisibility =>
        Details.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
}

public sealed partial class HistoryPage : Page
{
    public ObservableCollection<HistoryItem> HistoryItems { get; } = new();

    public HistoryPage()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        App.State.RunCompleted += OnRunCompleted;
        Populate();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.State.RunCompleted -= OnRunCompleted;
    }

    private void OnRunCompleted(RunResult _) =>
        DispatcherQueue.TryEnqueue(Populate);

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    private void Populate()
    {
        HistoryItems.Clear();

        var successFg = Res("SystemFillColorSuccessBrush");
        var successBg = Res("SystemFillColorSuccessBackgroundBrush");
        var cautionFg = Res("SystemFillColorCautionBrush");
        var cautionBg = Res("SystemFillColorCautionBackgroundBrush");
        var neutralFg = Res("TextFillColorSecondaryBrush");
        var neutralBg = Res("ControlFillColorSecondaryBrush");

        foreach (var r in App.State.Log.AsEnumerable().Reverse())
        {
            DateTime.TryParse(r.Timestamp, out var dt);

            var (statusText, headFg, headBg) = r.Status switch
            {
                "success" => ("✓ Success", successFg, successBg),
                "stopped" => ("⚠ Stopped", cautionFg, cautionBg),
                _         => ("⚠ Issues",  cautionFg, cautionBg),
            };

            int sentCount = r.OrderDetails.Count(d => d.SentToCadLink || d.Status == "already_sent");

            // ── Build per-item detail rows ────────────────────────────────
            var details = new List<HistoryDetailRow>();

            foreach (var o in r.OrderDetails)
            {
                bool sent = o.SentToCadLink || o.Status == "already_sent";

                string st; Brush fg, bg;
                if (sent)                 { st = "Sent";    fg = successFg; bg = successBg; }
                else if (o.Status == "ok"){ st = "Ready";   fg = neutralFg; bg = neutralBg; }
                else                      { st = "Skipped"; fg = cautionFg; bg = cautionBg; }

                details.Add(new HistoryDetailRow
                {
                    Product          = string.IsNullOrEmpty(o.Product) ? "(unknown)" : o.Product,
                    Size             = string.IsNullOrEmpty(o.Size)    ? "—" : o.Size,
                    File             = string.IsNullOrEmpty(o.File)    ? "—" : o.File!,
                    StatusText       = st,
                    StatusColor      = fg,
                    StatusBackground = bg,
                });
            }

            // Surface run-level / sizing skips that have no line-item row
            var lineItemOrderIds = r.OrderDetails
                .Select(d => d.OrderId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var s in r.SkippedDetails)
            {
                if (lineItemOrderIds.Contains(s.OrderId)) continue;
                details.Add(new HistoryDetailRow
                {
                    Product          = s.Reason,
                    Size             = "—",
                    File             = "—",
                    StatusText       = "Skipped",
                    StatusColor      = cautionFg,
                    StatusBackground = cautionBg,
                });
            }

            HistoryItems.Add(new HistoryItem
            {
                DateDisplay      = dt.ToString("MMM d  h:mm tt"),
                SummaryLine      = $"{r.OrdersProcessed} matched  ·  {sentCount} sent to CadLink  ·  {r.Skipped} skipped",
                StatusDisplay    = statusText,
                StatusColor      = headFg,
                StatusBackground = headBg,
                Details          = details,
            });
        }
    }
}
