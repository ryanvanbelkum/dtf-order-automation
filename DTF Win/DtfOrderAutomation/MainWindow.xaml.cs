using System;
using System.Threading.Tasks;
using DtfOrderAutomation.Pages;
using DtfOrderAutomation.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DtfOrderAutomation;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Fully exit when the window is closed.
        Closed += (_, _) => Application.Current.Exit();

        // Subscribe to run state for status badge
        App.State.RunStateChanged += OnRunStateChanged;
        App.State.RunCompleted    += OnRunCompleted;

        // Start on Dashboard
        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(DashboardPage));
    }

    // ── Navigation ─────────────────────────────────────────────────────────

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;

        var pageType = item.Tag?.ToString() switch
        {
            "DashboardPage" => typeof(DashboardPage),
            "MappingPage"   => typeof(MappingPage),
            "LastRunPage"   => typeof(LastRunPage),
            "HistoryPage"   => typeof(HistoryPage),
            "SettingsPage"  => typeof(SettingsPage),
            _               => null
        };

        if (pageType is not null && ContentFrame.CurrentSourcePageType != pageType)
            ContentFrame.Navigate(pageType);
    }

    public void NavigateTo<T>() where T : Page
    {
        foreach (NavigationViewItem item in NavView.MenuItems)
            if (item.Tag?.ToString() == typeof(T).Name) { NavView.SelectedItem = item; return; }
        foreach (NavigationViewItem item in NavView.FooterMenuItems)
            if (item.Tag?.ToString() == typeof(T).Name) { NavView.SelectedItem = item; return; }
    }

    // ── Status badge ───────────────────────────────────────────────────────

    private void OnRunStateChanged(bool isRunning)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusBadge.Text = isRunning ? "● Running" : "● Idle";
            StatusBadge.Foreground = isRunning
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(255, 255, 214, 10))
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
        });
    }

    private void OnRunCompleted(Models.RunResult result)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var (text, color) = result.Status switch
            {
                "success" => ("● Success",             Microsoft.UI.ColorHelper.FromArgb(255, 48, 209, 88)),
                "stopped" => ("● Stopped",             Microsoft.UI.ColorHelper.FromArgb(255, 255, 69, 58)),
                _         => ("● Completed w/ issues", Microsoft.UI.ColorHelper.FromArgb(255, 255, 214, 10)),
            };
            StatusBadge.Text       = text;
            StatusBadge.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(color);

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
            {
                await Task.Delay(5000);
                StatusBadge.Text       = "● Idle";
                StatusBadge.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
            });
        });
    }

    // ── Update prompt ──────────────────────────────────────────────────────

    public async void ShowUpdatePrompt(VersionInfo info)
    {
        var dialog = new ContentDialog
        {
            Title             = "Update Available",
            Content           = $"Version {info.Version} is available.\n\n{info.ReleaseNotes}",
            PrimaryButtonText = "Download & Install",
            CloseButtonText   = "Later",
            XamlRoot          = ContentFrame.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await App.UpdateService.DownloadAndInstallAsync(info.DownloadUrl);
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title           = "Update Failed",
                Content         = ex.Message,
                CloseButtonText = "OK",
                XamlRoot        = ContentFrame.XamlRoot,
            }.ShowAsync();
        }
    }
}
