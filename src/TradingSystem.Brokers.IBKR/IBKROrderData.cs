namespace TradingSystem.Brokers.IBKR;

/// <summary>
/// Internal DTO for order callback data from TWS API.
/// </summary>
internal class OrderData
{
    public int OrderId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string SecType { get; set; } = "STK";
    public string Action { get; set; } = string.Empty;
    public decimal TotalQuantity { get; set; }
    public string OrderType { get; set; } = string.Empty;
    public double LmtPrice { get; set; }
    public double AuxPrice { get; set; }
    public string Tif { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Filled { get; set; }
    public decimal Remaining { get; set; }
    public double AvgFillPrice { get; set; }
    public string WhyHeld { get; set; } = string.Empty;
}
