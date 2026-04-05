using System;
using System.Collections.ObjectModel;
using System.Text;
using DtfOrderAutomation.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;

namespace DtfOrderAutomation.Pages;

// ── View model for a single order row ─────────────────────────────────────

public class OrderRow
{
    public string  OrderId { get; set; } = "";
    public string  Product { get; set; } = "";
    public string  Size    { get; set; } = "";
    public string? File    { get; set; }
    public string  Status  { get; set; } = "";

    public string StatusIcon => Status == "ok" ? "✓" : "⚠";

    public Brush StatusColor => Status == "ok"
        ? (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
        : (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
}

// ── Page ───────────────────────────────────────────────────────────────────

public sealed partial class LastRunPage : Page
{
    private readonly StringBuilder _liveBuffer = new();

    public ObservableCollection<OrderRow> ProcessedRows { get; } = new();
    public ObservableCollection<OrderRow> UnmappedRows  { get; } = new();

    public LastRunPage()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        App.State.LogLine         += OnLogLine;
        App.State.RunCompleted    += OnRunCompleted;
        App.State.RunStateChanged += OnRunStateChanged;

        if (App.State.IsRunning)
            ShowLiveLog();
        else
            PopulateFromHistory();
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
                ShowLiveLog();
            });
    }

    private void OnLogLine(string line)
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _liveBuffer.AppendLine(line);
            LogText.Text = _liveBuffer.ToString();
            LiveLogPanel.ChangeView(null, LiveLogPanel.ScrollableHeight, null);
        });
    }

    private void OnRunCompleted(RunResult _) =>
        DispatcherQueue.TryEnqueue(PopulateFromHistory);

    // ── Panel switching ────────────────────────────────────────────────────

    private void ShowLiveLog()
    {
        LiveLogPanel.Visibility = Visibility.Visible;
        ResultsPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowResults()
    {
        LiveLogPanel.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Visible;
    }

    // ── Populate two lists from last history entry ─────────────────────────

    private void PopulateFromHistory()
    {
        ProcessedRows.Clear();
        UnmappedRows.Clear();

        if (App.State.Log.Count == 0)
        {
            SummaryTimestamp.Text     = "No runs yet.";
            SummaryOrders.Text        = "";
            SummaryFiles.Text         = "";
            SummarySkipped.Visibility = Visibility.Collapsed;
            ProcessedHeader.Visibility = Visibility.Collapsed;
            UnmappedHeader.Visibility  = Visibility.Collapsed;
            ShowResults();
            return;
        }

        var r = App.State.Log[^1];

        SummaryTimestamp.Text = r.Timestamp;
        SummaryOrders.Text    = $"{r.OrdersProcessed} orders";
        SummaryFiles.Text     = $"{r.FilesQueued} files queued";

        if (r.Skipped > 0)
        {
            SummarySkipped.Text       = $"{r.Skipped} skipped";
            SummarySkipped.Visibility = Visibility.Visible;
        }
        else
        {
            SummarySkipped.Visibility = Visibility.Collapsed;
        }

        foreach (var o in r.OrderDetails)
        {
            var row = new OrderRow
            {
                OrderId = o.OrderId,
                Product = o.Product,
                Size    = o.Size,
                File    = o.File ?? "—",
                Status  = o.Status,
            };

            if (o.Status == "ok")
                ProcessedRows.Add(row);
            else
                UnmappedRows.Add(row);
        }

        foreach (var s in r.SkippedDetails)
            UnmappedRows.Add(new OrderRow
            {
                OrderId = s.OrderId,
                Product = s.Reason,
                Size    = "—",
                Status  = "skipped",
            });

        ProcessedHeader.Visibility = ProcessedRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnmappedHeader.Visibility  = UnmappedRows.Count  > 0 ? Visibility.Visible : Visibility.Collapsed;

        ShowResults();
    }

    // ── Processed row tap → detail modal ──────────────────────────────────

    private async void ProcessedRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not OrderRow row) return;

        App.State.ProductImages.TryGetValue(row.Product, out var imageUrl);

        var panel = new StackPanel { Spacing = 12, Width = 300 };

        if (!string.IsNullOrEmpty(imageUrl))
            panel.Children.Add(new Image
            {
                Source              = new BitmapImage(new Uri(imageUrl)),
                Height              = 220,
                Stretch             = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
            });

        AddDetailRow(panel, "Order",   row.OrderId);
        AddDetailRow(panel, "Product", row.Product);
        AddDetailRow(panel, "Size",    row.Size);
        AddDetailRow(panel, "File",    row.File ?? "—");

        var dialog = new ContentDialog
        {
            Title           = "Order Details",
            Content         = panel,
            CloseButtonText = "Close",
            XamlRoot        = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    // ── Unmapped row → Map Design File button ─────────────────────────────

    private async void UnmappedMapBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not OrderRow row) return;

        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
            picker.FileTypeFilter.Add(ext);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        // Save mapping
        var mapping = App.MappingService.Load();
        mapping[row.Product] = file.Name;
        App.MappingService.Save(mapping);

        // Move row from unmapped to processed
        UnmappedRows.Remove(row);
        row.File   = file.Name;
        row.Status = "ok";
        ProcessedRows.Add(row);

        ProcessedHeader.Visibility = Visibility.Visible;
        UnmappedHeader.Visibility  = UnmappedRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static void AddDetailRow(StackPanel panel, string label, string value)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new TextBlock { Text = label + ":", FontWeight = FontWeights.SemiBold, Width = 64 });
        row.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(row);
    }

    // ── Clear ──────────────────────────────────────────────────────────────

    private void ClearBtn_Click(object sender, RoutedEventArgs e)
    {
        ProcessedRows.Clear();
        UnmappedRows.Clear();
        LogText.Text              = "";
        SummaryTimestamp.Text     = "";
        SummaryOrders.Text        = "";
        SummaryFiles.Text         = "";
        SummarySkipped.Visibility = Visibility.Collapsed;
        ProcessedHeader.Visibility = Visibility.Collapsed;
        UnmappedHeader.Visibility  = Visibility.Collapsed;
    }
}
