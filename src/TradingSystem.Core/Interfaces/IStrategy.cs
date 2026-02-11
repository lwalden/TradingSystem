using TradingSystem.Core.Models;

namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Base interface for all trading strategies
/// </summary>
public interface IStrategy
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    SleeveType Sleeve { get; }
    StrategyType Type { get; }
    bool IsEnabled { get; set; }
    
    /// <summary>
    /// Whether this strategy requires Claude AI analysis
    /// </summary>
    bool RequiresAIAnalysis { get; }
    
    /// <summary>
    /// Evaluate the strategy and generate signals
    /// </summary>
    Task<List<Signal>> EvaluateAsync(StrategyContext context, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validate that a signal still meets entry criteria
    /// Called just before execution
    /// </summary>
    Task<bool> ValidateSignalAsync(Signal signal, StrategyContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Strategy that requires AI analysis
/// </summary>
public interface IAIStrategy : IStrategy
{
    /// <summary>
    /// Build the prompt for Claude analysis
    /// </summary>
    AIAnalysisRequest BuildAnalysisRequest(StrategyContext context);
    
    /// <summary>
    /// Parse Claude's response into signals
    /// </summary>
    List<Signal> ParseAnalysisResponse(string response, StrategyContext context);
}

/// <summary>
/// Context provided to strategies for evaluation
/// </summary>
public class StrategyContext
{
    public Account Account { get; set; } = new();
    public MarketRegime MarketRegime { get; set; } = new();
    public DateTime EvaluationTime { get; set; } = DateTime.UtcNow;
    
    // Pre-loaded data for efficiency
    public Dictionary<string, Quote> Quotes { get; set; } = new();
    public Dictionary<string, TechnicalIndicators> Indicators { get; set; } = new();
    public Dictionary<string, OptionsAnalytics> OptionsAnalytics { get; set; } = new();
    public Dictionary<string, SecurityCalendar> Calendars { get; set; } = new();
    
    // Config
    public Configuration.TradingSystemConfig Config { get; set; } = new();
    
    // Helper methods
    public Quote? GetQuote(string symbol) => Quotes.GetValueOrDefault(symbol);
    public TechnicalIndicators? GetIndicators(string symbol) => Indicators.GetValueOrDefault(symbol);
    public bool IsInNoTradeWindow(string symbol) => 
        Calendars.TryGetValue(symbol, out var cal) && 
        cal.IsInEarningsNoTradeWindow(EvaluationTime);
}

/// <summary>
/// Request for Claude AI analysis
/// </summary>
public class AIAnalysisRequest
{
    public string StrategyId { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
    public Dictionary<string, object> Context { get; set; } = new();
    public int MaxTokens { get; set; } = 2000;
    public string PreferredModel { get; set; } = "claude-sonnet-4-20250514";
}

public enum StrategyType
{
    // Income strategies
    MonthlyReinvest,
    QuarterlyQualityAudit,
    DistributionCapture,
    
    // Tactical equity strategies
    MomentumBreakout,
    PullbackToValue,
    MeanReversion,
    
    // Options strategies
    CashSecuredPut,
    CoveredCall,
    BearCallSpread,
    BullPutSpread,
    IronCondor,
    CalendarSpread,
    DiagonalSpread
}
