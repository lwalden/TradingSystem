namespace TradingSystem.Core.Models;

/// <summary>
/// Represents a position in the portfolio
/// </summary>
public class Position
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Symbol { get; set; } = string.Empty;
    public string SecurityType { get; set; } = "STK"; // STK, OPT, etc.
    public decimal Quantity { get; set; }
    public decimal AverageCost { get; set; }
    public decimal MarketPrice { get; set; }
    public decimal MarketValue => Quantity * MarketPrice;
    public decimal UnrealizedPnL => (MarketPrice - AverageCost) * Quantity;
    public decimal UnrealizedPnLPercent => AverageCost != 0 ? (MarketPrice - AverageCost) / AverageCost * 100 : 0;
    
    // Sleeve assignment
    public SleeveType Sleeve { get; set; } = SleeveType.Income;
    public string Category { get; set; } = string.Empty; // DividendGrowthETF, BDC, REIT, etc.
    
    // Options-specific fields
    public string? UnderlyingSymbol { get; set; }
    public decimal? Strike { get; set; }
    public DateTime? Expiration { get; set; }
    public OptionRight? Right { get; set; } // Call or Put
    
    // Tracking
    public DateTime OpenedAt { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public string? TradeId { get; set; } // Link to originating trade
}

public enum SleeveType
{
    Income,
    Tactical,
    Cash
}

public enum OptionRight
{
    Call,
    Put
}
