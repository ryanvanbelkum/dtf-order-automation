using System;
using System.Collections.ObjectModel;
using System.Linq;
using DtfOrderAutomation.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DtfOrderAutomation.Pages;

public class HistoryItem
{
    public string DateDisplay     { get; set; } = "";
    public string OrdersProcessed { get; set; } = "";
    public string Skipped         { get; set; } = "";
    public string StatusDisplay   { get; set; } = "";
    public Brush  StatusColor     { get; set; } = new SolidColorBrush(Colors.White);
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

    private void Populate()
    {
        HistoryItems.Clear();
        foreach (var r in App.State.Log.AsEnumerable().Reverse())
        {
            DateTime.TryParse(r.Timestamp, out var dt);
            var (statusText, color) = r.Status switch
            {
                "success" => ("✓ Success", ColorHelper.FromArgb(255, 48, 209, 88)),
                "stopped" => ("⚠ Stopped", ColorHelper.FromArgb(255, 255, 214, 10)),
                _         => ("⚠ Issues",  ColorHelper.FromArgb(255, 255, 214, 10)),
            };

            HistoryItems.Add(new HistoryItem
            {
                DateDisplay     = dt.ToString("MMM d  h:mm tt"),
                OrdersProcessed = r.OrdersProcessed.ToString(),
                Skipped         = r.Skipped.ToString(),
                StatusDisplay   = statusText,
                StatusColor     = new SolidColorBrush(color),
            });
        }
    }
}
