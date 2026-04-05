using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DtfOrderAutomation.Models;

namespace DtfOrderAutomation.Services;

/// <summary>
/// Persists the Shopify product list (with image URLs) locally so MappingPage
/// shows products immediately on launch without requiring a re-sync every time.
/// File: %LocalAppData%\DtfOrderAutomation\dtf_products.json
/// </summary>
public class ProductsService
{
    private static readonly string ProductsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DtfOrderAutomation", "dtf_products.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public List<ShopifyProduct> Load()
    {
        if (!File.Exists(ProductsPath)) return new();
        try
        {
            var json = File.ReadAllText(ProductsPath);
            return JsonSerializer.Deserialize<List<ShopifyProduct>>(json, Options) ?? new();
        }
        catch { return new(); }
    }

    public void Save(List<ShopifyProduct> products)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProductsPath)!);
        File.WriteAllText(ProductsPath, JsonSerializer.Serialize(products, Options));
    }
}
