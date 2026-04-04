using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace DtfOrderAutomation.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var cfg = App.Config;
        StoreUrlBox.Text      = cfg.ShopifyStoreUrl;
        ClientIdBox.Text      = cfg.ShopifyClientId;
        ClientSecretBox.Password = cfg.ShopifyClientSecret;
        DesignsFolderBox.Text = cfg.DesignsFolder;
        HotFolderBox.Text     = cfg.HotFolder;
        VersionLabel.Text     = $"DTF Order Automation v{AppVersion.Current}";
    }

    // ── Folder pickers ─────────────────────────────────────────────────────

    private async void BrowseDesignsFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path is not null) DesignsFolderBox.Text = path;
    }

    private async void BrowseHotFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path is not null) HotFolderBox.Text = path;
    }

    private static async System.Threading.Tasks.Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    // ── Save ───────────────────────────────────────────────────────────────

    private async void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var cfg = App.Config;
        cfg.ShopifyStoreUrl    = StoreUrlBox.Text.Trim().TrimEnd('/');
        cfg.ShopifyClientId    = ClientIdBox.Text.Trim();
        cfg.ShopifyClientSecret = ClientSecretBox.Password.Trim();
        cfg.DesignsFolder      = DesignsFolderBox.Text.Trim();
        cfg.HotFolder          = HotFolderBox.Text.Trim();

        // Clear cached token whenever credentials change
        cfg.ShopifyToken       = "";
        cfg.ShopifyTokenExpiry = "";

        App.ConfigService.Save(cfg);

        var dialog = new ContentDialog
        {
            Title           = "Saved",
            Content         = "Settings saved successfully.",
            CloseButtonText = "OK",
            XamlRoot        = XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
