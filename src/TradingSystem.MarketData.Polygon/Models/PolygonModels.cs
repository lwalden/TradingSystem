using System.Text.Json.Serialization;

namespace TradingSystem.MarketData.Polygon.Models;

/// <summary>
/// Root response from the Polygon.io Benzinga earnings endpoint.
/// GET /benzinga/v1/earnings
/// </summary>
internal class PolygonEarningsResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("next_url")]
    public string? NextUrl { get; set; }

    [JsonPropertyName("results")]
    public List<PolygonEarningsResult> Results { get; set; } = new();
}

internal class PolygonEarningsResult
{
    [JsonPropertyName("benzinga_id")]
    public string BenzingaId { get; set; } = string.Empty;

    [JsonPropertyName("ticker")]
    public string Ticker { get; set; } = string.Empty;

    [JsonPropertyName("company_name")]
    public string CompanyName { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonPropertyName("date_status")]
    public string? DateStatus { get; set; }

    [JsonPropertyName("fiscal_year")]
    public int? FiscalYear { get; set; }

    [JsonPropertyName("fiscal_period")]
    public string? FiscalPeriod { get; set; }

    [JsonPropertyName("actual_eps")]
    public decimal? ActualEps { get; set; }

    [JsonPropertyName("estimated_eps")]
    public decimal? EstimatedEps { get; set; }

    [JsonPropertyName("eps_surprise")]
    public decimal? EpsSurprise { get; set; }

    [JsonPropertyName("eps_surprise_percent")]
    public decimal? EpsSurprisePercent { get; set; }

    [JsonPropertyName("actual_revenue")]
    public decimal? ActualRevenue { get; set; }

    [JsonPropertyName("estimated_revenue")]
    public decimal? EstimatedRevenue { get; set; }

    [JsonPropertyName("revenue_surprise")]
    public decimal? RevenueSurprise { get; set; }

    [JsonPropertyName("revenue_surprise_percent")]
    public decimal? RevenueSurprisePercent { get; set; }

    [JsonPropertyName("last_updated")]
    public string? LastUpdated { get; set; }
}
