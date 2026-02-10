namespace TradingSystem.Brokers.IBKR;

/// <summary>
/// Internal DTOs for transferring raw IBKR callback data before mapping to domain models.
/// </summary>
internal class AccountSummaryResult
{
    public string AccountId { get; set; } = "";
    public decimal NetLiquidation { get; set; }
    public decimal TotalCashValue { get; set; }
    public decimal BuyingPower { get; set; }
    public decimal GrossPositionValue { get; set; }
    public decimal MaintMarginReq { get; set; }
    public decimal InitMarginReq { get; set; }
    public decimal AvailableFunds { get; set; }
}

internal class PositionData
{
    public string Account { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string SecType { get; set; } = "";
    public decimal Quantity { get; set; }
    public double AverageCost { get; set; }
    public decimal? Strike { get; set; }
    public string? LastTradeDateOrContractMonth { get; set; }
    public string? Right { get; set; }
    public string? UnderlyingSymbol { get; set; }
}

internal class QuoteData
{
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public decimal Last { get; set; }
    public decimal BidSize { get; set; }
    public decimal AskSize { get; set; }
    public long Volume { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
}
