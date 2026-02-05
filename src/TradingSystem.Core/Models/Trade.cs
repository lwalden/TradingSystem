namespace TradingSystem.Core.Models;

/// <summary>
/// Represents a completed trade with full audit trail
/// </summary>
public class Trade
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    // Security
    public string Symbol { get; set; } = string.Empty;
    public string SecurityType { get; set; } = "STK";
    
    // Position details
    public OrderAction Action { get; set; }
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal? Commission { get; set; }
    
    // Risk management
    public decimal? InitialStopPrice { get; set; }
    public decimal? TargetPrice { get; set; }
    public decimal RiskAmount { get; set; } // $ at risk
    public decimal? RMultiple { get; set; } // Realized R-multiple
    
    // P&L
    public decimal? RealizedPnL { get; set; }
    public decimal? RealizedPnLPercent { get; set; }
    public decimal? MAE { get; set; } // Maximum Adverse Excursion
    public decimal? MFE { get; set; } // Maximum Favorable Excursion
    
    // Classification
    public SleeveType Sleeve { get; set; }
    public string StrategyId { get; set; } = string.Empty;
    public string StrategyName { get; set; } = string.Empty;
    public string SetupType { get; set; } = string.Empty; // Breakout, Pullback, CSP, etc.
    
    // Audit
    public string Rationale { get; set; } = string.Empty;
    public string? ExitReason { get; set; }
    public List<string> SignalIds { get; set; } = new();
    public List<string> OrderIds { get; set; } = new();
    
    // Rule compliance
    public bool PassedAllQualityGates { get; set; }
    public List<string> ViolatedRules { get; set; } = new();
    
    // Timestamps
    public DateTime EntryTime { get; set; }
    public DateTime? ExitTime { get; set; }
    public int? HoldingPeriodDays => ExitTime.HasValue ? (ExitTime.Value - EntryTime).Days : null;
    
    // Partition key for Cosmos DB
    public string PartitionKey => $"{EntryTime:yyyy-MM}";
}
