using TradingSystem.Core.Models;

namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Abstraction for brokerage API operations
/// Implementations: IBKR, Alpaca, etc.
/// </summary>
public interface IBrokerService
{
    // Connection
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    bool IsConnected { get; }
    
    // Account data
    Task<Account> GetAccountAsync(CancellationToken cancellationToken = default);
    Task<List<Position>> GetPositionsAsync(CancellationToken cancellationToken = default);
    Task<decimal> GetBuyingPowerAsync(CancellationToken cancellationToken = default);
    
    // Market data
    Task<Quote> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default);
    Task<List<Quote>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default);
    Task<List<PriceBar>> GetHistoricalBarsAsync(string symbol, BarTimeframe timeframe, 
        DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);
    
    // Options data
    Task<List<OptionContract>> GetOptionChainAsync(string underlying, DateTime? expiration = null,
        CancellationToken cancellationToken = default);
    Task<OptionsAnalytics> GetOptionsAnalyticsAsync(string symbol, CancellationToken cancellationToken = default);
    
    // Orders
    Task<Order> PlaceOrderAsync(Order order, CancellationToken cancellationToken = default);
    Task<Order> PlaceComboOrderAsync(Order order, CancellationToken cancellationToken = default);
    Task<Order> GetOrderStatusAsync(string orderId, CancellationToken cancellationToken = default);
    Task<List<Order>> GetOpenOrdersAsync(CancellationToken cancellationToken = default);
    Task<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default);
    Task<Order> ModifyOrderAsync(string orderId, decimal? newLimitPrice = null, 
        decimal? newQuantity = null, CancellationToken cancellationToken = default);
    
    // Calendar data
    Task<SecurityCalendar> GetSecurityCalendarAsync(string symbol, CancellationToken cancellationToken = default);
    Task<List<DividendEvent>> GetUpcomingDividendsAsync(IEnumerable<string> symbols, 
        int daysAhead = 30, CancellationToken cancellationToken = default);
    Task<List<EarningsEvent>> GetUpcomingEarningsAsync(IEnumerable<string> symbols,
        int daysAhead = 14, CancellationToken cancellationToken = default);
}

/// <summary>
/// Market data service with caching and calculated indicators
/// </summary>
public interface IMarketDataService
{
    Task<Quote> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default);
    Task<List<PriceBar>> GetDailyBarsAsync(string symbol, int days, CancellationToken cancellationToken = default);
    Task<TechnicalIndicators> GetIndicatorsAsync(string symbol, CancellationToken cancellationToken = default);
    Task<MarketRegime> GetMarketRegimeAsync(CancellationToken cancellationToken = default);
    Task<OptionsAnalytics> GetOptionsAnalyticsAsync(string symbol, CancellationToken cancellationToken = default);
    
    // Bulk operations for efficiency
    Task<Dictionary<string, Quote>> GetQuotesBulkAsync(IEnumerable<string> symbols, 
        CancellationToken cancellationToken = default);
    Task<Dictionary<string, TechnicalIndicators>> GetIndicatorsBulkAsync(IEnumerable<string> symbols,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Calendar and event service
/// </summary>
public interface ICalendarService
{
    Task<List<EarningsEvent>> GetEarningsCalendarAsync(DateTime startDate, DateTime endDate,
        IEnumerable<string>? symbols = null, CancellationToken cancellationToken = default);
    Task<List<DividendEvent>> GetDividendCalendarAsync(DateTime startDate, DateTime endDate,
        IEnumerable<string>? symbols = null, CancellationToken cancellationToken = default);
    Task<List<MacroEvent>> GetMacroCalendarAsync(DateTime startDate, DateTime endDate,
        CancellationToken cancellationToken = default);
    
    Task<bool> IsInNoTradeWindowAsync(string symbol, DateTime date, CancellationToken cancellationToken = default);
    Task<List<string>> GetSymbolsInNoTradeWindowAsync(IEnumerable<string> symbols, DateTime date,
        CancellationToken cancellationToken = default);
}
