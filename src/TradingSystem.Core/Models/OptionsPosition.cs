using TradingSystem.Core.Interfaces;

namespace TradingSystem.Core.Models;

/// <summary>
/// A logical options position grouping related legs (e.g., a bull put spread).
/// Individual Position objects from the broker are grouped here for P&L tracking
/// and lifecycle management.
/// </summary>
public class OptionsPosition
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UnderlyingSymbol { get; set; } = string.Empty;
    public StrategyType Strategy { get; set; }
    public SleeveType Sleeve { get; set; } = SleeveType.Tactical;

    // Legs
    public List<OptionsPositionLeg> Legs { get; set; } = new();

    // Entry metrics (captured at open)
    public decimal EntryNetCredit { get; set; } // Positive = credit received, negative = debit paid
    public decimal MaxProfit { get; set; }
    public decimal MaxLoss { get; set; }
    public int Quantity { get; set; } = 1; // Number of spread contracts

    // Current state
    public decimal CurrentValue { get; set; } // Current mid price of the spread

    /// <summary>
    /// Unrealized P&L in dollars. For credit spreads: profit when CurrentValue &lt; EntryNetCredit.
    /// Multiplied by 100 (options multiplier) and quantity.
    /// </summary>
    public decimal UnrealizedPnL => (EntryNetCredit - CurrentValue) * 100 * Quantity;

    /// <summary>
    /// Unrealized P&L as a percentage of max profit.
    /// </summary>
    public decimal UnrealizedPnLPercent => MaxProfit != 0
        ? Math.Round(UnrealizedPnL / (MaxProfit * 100 * Quantity) * 100, 2)
        : 0;

    // Lifecycle
    public OptionsPositionStatus Status { get; set; } = OptionsPositionStatus.Open;
    public string? ExitReason { get; set; }
    public decimal? RealizedPnL { get; set; }

    // DTE tracking
    public DateTime Expiration { get; set; } // Nearest expiration among legs
    public int DTE => Math.Max(0, (Expiration.Date - DateTime.Today).Days);

    // IV context at entry
    public decimal EntryIVRank { get; set; }

    // Linking
    public string? SignalId { get; set; }
    public List<string> OrderIds { get; set; } = new();
    public string? TradeId { get; set; }

    // Timestamps
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    // Partition key for Cosmos DB
    public string PartitionKey => $"{OpenedAt:yyyy-MM}";
}

/// <summary>
/// A single leg within an OptionsPosition.
/// </summary>
public class OptionsPositionLeg
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Strike { get; set; }
    public DateTime Expiration { get; set; }
    public OptionRight Right { get; set; }
    public OrderAction Action { get; set; } // Sell = short, Buy = long
    public int Quantity { get; set; } = 1;
    public decimal EntryPrice { get; set; } // Fill price for this leg
    public decimal CurrentPrice { get; set; } // Current mid price
    public int? ConId { get; set; } // IBKR ConId for this leg
}

public enum OptionsPositionStatus
{
    Open,
    ProfitTargetReached,
    StopTriggered,
    RollPending,
    Closing,
    Closed,
    Expired
}
