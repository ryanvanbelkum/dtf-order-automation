using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;

namespace DtfOrderAutomation.Pages;

// ── View model for a single mapping row ───────────────────────────────────

public class MappingItem : INotifyPropertyChanged
{
    /// <summary>Shopify product ID — used as the mapping key. Empty for legacy title-keyed entries.</summary>
    public string  ProductId   { get; set; } = "";
    public string  ProductName { get; set; } = "";
    public string? ImageUrl    { get; set; }

    private string _designFile = "";
    public string DesignFile
    {
        get => _designFile;
        set
        {
            _designFile = value;
            PropertyChanged?.Invoke(this, new(nameof(DesignFile)));
            PropertyChanged?.Invoke(this, new(nameof(DisplayFile)));
            PropertyChanged?.Invoke(this, new(nameof(FileColor)));
            PropertyChanged?.Invoke(this, new(nameof(IsMapped)));
        }
    }

    public bool   IsMapped    => !string.IsNullOrEmpty(DesignFile);
    public string DisplayFile => IsMapped ? DesignFile : "Not mapped";

    public Brush FileColor => IsMapped
        ? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
        : (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];

    public event PropertyChangedEventHandler? PropertyChanged;
}

// ── Page ──────────────────────────────────────────────────────────────────

public sealed partial class MappingPage : Page
{
    // All products loaded from Shopify; used as the source for filtering
    private readonly List<MappingItem> _allItems = new();

    // Bound to the ListView
    public ObservableCollection<MappingItem> DisplayedItems { get; } = new();

    // In-memory copy of the mapping (same dict the service saves)
    private Dictionary<string, string> _mapping = new();

    public MappingPage()
    {
        InitializeComponent();
        _mapping = App.MappingService.Load();
        LoadCachedProducts();
    }

    private void LoadCachedProducts()
    {
        var cached = App.ProductsService.Load();
        if (cached.Count == 0) return;

        _allItems.Clear();
        App.State.ProductImages.Clear();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in cached)
        {
            // Prefer keying by product ID; fall back to title for products without an ID
            var key = string.IsNullOrEmpty(p.Id) ? p.Title : p.Id;
            // Also check the old title-based key so legacy mappings surface correctly
            if (!_mapping.TryGetValue(key, out var file) || string.IsNullOrEmpty(file))
                _mapping.TryGetValue(p.Title, out file);
            _allItems.Add(new MappingItem { ProductId = p.Id, ProductName = p.Title, DesignFile = file ?? "", ImageUrl = p.ImageUrl });
            App.State.ProductImages[p.Title] = p.ImageUrl;
            seenKeys.Add(key);
            seenKeys.Add(p.Title);
        }

        // Surface any mapping entries whose keys don't match any known product
        // (e.g. title-keyed entries saved before product IDs were introduced)
        foreach (var kv in _mapping)
        {
            if (!seenKeys.Contains(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                _allItems.Add(new MappingItem { ProductName = kv.Key, DesignFile = kv.Value });
        }

        SearchBox.IsEnabled         = true;
        SearchBox.PlaceholderText   = "Filter by name…";
        UnmappedOnlyCheck.IsEnabled = true;
        ApplyFilter();
    }

    // ── Sync ──────────────────────────────────────────────────────────────

    private async void SyncBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateSettings()) return;

        SyncBtn.IsEnabled = false;
        SyncBtn.Content   = "⏳  Syncing…";
        SyncStatus.Text   = "";

        var (products, error) = await App.ShopifyService.FetchProductsAsync(App.Config);

        SyncBtn.IsEnabled = true;
        SyncBtn.Content   = "↻  Sync Products from Shopify";

        if (error is not null)
        {
            SyncStatus.Text = $"✗ {error}";
            return;
        }

        _allItems.Clear();
        App.State.ProductImages.Clear();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in products)
        {
            var key = string.IsNullOrEmpty(p.Id) ? p.Title : p.Id;
            if (!_mapping.TryGetValue(key, out var file) || string.IsNullOrEmpty(file))
                _mapping.TryGetValue(p.Title, out file);
            _allItems.Add(new MappingItem { ProductId = p.Id, ProductName = p.Title, DesignFile = file ?? "", ImageUrl = p.ImageUrl });
            App.State.ProductImages[p.Title] = p.ImageUrl;
            seenKeys.Add(key);
            seenKeys.Add(p.Title);
        }

        // Surface any mapping entries whose keys don't match any known product
        foreach (var kv in _mapping)
        {
            if (!seenKeys.Contains(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                _allItems.Add(new MappingItem { ProductName = kv.Key, DesignFile = kv.Value });
        }

        App.ProductsService.Save(products);

        SearchBox.IsEnabled         = true;
        SearchBox.PlaceholderText   = "Filter by name…";
        UnmappedOnlyCheck.IsEnabled = true;

        ApplyFilter();
    }

    // ── Filter ────────────────────────────────────────────────────────────

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void UnmappedOnlyCheck_Click(object sender, RoutedEventArgs e)    => ApplyFilter();

    private void ApplyFilter()
    {
        var query        = SearchBox.Text.Trim().ToLower();
        var unmappedOnly = UnmappedOnlyCheck.IsChecked == true;

        var filtered = _allItems
            .Where(i => !(unmappedOnly && i.IsMapped))
            .Where(i => string.IsNullOrEmpty(query) || i.ProductName.ToLower().Contains(query))
            .OrderBy(i => i.IsMapped ? 0 : 1)
            .ThenBy(i => i.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        DisplayedItems.Clear();
        foreach (var item in filtered)
            DisplayedItems.Add(item);

        var mapped = _allItems.Count(i => i.IsMapped);
        SyncStatus.Text = _allItems.Count > 0
            ? $"{_allItems.Count} products — {mapped} mapped" +
              (filtered.Count != _allItems.Count ? $" — {filtered.Count} shown" : "")
            : "";
    }

    // ── Browse ────────────────────────────────────────────────────────────

    private async void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not MappingItem tagItem) return;
        var productName = tagItem.ProductName;

        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        picker.SuggestedStartLocation = PickerLocationId.Desktop;
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
            picker.FileTypeFilter.Add(ext);

        if (!string.IsNullOrEmpty(App.Config.DesignsFolder))
        {
            try
            {
                picker.SuggestedStartLocation = PickerLocationId.Desktop;
                // There's no direct path setter in WinRT picker, but starting location is approximate
            }
            catch { /* ignore */ }
        }

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        // Update in-memory model — key by product ID when available, otherwise title
        var key = string.IsNullOrEmpty(tagItem.ProductId) ? productName : tagItem.ProductId;
        _mapping[key] = file.Name;
        var item = _allItems.FirstOrDefault(i => i.ProductName == productName);
        if (item is not null) item.DesignFile = file.Name;

        ApplyFilter(); // refresh counts
    }

    // ── Save ──────────────────────────────────────────────────────────────

    private async void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        // Pull current DesignFile values back into the dictionary, keyed by product ID when available
        foreach (var item in _allItems)
        {
            var key = string.IsNullOrEmpty(item.ProductId) ? item.ProductName : item.ProductId;
            if (!string.IsNullOrEmpty(item.DesignFile))
                _mapping[key] = item.DesignFile;
            else
                _mapping.Remove(key);
        }

        App.MappingService.Save(_mapping);

        var mapped = _allItems.Count(i => i.IsMapped);
        var dialog = new ContentDialog
        {
            Title           = "Saved",
            Content         = $"Mappings saved — {mapped} product(s) mapped.",
            CloseButtonText = "OK",
            XamlRoot        = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    // ── Row click → detail modal ──────────────────────────────────────────

    private async void ProductList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not MappingItem item) return;
        await ShowProductDetailAsync(item.ProductName, item.DesignFile, item.IsMapped, item.ImageUrl);
    }

    private async Task ShowProductDetailAsync(string productName, string designFile, bool isMapped, string? imageUrl)
    {
        var panel = new StackPanel { Spacing = 12, Width = 300 };

        if (!string.IsNullOrEmpty(imageUrl))
        {
            panel.Children.Add(new Image
            {
                Source              = new BitmapImage(new Uri(imageUrl)),
                Height              = 220,
                Stretch             = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text         = productName,
            FontWeight   = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        panel.Children.Add(new TextBlock
        {
            Text         = isMapped ? $"Design file:  {designFile}" : "No design file mapped",
            Foreground   = isMapped
                ? (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
                : (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            TextWrapping = TextWrapping.Wrap,
        });

        var dialog = new ContentDialog
        {
            Title           = "Product Details",
            Content         = panel,
            CloseButtonText = "Close",
            XamlRoot        = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private bool ValidateSettings()
    {
        if (!string.IsNullOrWhiteSpace(App.Config.ShopifyStoreUrl) &&
            !string.IsNullOrWhiteSpace(App.Config.ShopifyClientId) &&
            !string.IsNullOrWhiteSpace(App.Config.ShopifyClientSecret))
            return true;

        SyncStatus.Text = "✗ Enter Shopify Store URL, Client ID, and Client Secret in Settings first";
        return false;
    }
}
