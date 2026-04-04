using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DtfOrderAutomation.Models;

namespace DtfOrderAutomation.Services;

public class AutomationService
{
    private static readonly HashSet<string> ChildSizes =
        new(StringComparer.OrdinalIgnoreCase) { "YXS", "YS", "YM", "YL" };

    private readonly ShopifyService _shopify;
    private readonly MappingService _mappingService;

    public AutomationService(ShopifyService shopify, MappingService mappingService)
    {
        _shopify        = shopify;
        _mappingService = mappingService;
    }

    public async Task<RunResult> RunAsync(
        AppConfig config,
        Action<string> log,
        CancellationToken ct = default)
    {
        var result = new RunResult { Timestamp = DateTime.Now.ToString("O"), Status = "success" };

        // ── Mappings ──────────────────────────────────────────────────────
        log("Loading product mappings…");
        var mapping = _mappingService.Load();
        log(mapping.Count > 0
            ? $"  ✓ {mapping.Count} product(s) mapped"
            : "  ⚠ No mappings configured — go to the Mapping tab to set up your products");

        ct.ThrowIfCancellationRequested();

        // ── Fetch orders ──────────────────────────────────────────────────
        log("\nFetching orders from Shopify…");
        List<JsonElement>? orders;
        try
        {
            orders = await _shopify.FetchOrdersAsync(config);
        }
        catch (Exception ex)
        {
            log($"  ✗ Could not connect to Shopify: {ex.Message}");
            result.Status = "error";
            result.SkippedDetails.Add(new() { OrderId = "—", Reason = "Could not connect to Shopify" });
            return result;
        }

        if (orders is null)
        {
            log("  ✗ Auth failed — check Client ID, Secret, and Store URL in Settings");
            result.Status = "error";
            result.SkippedDetails.Add(new() { OrderId = "—", Reason = "Authentication failed" });
            return result;
        }

        log($"  ✓ Found {orders.Count} unfulfilled order(s)");

        if (orders.Count == 0)
        {
            log("\nNothing to do.");
            return result;
        }

        log("");

        // ── Process each order ────────────────────────────────────────────
        foreach (var order in orders)
        {
            ct.ThrowIfCancellationRequested();

            var orderId   = order.TryGetProperty("name", out var n) ? n.GetString()! : order.GetProperty("id").GetRawText();
            var lineItems = order.TryGetProperty("line_items", out var li)
                          ? li.EnumerateArray().ToList()
                          : new List<JsonElement>();

            log($"Order {orderId}  ({lineItems.Count} line item(s))");

            foreach (var item in lineItems)
            {
                ct.ThrowIfCancellationRequested();

                var productName = item.TryGetProperty("name", out var pn) ? pn.GetString()?.Trim() ?? "" : "";
                var size        = ExtractSize(item);

                if (!mapping.TryGetValue(productName, out var designFile) || string.IsNullOrEmpty(designFile))
                {
                    log($"  ⚠ {productName} ({size}) — not in mapping, skipped");
                    result.Skipped++;
                    result.SkippedDetails.Add(new() { OrderId = orderId, Reason = $"'{productName}' not in mapping" });
                    result.OrderDetails.Add(new() { OrderId = orderId, Product = productName, Size = size, Status = "skipped" });
                    continue;
                }

                var designPath = Path.Combine(config.DesignsFolder, designFile);
                if (!File.Exists(designPath))
                {
                    log($"  ⚠ {productName} ({size}) — design file not found: {designFile}");
                    result.Skipped++;
                    result.SkippedDetails.Add(new() { OrderId = orderId, Reason = $"Design file not found: {designFile}" });
                    result.OrderDetails.Add(new() { OrderId = orderId, Product = productName, Size = size, Status = "skipped", File = designFile });
                    continue;
                }

                try
                {
                    var (widthIn, heightIn) = CalculateSize(designPath, size);

                    var baseName  = $"{orderId}_{productName}_{size}".Replace(" ", "_").Replace("/", "-");
                    var jhdrName  = baseName + ".jhdr";
                    var imgName   = baseName + Path.GetExtension(designFile);
                    var jhdrPath  = Path.Combine(config.HotFolder, jhdrName);
                    var imgDst    = Path.Combine(config.HotFolder, imgName);

                    WriteJhdr(jhdrPath, widthIn, heightIn);
                    await Task.Delay(100, ct);
                    File.Copy(designPath, imgDst, overwrite: true);

                    log($"  ✓ {productName} ({size})  →  {imgName}  [{widthIn}\" × {heightIn}\"]");
                    result.FilesQueued++;
                    result.OrdersProcessed++;
                    result.HotFolderFiles.AddRange(new[] { jhdrName, imgName });
                    result.OrderDetails.Add(new() { OrderId = orderId, Product = productName, Size = size, Status = "ok", File = imgName });
                }
                catch (Exception ex)
                {
                    log($"  ✗ {productName} ({size}) — error: {ex.Message}");
                    result.Skipped++;
                    result.SkippedDetails.Add(new() { OrderId = orderId, Reason = ex.Message });
                }
            }
        }

        if (result.Skipped > 0 && result.OrdersProcessed == 0)
            result.Status = "error";
        else if (result.Skipped > 0)
            result.Status = "partial";

        log($"\n{"─".PadRight(48, '─')}\n" +
            $"Done: {result.OrdersProcessed} processed · {result.FilesQueued} queued · {result.Skipped} skipped");

        return result;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string ExtractSize(JsonElement lineItem)
    {
        if (lineItem.TryGetProperty("properties", out var props))
            foreach (var prop in props.EnumerateArray())
            {
                var name = prop.TryGetProperty("name", out var n) ? n.GetString()?.ToLower() : null;
                if (name == "size" && prop.TryGetProperty("value", out var v))
                    return v.GetString()?.Trim().ToUpper() ?? "?";
            }

        if (lineItem.TryGetProperty("variant_title", out var vt) && vt.GetString() is { } vtStr)
        {
            var parts = vtStr.Split(" / ");
            if (parts.Length > 0) return parts[0].Trim().ToUpper();
        }

        return "?";
    }

    private static (double WidthIn, double HeightIn) CalculateSize(string imagePath, string size)
    {
        using var bmp = new System.Drawing.Bitmap(imagePath);
        int wPx = bmp.Width, hPx = bmp.Height;

        bool isChild     = ChildSizes.Contains(size);
        bool isLandscape = wPx > hPx;

        double widthIn, heightIn;
        if (isLandscape)
        {
            widthIn  = isChild ? 11.0 : 12.0;
            heightIn = Math.Round(widthIn * ((double)hPx / wPx), 3);
        }
        else
        {
            widthIn  = isChild ? 10.0 : 11.0;
            heightIn = widthIn;
        }
        return (widthIn, heightIn);
    }

    private static void WriteJhdr(string path, double widthIn, double heightIn)
    {
        double ptsW = Math.Round(widthIn  * 72, 2);
        double ptsH = Math.Round(heightIn * 72, 2);
        File.WriteAllText(path,
            $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n" +
            $"<JHDR>\n" +
            $"  <Size sizetype=\"0\" width=\"{ptsW}\" height=\"{ptsH}\" />\n" +
            $"</JHDR>\n");
    }
}
