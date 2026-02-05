namespace TradingSystem.Core.Models;

/// <summary>
/// Earnings event
/// </summary>
public class EarningsEvent
{
    public string Symbol { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public EarningsTiming Timing { get; set; }
    public decimal? EstimatedEPS { get; set; }
    public decimal? ActualEPS { get; set; }
    public decimal? Surprise { get; set; }
    
    // Computed no-trade window
    public DateTime NoTradeStart => Date.AddDays(-2);
    public DateTime NoTradeEnd => Date.AddDays(1);
    public bool IsInNoTradeWindow(DateTime checkDate) => 
        checkDate >= NoTradeStart && checkDate <= NoTradeEnd;
}

public enum EarningsTiming
{
    BeforeMarketOpen,
    AfterMarketClose,
    Unknown
}

/// <summary>
/// Dividend event
/// </summary>
public class DividendEvent
{
    public string Symbol { get; set; } = string.Empty;
    public DateTime ExDate { get; set; }
    public DateTime PayDate { get; set; }
    public DateTime RecordDate { get; set; }
    public decimal Amount { get; set; }
    public decimal Yield { get; set; }
    public DividendFrequency Frequency { get; set; }
    
    // For covered call management
    public DateTime RollDeadline => ExDate.AddDays(-3);
}

public enum DividendFrequency
{
    Monthly,
    Quarterly,
    SemiAnnual,
    Annual,
    Irregular
}

/// <summary>
/// Macro economic events
/// </summary>
public class MacroEvent
{
    public string Name { get; set; } = string.Empty; // FOMC, CPI, NFP, etc.
    public DateTime Date { get; set; }
    public string Time { get; set; } = string.Empty;
    public MacroEventImpact Impact { get; set; }
    public string? Forecast { get; set; }
    public string? Previous { get; set; }
    public string? Actual { get; set; }
}

public enum MacroEventImpact
{
    Low,
    Medium,
    High
}

/// <summary>
/// Combined calendar for a symbol
/// </summary>
public class SecurityCalendar
{
    public string Symbol { get; set; } = string.Empty;
    public List<EarningsEvent> Earnings { get; set; } = new();
    public List<DividendEvent> Dividends { get; set; } = new();
    
    public EarningsEvent? NextEarnings => Earnings
        .Where(e => e.Date >= DateTime.Today)
        .OrderBy(e => e.Date)
        .FirstOrDefault();
    
    public DividendEvent? NextExDiv => Dividends
        .Where(d => d.ExDate >= DateTime.Today)
        .OrderBy(d => d.ExDate)
        .FirstOrDefault();
    
    public bool IsInEarningsNoTradeWindow(DateTime date) =>
        Earnings.Any(e => e.IsInNoTradeWindow(date));
}
