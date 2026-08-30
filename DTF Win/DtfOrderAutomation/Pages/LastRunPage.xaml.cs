using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

public class OrderRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string  OrderId   { get; set; } = "";
    public string  ProductId { get; set; } = "";
    public string  Product   { get; set; } = "";
    public string  Size      { get; set; } = "";
    public string? File    { get; set; }
    public string  Status  { get; set; } = "";

    // Reference to the backing model — used by Send to CadLink
    public OrderDetail? SourceDetail { get; set; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; Notify(nameof(IsSelected)); } }
    }

    private bool _sent;
    public bool Sent
    {
        get => _sent;
        set
        {
            if (_sent != value)
            {
                _sent = value;
                Notify(nameof(Sent));
                Notify(nameof(SentBadgeVisibility));
            }
        }
    }

    public Visibility SentBadgeVisibility => Sent ? Visibility.Visible : Visibility.Collapsed;

    public string StatusIcon => Status == "ok" ? "✓" : "⚠";

    public Brush StatusColor => Status == "ok"
        ? (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
        : (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
}

// ── Page ───────────────────────────────────────────────────────────────────

public sealed partial class LastRunPage : Page
{
    private readonly StringBuilder _liveBuffer = new();

    public ObservableCollection<OrderRow> ProcessedRows   { get; } = new();
    public ObservableCollection<OrderRow> UnmappedRows    { get; } = new();
    public ObservableCollection<OrderRow> AlreadySentRows { get; } = new();

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
            LiveLogScroller.ChangeView(null, LiveLogScroller.ScrollableHeight, null);
        });
    }

    private void OnRunCompleted(RunResult _) =>
        DispatcherQueue.TryEnqueue(PopulateFromHistory);

    // ── Panel switching ────────────────────────────────────────────────────

    private void ShowLiveLog()
    {
        RunningRing.IsActive    = true;
        LiveLogPanel.Visibility = Visibility.Visible;
        ResultsPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowResults()
    {
        RunningRing.IsActive    = false;
        LiveLogPanel.Visibility = Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Visible;
    }

    // ── Populate two lists from last history entry ─────────────────────────

    private void PopulateFromHistory()
    {
        ProcessedRows.Clear();
        UnmappedRows.Clear();
        AlreadySentRows.Clear();
        UpdateSelectionState();

        if (App.State.Log.Count == 0)
        {
            SummaryTimestamp.Text     = "No runs yet.";
            SummaryOrders.Text        = "";
            SummaryFiles.Text         = "";
            SummarySkipped.Visibility    = Visibility.Collapsed;
            ProcessedHeader.Visibility   = Visibility.Collapsed;
            UnmappedHeader.Visibility    = Visibility.Collapsed;
            AlreadySentHeader.Visibility = Visibility.Collapsed;
            ShowResults();
            return;
        }

        var r = App.State.Log[^1];

        SummaryTimestamp.Text = r.Timestamp;
        SummaryOrders.Text    = $"{r.OrdersProcessed} orders";
        SummaryFiles.Text     = $"{r.FilesSent} files sent to CadLink";

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
                OrderId      = o.OrderId,
                ProductId    = o.ProductId,
                Product      = o.Product,
                Size         = o.Size,
                File         = o.File ?? "—",
                Status       = o.Status,
                SourceDetail = o,
                Sent         = o.SentToCadLink || o.Status == "already_sent",
            };

            if (o.Status == "already_sent")
                AlreadySentRows.Add(row);
            else if (o.Status == "ok")
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

        ProcessedHeader.Visibility   = ProcessedRows.Count   > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnmappedHeader.Visibility    = UnmappedRows.Count    > 0 ? Visibility.Visible : Visibility.Collapsed;
        AlreadySentHeader.Visibility = AlreadySentRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

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

    // Prevent checkbox clicks from bubbling up to the row's Tapped handler
    private void CheckBox_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

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

        // Save mapping — key by product ID when available so it survives title changes
        var mapping = App.MappingService.Load();
        var mappingKey = string.IsNullOrEmpty(row.ProductId) ? row.Product : row.ProductId;
        mapping[mappingKey] = file.Name;
        App.MappingService.Save(mapping);

        // Build source detail so this row can be sent to CadLink
        var detail = App.AutomationService.TryBuildMatchedDetail(
            row.OrderId, row.Product, row.Size, file.Path, file.Name);
        if (detail != null)
        {
            row.SourceDetail = detail;
            // Add to the last run's OrderDetails so SentToCadLink persists
            if (App.State.Log.Count > 0)
            {
                var lastRun = App.State.Log[^1];
                lastRun.OrderDetails.RemoveAll(d => d.OrderId == row.OrderId && d.Status == "skipped");
                lastRun.OrderDetails.Add(detail);
                App.LogService.Save(App.State.Log);
            }
        }

        // Move row from unmapped to processed
        UnmappedRows.Remove(row);
        row.File   = detail?.File ?? file.Name;
        row.Status = "ok";
        ProcessedRows.Add(row);

        ProcessedHeader.Visibility = Visibility.Visible;
        UnmappedHeader.Visibility  = UnmappedRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelectionState();
    }

    // ── Selection ──────────────────────────────────────────────────────────

    private void SelectAllCheck_Click(object sender, RoutedEventArgs e)
    {
        bool check = SelectAllCheck.IsChecked == true;
        foreach (var row in ProcessedRows) row.IsSelected = check;
        SendToCadLinkBtn.IsEnabled = check && ProcessedRows.Count > 0;
    }

    private void RowCheckBox_Click(object sender, RoutedEventArgs e) => UpdateSelectionState();

    private void UpdateSelectionState()
    {
        bool any = ProcessedRows.Any(r => r.IsSelected);
        bool all = ProcessedRows.Count > 0 && ProcessedRows.All(r => r.IsSelected);
        SendToCadLinkBtn.IsEnabled = any;
        SelectAllCheck.IsChecked   = all;
    }

    // ── Send to CadLink ────────────────────────────────────────────────────

    private async void SendToCadLinkBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = ProcessedRows
            .Where(r => r.IsSelected && r.SourceDetail != null)
            .Select(r => r.SourceDetail!)
            .ToList();

        if (selected.Count == 0) return;

        var config = App.Config;
        if (string.IsNullOrEmpty(config.HotFolder))
        {
            await new ContentDialog
            {
                Title           = "Hot Folder Not Set",
                Content         = "Please configure the CadLink hot folder in Settings before sending.",
                CloseButtonText = "OK",
                XamlRoot        = XamlRoot,
            }.ShowAsync();
            return;
        }

        SetSending(true);
        try
        {
            var dropped   = await Task.Run(() => App.AutomationService.SendToHotFolder(config, selected));
            int filesSent = dropped.Count / 2;

            // Tally the files just queued onto the run being displayed
            if (filesSent > 0 && App.State.Log.Count > 0)
            {
                App.State.Log[^1].FilesSent += filesSent;
                SummaryFiles.Text = $"{App.State.Log[^1].FilesSent} files sent to CadLink";
            }

            // Persist the updated SentToCadLink flags + file count
            App.LogService.Save(App.State.Log);

            // Mark rows as sent and deselect them
            foreach (var row in ProcessedRows.Where(r => r.IsSelected && r.SourceDetail?.SentToCadLink == true))
            {
                row.Sent       = true;
                row.IsSelected = false;
            }

            await new ContentDialog
            {
                Title           = "Sent to CadLink",
                Content         = filesSent > 0
                    ? $"Queued {filesSent} file(s) to the hot folder."
                    : "No new files were sent (all selected orders were already queued previously).",
                CloseButtonText = "OK",
                XamlRoot        = XamlRoot,
            }.ShowAsync();
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title           = "Send Failed",
                Content         = ex.Message,
                CloseButtonText = "OK",
                XamlRoot        = XamlRoot,
            }.ShowAsync();
        }
        finally
        {
            SetSending(false);
        }
    }

    // Toggle the "Send to CadLink" button between its idle and working states.
    private void SetSending(bool sending)
    {
        SendSpinner.IsActive    = sending;
        SendSpinner.Visibility  = sending ? Visibility.Visible : Visibility.Collapsed;
        SendBtnText.Text        = sending ? "Sending…" : "Send to CadLink";

        // Lock out controls that would interfere with an in-flight send
        ClearBtn.IsEnabled       = !sending;
        SelectAllCheck.IsEnabled = !sending;

        if (sending)
            SendToCadLinkBtn.IsEnabled = false;
        else
            UpdateSelectionState();   // restores correct enabled state from selection
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
        AlreadySentRows.Clear();
        LogText.Text              = "";
        SummaryTimestamp.Text     = "";
        SummaryOrders.Text        = "";
        SummaryFiles.Text         = "";
        SummarySkipped.Visibility    = Visibility.Collapsed;
        ProcessedHeader.Visibility   = Visibility.Collapsed;
        UnmappedHeader.Visibility    = Visibility.Collapsed;
        AlreadySentHeader.Visibility = Visibility.Collapsed;
        UpdateSelectionState();
    }
}
