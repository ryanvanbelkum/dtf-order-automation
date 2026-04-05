using System.Text.Json.Serialization;

namespace DtfOrderAutomation.Models;

public class AppConfig
{
    [JsonPropertyName("shopify_store_url")]
    public string ShopifyStoreUrl { get; set; } = "";

    [JsonPropertyName("shopify_client_id")]
    public string ShopifyClientId { get; set; } = "";

    [JsonPropertyName("shopify_client_secret")]
    public string ShopifyClientSecret { get; set; } = "";

    [JsonPropertyName("shopify_token")]
    public string ShopifyToken { get; set; } = "";

    [JsonPropertyName("shopify_token_expiry")]
    public string ShopifyTokenExpiry { get; set; } = "";

    [JsonPropertyName("designs_folder")]
    public string DesignsFolder { get; set; } = "";

    [JsonPropertyName("hot_folder")]
    public string HotFolder { get; set; } = "";

    [JsonPropertyName("last_run")]
    public string? LastRun { get; set; }
}
