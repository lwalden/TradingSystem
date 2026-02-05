namespace TradingSystem.Core.Models;

/// <summary>
/// Represents a trading order
/// </summary>
public class Order
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? BrokerId { get; set; } // ID assigned by broker
    
    // Security identification
    public string Symbol { get; set; } = string.Empty;
    public string SecurityType { get; set; } = "STK";
    
    // Options fields (if applicable)
    public string? UnderlyingSymbol { get; set; }
    public decimal? Strike { get; set; }
    public DateTime? Expiration { get; set; }
    public OptionRight? Right { get; set; }
    
    // Order details
    public OrderAction Action { get; set; }
    public decimal Quantity { get; set; }
    public OrderType OrderType { get; set; }
    public decimal? LimitPrice { get; set; }
    public decimal? StopPrice { get; set; }
    public TimeInForce TimeInForce { get; set; } = TimeInForce.Day;
    
    // Status tracking
    public OrderStatus Status { get; set; } = OrderStatus.PendingSubmit;
    public decimal FilledQuantity { get; set; }
    public decimal? AverageFillPrice { get; set; }
    public decimal? Commission { get; set; }
    
    // Metadata
    public SleeveType Sleeve { get; set; }
    public string? StrategyId { get; set; }
    public string? SignalId { get; set; }
    public string? Rationale { get; set; }
    public decimal? ExpectedRMultiple { get; set; }
    
    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? FilledAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public enum OrderAction
{
    Buy,
    Sell,
    SellShort,
    BuyToCover
}

public enum OrderType
{
    Market,
    Limit,
    Stop,
    StopLimit,
    TrailingStop
}

public enum TimeInForce
{
    Day,
    GTC, // Good Till Cancelled
    IOC, // Immediate or Cancel
    FOK  // Fill or Kill
}

public enum OrderStatus
{
    PendingSubmit,
    Submitted,
    Accepted,
    PartiallyFilled,
    Filled,
    Cancelled,
    Rejected,
    Error
}
