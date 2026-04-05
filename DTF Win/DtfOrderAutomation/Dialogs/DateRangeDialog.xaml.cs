using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DtfOrderAutomation.Dialogs;

public sealed partial class DateRangeDialog : ContentDialog
{
    public DateTime? From { get; private set; }
    public DateTime? To   { get; private set; }

    private DateTime? _lastRunTime;

    public DateRangeDialog(string? lastRun)
    {
        InitializeComponent();

        if (lastRun is not null && DateTime.TryParse(lastRun, out var dt))
        {
            _lastRunTime                = dt;
            SinceLastRunRadio.IsEnabled = true;
            LastRunHint.Text            = dt.ToString("MMM d, yyyy 'at' h:mm tt");

            // Pre-fill From with last run time so the user can tweak it
            FromDate.Date = new DateTimeOffset(dt.Date);
            FromTime.Time = dt.TimeOfDay;
        }
        else
        {
            SinceLastRunRadio.IsEnabled = false;
            LastRunHint.Text            = "No previous run recorded";

            // Default: yesterday at 6pm
            FromDate.Date = new DateTimeOffset(DateTime.Today.AddDays(-1));
            FromTime.Time = new TimeSpan(18, 0, 0);
        }

        // To: blank by default (= now), but seed the time picker to current time
        ToTime.Time = DateTime.Now.TimeOfDay;
    }

    private void RadioChanged(object sender, RoutedEventArgs e)
    {
        if (CustomPanel is null) return;
        CustomPanel.Visibility = AllOrdersRadio.IsChecked == true || SinceLastRunRadio.IsChecked == true
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void Preset_Yesterday6pmToNow(object sender, RoutedEventArgs e)
    {
        FromDate.Date = new DateTimeOffset(DateTime.Today.AddDays(-1));
        FromTime.Time = new TimeSpan(18, 0, 0);
        ToDate.Date   = null;
        ToTime.Time   = DateTime.Now.TimeOfDay;
    }

    private void Preset_TodayMidnightToNow(object sender, RoutedEventArgs e)
    {
        FromDate.Date = new DateTimeOffset(DateTime.Today);
        FromTime.Time = TimeSpan.Zero;
        ToDate.Date   = null;
        ToTime.Time   = DateTime.Now.TimeOfDay;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (SinceLastRunRadio.IsChecked == true)
        {
            From = _lastRunTime;
            To   = null;
        }
        else if (CustomRangeRadio.IsChecked == true)
        {
            From = FromDate.Date is { } fd
                ? fd.DateTime.Date + FromTime.Time
                : null;
            To = ToDate.Date is { } td
                ? td.DateTime.Date + ToTime.Time
                : null;
        }
        else // AllOrdersRadio
        {
            From = null;
            To   = null;
        }
    }
}
