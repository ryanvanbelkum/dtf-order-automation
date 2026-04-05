using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using DtfOrderAutomation.Models;

namespace DtfOrderAutomation.Services;

public class ShopifyService : IDisposable
{
    private readonly HttpClient _http;
    private readonly ConfigService _configService;

    public ShopifyService(ConfigService configService)
    {
        _configService = configService;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    // ── Token ──────────────────────────────────────────────────────────────

    public async Task<string?> GetTokenAsync(AppConfig config)
    {
        var store = config.ShopifyStoreUrl.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(store) ||
            string.IsNullOrEmpty(config.ShopifyClientId) ||
            string.IsNullOrEmpty(config.ShopifyClientSecret))
            return null;

        // Return cached token if still fresh
        if (!string.IsNullOrEmpty(config.ShopifyToken) && !string.IsNullOrEmpty(config.ShopifyTokenExpiry))
        {
            if (DateTime.TryParse(config.ShopifyTokenExpiry, out var expiry) && expiry > DateTime.Now)
                return config.ShopifyToken;
        }

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"]    = "client_credentials",
            ["client_id"]     = config.ShopifyClientId,
            ["client_secret"] = config.ShopifyClientSecret,
        });

        var resp = await _http.PostAsync($"https://{store}/admin/oauth/access_token", content);
        resp.EnsureSuccessStatusCode();

        var data       = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var token      = data.GetProperty("access_token").GetString();
        var expiresIn  = data.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 86399;

        if (!string.IsNullOrEmpty(token))
        {
            config.ShopifyToken        = token;
            config.ShopifyTokenExpiry  = DateTime.Now.AddSeconds(expiresIn - 60).ToString("O");
            _configService.Save(config);
        }

        return token;
    }

    // ── Orders ─────────────────────────────────────────────────────────────

    public async Task<List<JsonElement>?> FetchOrdersAsync(
        AppConfig config,
        DateTime? from = null,
        DateTime? to   = null)
    {
        var store = config.ShopifyStoreUrl.Trim().TrimEnd('/');
        var token = await GetTokenAsync(config);
        if (token is null) return null;

        var query = "?status=open&fulfillment_status=unfulfilled&limit=250";
        if (from.HasValue)
            query += $"&created_at_min={Uri.EscapeDataString(from.Value.ToString("O"))}";
        if (to.HasValue)
            query += $"&created_at_max={Uri.EscapeDataString(to.Value.ToString("O"))}";

        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://{store}/admin/api/2024-01/orders.json{query}");
        req.Headers.Add("X-Shopify-Access-Token", token);

        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var data = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return data.GetProperty("orders").EnumerateArray().ToList();
    }

    // ── Products ───────────────────────────────────────────────────────────

    public async Task<(List<ShopifyProduct> Products, string? Error)> FetchProductsAsync(AppConfig config)
    {
        var store = config.ShopifyStoreUrl.Trim().TrimEnd('/');
        string? token;
        try { token = await GetTokenAsync(config); }
        catch (Exception ex) { return (new(), $"Auth failed: {ex.Message}"); }

        if (token is null) return (new(), "Could not get Shopify token — check Client ID and Secret in Settings");

        var products = new List<ShopifyProduct>();
        string? url  = $"https://{store}/admin/api/2024-01/products.json?limit=250&fields=id,title,images";

        while (url is not null)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("X-Shopify-Access-Token", token);

            HttpResponseMessage resp;
            try { resp = await _http.SendAsync(req); resp.EnsureSuccessStatusCode(); }
            catch (Exception ex) { return (products, $"Error fetching products: {ex.Message}"); }

            JsonElement data;
            try { data = await resp.Content.ReadFromJsonAsync<JsonElement>(); }
            catch (Exception ex) { return (products, $"Error reading response: {ex.Message}"); }

            foreach (var p in data.GetProperty("products").EnumerateArray())
            {
                if (!p.TryGetProperty("title", out var t)) continue;

                string? imageUrl = null;
                if (p.TryGetProperty("images", out var imgs) &&
                    imgs.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var img in imgs.EnumerateArray())
                    {
                        if (img.TryGetProperty("src", out var src))
                        {
                            imageUrl = src.GetString();
                            break;
                        }
                    }
                }

                products.Add(new ShopifyProduct { Title = t.GetString() ?? "", ImageUrl = imageUrl });
            }

            // Follow Link header for pagination
            url = null;
            if (resp.Headers.TryGetValues("Link", out var linkVals))
            {
                foreach (var part in string.Join(",", linkVals).Split(','))
                {
                    if (!part.Contains("rel=\"next\"")) continue;
                    var lt = part.IndexOf('<');
                    var gt = part.IndexOf('>');
                    if (lt >= 0 && gt > lt)
                        url = part[(lt + 1)..gt];
                    break;
                }
            }
        }

        return (products, null);
    }

    public void Dispose() => _http.Dispose();
}
