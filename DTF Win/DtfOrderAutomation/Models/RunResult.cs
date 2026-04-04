using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DtfOrderAutomation.Models;

public class RunResult
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    /// <summary>success | partial | error | stopped</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "success";

    [JsonPropertyName("orders_processed")]
    public int OrdersProcessed { get; set; }

    [JsonPropertyName("files_queued")]
    public int FilesQueued { get; set; }

    [JsonPropertyName("skipped")]
    public int Skipped { get; set; }

    [JsonPropertyName("order_details")]
    public List<OrderDetail> OrderDetails { get; set; } = new();

    [JsonPropertyName("skipped_details")]
    public List<SkippedDetail> SkippedDetails { get; set; } = new();

    [JsonPropertyName("hot_folder_files")]
    public List<string> HotFolderFiles { get; set; } = new();
}

public class OrderDetail
{
    [JsonPropertyName("order_id")]
    public string OrderId { get; set; } = "";

    [JsonPropertyName("product")]
    public string Product { get; set; } = "";

    [JsonPropertyName("size")]
    public string Size { get; set; } = "";

    /// <summary>ok | skipped</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("file")]
    public string? File { get; set; }
}

public class SkippedDetail
{
    [JsonPropertyName("order_id")]
    public string OrderId { get; set; } = "";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";
}
