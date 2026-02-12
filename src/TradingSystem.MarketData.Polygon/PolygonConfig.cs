namespace TradingSystem.MarketData.Polygon;

public class PolygonConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.polygon.io";
    public int MaxRequestsPerMinute { get; set; } = 5; // Starter plan limit
    public int EarningsLookbackDays { get; set; } = 7;
    public int EarningsLookforwardDays { get; set; } = 30;
}
