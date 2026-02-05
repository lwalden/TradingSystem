using TradingSystem.Core.Models;

namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Risk management service
/// </summary>
public interface IRiskManager
{
    /// <summary>
    /// Validate a signal against risk rules before execution
    /// </summary>
    Task<RiskValidationResult> ValidateSignalAsync(Signal signal, Account account, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Calculate position size based on risk parameters
    /// </summary>
    PositionSizeResult CalculatePositionSize(string symbol, decimal entryPrice, decimal stopPrice,
        decimal accountEquity, decimal riskPercent);
    
    /// <summary>
    /// Check if daily/weekly stop has been hit
    /// </summary>
    Task<bool> IsTradingHaltedAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check position limits (single equity, spread, sleeve caps)
    /// </summary>
    Task<PositionLimitResult> CheckPositionLimitsAsync(string symbol, decimal proposedValue,
        SleeveType sleeve, Account account, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check issuer and category caps for income sleeve
    /// </summary>
    Task<CapCheckResult> CheckIncomeCapsAsync(string symbol, decimal proposedValue,
        Account account, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get current risk metrics
    /// </summary>
    Task<RiskMetrics> GetRiskMetricsAsync(CancellationToken cancellationToken = default);
}

public class RiskValidationResult
{
    public bool IsValid { get; set; }
    public List<string> PassedChecks { get; set; } = new();
    public List<string> FailedChecks { get; set; } = new();
    public string? RejectionReason { get; set; }
    public decimal? AdjustedPositionSize { get; set; } // If size was reduced
}

public class PositionSizeResult
{
    public int Shares { get; set; }
    public decimal RiskAmount { get; set; }
    public decimal PositionValue { get; set; }
    public decimal RiskPercent { get; set; }
    public string Rationale { get; set; } = string.Empty;
}

public class PositionLimitResult
{
    public bool WithinLimits { get; set; }
    public decimal CurrentExposure { get; set; }
    public decimal ProposedExposure { get; set; }
    public decimal MaxAllowed { get; set; }
    public string? ViolationType { get; set; }
}

public class CapCheckResult
{
    public bool WithinCaps { get; set; }
    public decimal? IssuerExposure { get; set; }
    public decimal? CategoryExposure { get; set; }
    public string? IssuerCapViolation { get; set; }
    public string? CategoryCapViolation { get; set; }
}

public class RiskMetrics
{
    public decimal DailyPnL { get; set; }
    public decimal DailyPnLPercent { get; set; }
    public decimal WeeklyPnL { get; set; }
    public decimal WeeklyPnLPercent { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal CurrentDrawdown { get; set; }
    public bool DailyStopTriggered { get; set; }
    public bool WeeklyStopTriggered { get; set; }
    public int OpenPositionCount { get; set; }
    public decimal GrossExposure { get; set; }
    public decimal NetExposure { get; set; }
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// Order execution service
/// </summary>
public interface IExecutionService
{
    /// <summary>
    /// Execute a signal by placing orders
    /// </summary>
    Task<ExecutionResult> ExecuteSignalAsync(Signal signal, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Execute multiple signals (batched)
    /// </summary>
    Task<List<ExecutionResult>> ExecuteSignalsAsync(IEnumerable<Signal> signals, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update stops/targets for existing position
    /// </summary>
    Task<bool> UpdateStopAsync(string symbol, decimal newStopPrice, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Close a position
    /// </summary>
    Task<ExecutionResult> ClosePositionAsync(string symbol, string? reason = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get execution status for a signal
    /// </summary>
    Task<ExecutionStatus> GetExecutionStatusAsync(string signalId, CancellationToken cancellationToken = default);
}

public class ExecutionResult
{
    public bool Success { get; set; }
    public string SignalId { get; set; } = string.Empty;
    public List<Order> Orders { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public decimal? Slippage { get; set; }
    public decimal? Commission { get; set; }
}

public class ExecutionStatus
{
    public string SignalId { get; set; } = string.Empty;
    public ExecutionState State { get; set; }
    public decimal FilledQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public decimal? AverageFillPrice { get; set; }
    public List<Order> RelatedOrders { get; set; } = new();
}

public enum ExecutionState
{
    Pending,
    PartiallyFilled,
    Filled,
    Cancelled,
    Rejected,
    Error
}
