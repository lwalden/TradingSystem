using TradingSystem.Core.Models;

namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Repository for trade data
/// </summary>
public interface ITradeRepository
{
    Task<Trade> SaveAsync(Trade trade, CancellationToken cancellationToken = default);
    Task<Trade?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<List<Trade>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, 
        CancellationToken cancellationToken = default);
    Task<List<Trade>> GetByStrategyAsync(string strategyId, DateTime? since = null,
        CancellationToken cancellationToken = default);
    Task<List<Trade>> GetOpenTradesAsync(CancellationToken cancellationToken = default);
    Task<TradeStatistics> GetStatisticsAsync(DateTime? since = null, string? strategyId = null,
        CancellationToken cancellationToken = default);
}

public class TradeStatistics
{
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public decimal WinRate => TotalTrades > 0 ? (decimal)WinningTrades / TotalTrades * 100 : 0;
    public decimal TotalPnL { get; set; }
    public decimal AveragePnL { get; set; }
    public decimal AverageWin { get; set; }
    public decimal AverageLoss { get; set; }
    public decimal ProfitFactor => AverageLoss != 0 ? Math.Abs(AverageWin * WinningTrades / (AverageLoss * LosingTrades)) : 0;
    public decimal AverageRMultiple { get; set; }
    public decimal ExpectancyPerTrade { get; set; }
    public decimal LargestWin { get; set; }
    public decimal LargestLoss { get; set; }
    public int AverageHoldingDays { get; set; }
    public int MaxConsecutiveWins { get; set; }
    public int MaxConsecutiveLosses { get; set; }
}

/// <summary>
/// Repository for signals
/// </summary>
public interface ISignalRepository
{
    Task<Signal> SaveAsync(Signal signal, CancellationToken cancellationToken = default);
    Task<Signal?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<List<Signal>> GetActiveSignalsAsync(CancellationToken cancellationToken = default);
    Task<List<Signal>> GetByStrategyAsync(string strategyId, DateTime? since = null,
        CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(string id, SignalStatus status, string? notes = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository for orders
/// </summary>
public interface IOrderRepository
{
    Task<Order> SaveAsync(Order order, CancellationToken cancellationToken = default);
    Task<Order?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<Order?> GetByBrokerIdAsync(string brokerId, CancellationToken cancellationToken = default);
    Task<List<Order>> GetOpenOrdersAsync(CancellationToken cancellationToken = default);
    Task<List<Order>> GetByDateRangeAsync(DateTime startDate, DateTime endDate,
        CancellationToken cancellationToken = default);
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository for daily snapshots and reporting
/// </summary>
public interface ISnapshotRepository
{
    Task SaveDailySnapshotAsync(DailySnapshot snapshot, CancellationToken cancellationToken = default);
    Task<DailySnapshot?> GetSnapshotAsync(DateTime date, CancellationToken cancellationToken = default);
    Task<List<DailySnapshot>> GetSnapshotsAsync(DateTime startDate, DateTime endDate,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Daily portfolio snapshot for tracking
/// </summary>
public class DailySnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime Date { get; set; }
    public string PartitionKey => $"{Date:yyyy-MM}";
    
    // Account values
    public decimal NetLiquidationValue { get; set; }
    public decimal CashValue { get; set; }
    public decimal IncomeSleeveValue { get; set; }
    public decimal TacticalSleeveValue { get; set; }
    
    // Daily P&L
    public decimal DailyPnL { get; set; }
    public decimal DailyPnLPercent { get; set; }
    public decimal RealizedPnL { get; set; }
    public decimal UnrealizedPnL { get; set; }
    
    // Cumulative metrics
    public decimal YTDReturn { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal HighWaterMark { get; set; }
    
    // Activity
    public int TradesExecuted { get; set; }
    public int SignalsGenerated { get; set; }
    public decimal DividendsReceived { get; set; }
    public decimal CommissionsPaid { get; set; }
    
    // Position summary
    public int OpenPositions { get; set; }
    public decimal GrossExposure { get; set; }
    
    // Market context
    public decimal SPYClose { get; set; }
    public decimal VIXClose { get; set; }
    public RegimeType MarketRegime { get; set; }
}

/// <summary>
/// Configuration repository (for persisting config changes)
/// </summary>
public interface IConfigRepository
{
    Task<Configuration.TradingSystemConfig> GetConfigAsync(CancellationToken cancellationToken = default);
    Task SaveConfigAsync(Configuration.TradingSystemConfig config, CancellationToken cancellationToken = default);
    Task<T?> GetSettingAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetSettingAsync<T>(string key, T value, CancellationToken cancellationToken = default);
}
