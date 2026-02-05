namespace TradingSystem.Core.Models;

/// <summary>
/// Income sleeve allocation targets and current state
/// </summary>
public class IncomeSleeveState
{
    public decimal TotalValue { get; set; }
    public decimal CashBuffer { get; set; }
    public Dictionary<IncomeCategory, CategoryAllocation> Categories { get; set; } = new();
    public List<IssuerExposure> IssuerExposures { get; set; } = new();
    
    // Drift from targets
    public Dictionary<IncomeCategory, decimal> CategoryDrift { get; set; } = new();
    
    public DateTime LastUpdated { get; set; }
    public DateTime LastRebalanced { get; set; }
}

public class CategoryAllocation
{
    public IncomeCategory Category { get; set; }
    public decimal TargetPercent { get; set; }
    public decimal CurrentPercent { get; set; }
    public decimal CurrentValue { get; set; }
    public decimal DriftPercent => CurrentPercent - TargetPercent;
    public bool NeedsTrim => DriftPercent > 30; // Per config
    public List<Position> Positions { get; set; } = new();
}

public class IssuerExposure
{
    public string Issuer { get; set; } = string.Empty;
    public decimal ExposurePercent { get; set; }
    public decimal ExposureValue { get; set; }
    public bool ExceedsCap { get; set; }
    public List<string> Symbols { get; set; } = new();
}

public enum IncomeCategory
{
    DividendGrowthETF,
    CoveredCallETF,
    BDC,
    EquityREIT,
    MortgageREIT,
    PreferredsIGCredit,
    CashBuffer
}

/// <summary>
/// Quality metrics for income securities
/// </summary>
public class IncomeSecurityQuality
{
    public string Symbol { get; set; } = string.Empty;
    public IncomeCategory Category { get; set; }
    
    // Common metrics
    public decimal CurrentYield { get; set; }
    public decimal YieldOnCost { get; set; }
    public decimal DistributionGrowth3Yr { get; set; }
    public int DistributionCutCount { get; set; }
    public DateTime LastDistributionCutDate { get; set; }
    
    // Category-specific metrics
    public decimal? NIICoverage { get; set; } // BDCs
    public decimal? FFOPayoutRatio { get; set; } // REITs
    public decimal? AFFOPayoutRatio { get; set; } // REITs
    public decimal? BookValueTrend { get; set; } // mREITs
    public decimal? Leverage { get; set; }
    public decimal? NAVDrift { get; set; } // Covered call ETFs
    public decimal? ROCPercent { get; set; } // Return of capital %
    public decimal? PortfolioTurnover { get; set; } // Div growth ETFs
    
    // Quality assessment
    public QualityRating Rating { get; set; }
    public List<string> Flags { get; set; } = new(); // Warning flags
    public DateTime LastUpdated { get; set; }
}

public enum QualityRating
{
    Excellent,
    Good,
    Acceptable,
    Watch,     // Yellow flag
    Reduce,    // Need to trim
    Exit       // Need to sell
}

/// <summary>
/// Monthly reinvestment plan
/// </summary>
public class ReinvestmentPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime PlanDate { get; set; }
    public decimal AvailableCash { get; set; }
    public decimal DividendsReceived { get; set; }
    public decimal InterestReceived { get; set; }
    
    public List<ReinvestmentOrder> ProposedBuys { get; set; } = new();
    public decimal TotalProposedAmount => ProposedBuys.Sum(b => b.Amount);
    
    public bool WasExecuted { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public List<string> ExecutedOrderIds { get; set; } = new();
}

public class ReinvestmentOrder
{
    public string Symbol { get; set; } = string.Empty;
    public IncomeCategory Category { get; set; }
    public decimal Amount { get; set; } // Dollar amount
    public int Shares { get; set; }
    public decimal LimitPrice { get; set; }
    public string Rationale { get; set; } = string.Empty; // Why this security
    public decimal DriftReduction { get; set; } // How much drift this reduces
}
