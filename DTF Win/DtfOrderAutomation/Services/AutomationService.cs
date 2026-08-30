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
        CancellationToken ct              = default,
        DateTime? from                    = null,
        DateTime? to                      = null,
        HashSet<string>? alreadyProcessed = null)
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
        if (from.HasValue)
        {
            var toStr = to.HasValue ? to.Value.ToString("MMM d, h:mm tt") : "now";
            log($"\nFetching orders from Shopify… ({from.Value:MMM d, h:mm tt} → {toStr})");
        }
        else
        {
            log("\nFetching orders from Shopify…");
        }

        List<JsonElement>? orders;
        try
        {
            orders = await _shopify.FetchOrdersAsync(config, from, to);
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

            // Record already-sent orders so the UI can show them, but don't process again
            if (alreadyProcessed?.Contains(orderId) == true)
            {
                log($"Order {orderId} — already sent to CADlink");
                foreach (var item in lineItems)
                {
                    var pt  = item.TryGetProperty("title", out var t)   ? t.GetString()?.Trim() ?? "" : "";
                    var pid = item.TryGetProperty("product_id", out var p) ? p.GetRawText() : "";
                    var size = ExtractSize(item);
                    result.OrderDetails.Add(new() { OrderId = orderId, ProductId = pid, Product = pt, Size = size, Status = "already_sent", SentToCadLink = true });
                }
                continue;
            }

            log($"Order {orderId}  ({lineItems.Count} line item(s))");

            foreach (var item in lineItems)
            {
                ct.ThrowIfCancellationRequested();

                // `title` = product title only (e.g. "Custom T-Shirt")
                // `name`  = title + variant   (e.g. "Custom T-Shirt - L / Blue")
                var productTitle = item.TryGetProperty("title", out var pt) ? pt.GetString()?.Trim() ?? "" : "";
                var productName  = item.TryGetProperty("name",  out var pn) ? pn.GetString()?.Trim() ?? "" : "";
                var productId    = item.TryGetProperty("product_id", out var pid) ? pid.GetRawText() : "";
                var size         = ExtractSize(item);

                // Prefer product_id as the mapping key (stable, unaffected by title changes).
                // Fall back to title and then full name for backward compatibility with
                // mappings that were saved before product_id was introduced.
                string? designFile = null;
                var mappingKey = productId;
                if (string.IsNullOrEmpty(productId) || !mapping.TryGetValue(productId, out designFile) || string.IsNullOrEmpty(designFile))
                {
                    mappingKey = productTitle;
                    if (!mapping.TryGetValue(productTitle, out designFile) || string.IsNullOrEmpty(designFile))
                    {
                        mappingKey = productName;
                        mapping.TryGetValue(productName, out designFile);
                    }
                }

                if (string.IsNullOrEmpty(designFile))
                {
                    // Log with full name so user can see the variant, but store title as Product
                    // so it matches what the Mapping tab shows. The raw product_id is stored on
                    // ProductId and shown as its own field in the UI, so a mismatch against the
                    // Mapping tab's saved key is visible without digging through dtf_mapping.json.
                    var idNote = string.IsNullOrEmpty(productId) ? "no product_id on this line item" : $"product_id: {productId}";
                    log($"  ⚠ {productName} ({size}) — not in mapping ({idNote}), skipped");
                    result.Skipped++;
                    result.OrderDetails.Add(new() { OrderId = orderId, ProductId = productId, Product = $"{productTitle} — not in mapping", Size = size, Status = "skipped" });
                    continue;
                }

                // Mapping values may be absolute paths (set from Last Run page) or
                // plain filenames (set from Mapping tab, relative to DesignsFolder)
                var designPath = Path.IsPathRooted(designFile)
                    ? designFile
                    : Path.Combine(config.DesignsFolder, designFile);

                if (!File.Exists(designPath))
                {
                    log($"  ⚠ {productName} ({size}) — design file not found: {designFile}");
                    result.Skipped++;
                    result.OrderDetails.Add(new() { OrderId = orderId, ProductId = productId, Product = $"{productTitle} — design file not found", Size = size, Status = "skipped", File = designFile });
                    continue;
                }

                try
                {
                    var (widthIn, heightIn) = CalculateSize(designPath, size);

                    var imgName = BuildOutputFileName(orderId, size, designFile);

                    log($"  ✓ {productName} ({size})  →  {imgName}  [{widthIn}\" × {heightIn}\"]");
                    result.OrdersProcessed++;
                    result.OrderDetails.Add(new()
                    {
                        OrderId          = orderId,
                        ProductId        = productId,
                        Product          = productTitle,
                        Size             = size,
                        Status           = "ok",
                        File             = imgName,
                        DesignSourcePath = designPath,
                        PrintWidth       = widthIn,
                        PrintHeight      = heightIn,
                        SentToCadLink    = false,
                    });
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
            $"Done: {result.OrdersProcessed} matched · {result.Skipped} skipped — review below to send to CADlink");

        return result;
    }

    // ── Build a matched detail for a newly-assigned design file ───────────

    /// <summary>
    /// Tries to calculate print size and build a matched OrderDetail for a
    /// previously-skipped item. Returns null if the file can't be read.
    /// </summary>
    public OrderDetail? TryBuildMatchedDetail(
        string orderId, string product, string size,
        string designFilePath, string designFileName)
    {
        try
        {
            var (widthIn, heightIn) = CalculateSize(designFilePath, size);
            var imgName = BuildOutputFileName(orderId, size, designFileName);
            return new OrderDetail
            {
                OrderId          = orderId,
                Product          = product,
                Size             = size,
                Status           = "ok",
                File             = imgName,
                DesignSourcePath = designFilePath,
                PrintWidth       = widthIn,
                PrintHeight      = heightIn,
                SentToCadLink    = false,
            };
        }
        catch { return null; }
    }

    // ── Deferred hot-folder send ───────────────────────────────────────────

    /// <summary>
    /// Drops a JHDR job header + design image into the hot folder for each item.
    ///
    /// CadLink's hot folder requires the .jhdr to be present BEFORE the graphic
    /// appears, otherwise the graphic is imported without the size settings
    /// (see https://help.cadlink.com/.../job_header_files.htm). To guarantee
    /// this ordering — and to avoid CadLink ever seeing a half-copied image —
    /// the graphic is first staged under a ".part" name the hot folder ignores,
    /// the .jhdr is written, and only then is the graphic atomically renamed
    /// into place as the final import trigger.
    ///
    /// Marks each item SentToCadLink = true. Returns list of filenames dropped.
    /// </summary>
    public List<string> SendToHotFolder(AppConfig config, IEnumerable<OrderDetail> items)
    {
        var dropped = new List<string>();

        if (string.IsNullOrWhiteSpace(config.HotFolder))
            throw new InvalidOperationException("Hot folder is not configured.");
        Directory.CreateDirectory(config.HotFolder);

        foreach (var item in items)
        {
            if (item.SentToCadLink) continue;
            if (string.IsNullOrEmpty(item.DesignSourcePath) || string.IsNullOrEmpty(item.File)) continue;

            var baseName = Path.GetFileNameWithoutExtension(item.File);
            var jhdrName = baseName + ".jhdr";
            var jhdrPath = Path.Combine(config.HotFolder, jhdrName);
            var imgDst   = Path.Combine(config.HotFolder, item.File);
            var imgStage = imgDst + ".part";   // extension the hot folder won't import

            // 1. Stage the graphic under a non-image extension (not yet visible to CadLink).
            File.Copy(item.DesignSourcePath, imgStage, overwrite: true);

            // 2. Write the job header so the size settings are on disk first.
            WriteJhdr(jhdrPath, item.PrintWidth, item.PrintHeight);

            // 3. Give the .jhdr a moment to settle (matters on network/shared drives),
            //    then atomically reveal the graphic as the final import trigger.
            Thread.Sleep(150);
            if (File.Exists(imgDst)) File.Delete(imgDst);
            File.Move(imgStage, imgDst);

            item.SentToCadLink = true;
            dropped.Add(jhdrName);
            dropped.Add(item.File);
        }
        return dropped;
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

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c));
    }

    /// <summary>
    /// Builds the hot-folder output filename. The size is included so the same
    /// design ordered in two sizes (which can print at different dimensions —
    /// e.g. adult vs youth) does not collide on a single name and overwrite the
    /// other item's graphic and .jhdr.
    /// </summary>
    private static string BuildOutputFileName(string orderId, string size, string designFile)
    {
        var ext  = Path.GetExtension(designFile);
        var stem = Path.GetFileNameWithoutExtension(designFile);
        return SanitizeFileName($"{orderId}_{size}_{stem}{ext}");
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
