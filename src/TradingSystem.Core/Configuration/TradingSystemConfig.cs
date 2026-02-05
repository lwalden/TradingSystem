namespace TradingSystem.Core.Configuration;

/// <summary>
/// Master trading system configuration
/// </summary>
public class TradingSystemConfig
{
    public TradingMode Mode { get; set; } = TradingMode.Sandbox;
    
    // Capital allocation
    public decimal IncomeTargetPercent { get; set; } = 0.70m;
    public decimal TacticalTargetPercent { get; set; } = 0.30m;
    
    // Risk settings
    public RiskConfig Risk { get; set; } = new();
    
    // Income sleeve config
    public IncomeConfig Income { get; set; } = new();
    
    // Tactical sleeve config
    public TacticalConfig Tactical { get; set; } = new();
    
    // Calendar/no-trade config
    public CalendarConfig Calendar { get; set; } = new();
    
    // Execution config
    public ExecutionConfig Execution { get; set; } = new();
}

public enum TradingMode
{
    Sandbox,    // Paper trading only
    Live,       // Real money
    Backtest    // Historical simulation
}

public class RiskConfig
{
    public decimal RiskPerTradePercent { get; set; } = 0.004m; // 0.4%
    public decimal DailyStopPercent { get; set; } = 0.02m; // 2%
    public decimal WeeklyStopPercent { get; set; } = 0.04m; // 4%
    public decimal MaxSingleEquityPercent { get; set; } = 0.05m; // 5%
    public decimal MaxSingleSpreadPercent { get; set; } = 0.02m; // 2%
    public decimal MaxGrossLeverage { get; set; } = 1.2m;
    public decimal MaxDrawdownHalt { get; set; } = 0.10m; // 10% max DD before system pause
}

public class IncomeConfig
{
    // Allocation targets by category
    public Dictionary<string, decimal> AllocationTargets { get; set; } = new()
    {
        { "DividendGrowthETF", 0.25m },
        { "CoveredCallETF", 0.20m },
        { "BDC", 0.20m },
        { "EquityREIT", 0.10m },
        { "MortgageREIT", 0.10m },
        { "PreferredsIGCredit", 0.10m },
        { "CashBuffer", 0.05m }
    };
    
    // Caps
    public decimal MaxIssuerPercent { get; set; } = 0.10m; // 10%
    public decimal MaxCategoryPercent { get; set; } = 0.40m; // 40%
    public decimal RebalanceDriftTrimThreshold { get; set; } = 0.30m; // 30% over cap
    public decimal MonthlyCashBufferMonths { get; set; } = 1.0m;
    
    // Quality gates
    public IncomeQualityGates QualityGates { get; set; } = new();
}

public class IncomeQualityGates
{
    // Dividend Growth ETFs
    public decimal MinDividendCAGR5Yr { get; set; } = 0m;
    public decimal MaxPortfolioTurnover { get; set; } = 0.60m;
    
    // BDCs
    public decimal MinNIICoverage { get; set; } = 1.0m;
    
    // REITs
    public decimal MaxFFOPayoutRatio { get; set; } = 0.95m;
    
    // Covered Call ETFs
    public decimal MaxNAVDriftAnnualized { get; set; } = 0.05m;
    
    // Exit triggers
    public decimal DistributionCutThreshold { get; set; } = 0.20m; // 20% cut = reduce
    public int SequentialCutsToExit { get; set; } = 2;
}

public class TacticalConfig
{
    // Equity candidate filters
    public decimal MinADV { get; set; } = 10_000_000m; // $10M
    public decimal MinPrice { get; set; } = 5m;
    public decimal MaxSpreadPercent { get; set; } = 0.005m; // 0.5%
    
    // Breakout scan params
    public int BreakoutRSIMin { get; set; } = 45;
    public int BreakoutRSIMax { get; set; } = 65;
    public decimal BreakoutVolumeMultiple { get; set; } = 1.5m;
    
    // Pullback scan params
    public int PullbackRSI2Threshold { get; set; } = 10;
    public decimal PullbackATRMultiple { get; set; } = 0.5m;
    
    // Options params
    public OptionsConfig Options { get; set; } = new();
    
    // Position limits
    public decimal MaxTacticalGrossPercent { get; set; } = 0.40m; // 40% of account
}

public class OptionsConfig
{
    public int MinOpenInterest { get; set; } = 250;
    public decimal MaxSpreadDollars { get; set; } = 0.10m;
    public decimal MaxSpreadPercent { get; set; } = 0.025m; // 2.5%
    
    // Short premium requirements
    public decimal MinIVPercentile { get; set; } = 50m;
    public decimal MinIVRank { get; set; } = 30m;
    
    // CSP params
    public decimal CSPDeltaTarget { get; set; } = 0.20m;
    public int CSPMinDTE { get; set; } = 21;
    public int CSPMaxDTE { get; set; } = 45;
    public decimal CSPMinCreditPercent { get; set; } = 0.01m; // 1% per 30 days
    
    // Bear call spread params
    public decimal ShortCallDeltaMin { get; set; } = 0.15m;
    public decimal ShortCallDeltaMax { get; set; } = 0.25m;
    public int SpreadWidth { get; set; } = 4; // strikes
    
    // Profit taking
    public decimal ProfitTakeMin { get; set; } = 0.50m; // 50%
    public decimal ProfitTakeMax { get; set; } = 0.75m; // 75%
    public decimal StopMultipleCredit { get; set; } = 2.0m; // 2x credit = stop
}

public class CalendarConfig
{
    public int EarningsNoTradeDaysBefore { get; set; } = 2;
    public int EarningsNoTradeDaysAfter { get; set; } = 1;
    public int ExDivRollDaysBefore { get; set; } = 3;
    public bool ReduceRiskOnFOMC { get; set; } = true;
    public bool ReduceRiskOnCPI { get; set; } = true;
    public bool ReduceRiskOnNFP { get; set; } = true;
}

public class ExecutionConfig
{
    public decimal MaxSlippagePercent { get; set; } = 0.003m; // 0.3%
    public decimal MaxImpactPercent { get; set; } = 0.002m; // 0.2%
    public int OrderLadderCount { get; set; } = 3; // Split large orders
    public decimal MinLotDollars { get; set; } = 100m; // Minimum order size
    public TimeSpan OrderTimeout { get; set; } = TimeSpan.FromMinutes(30);
}
