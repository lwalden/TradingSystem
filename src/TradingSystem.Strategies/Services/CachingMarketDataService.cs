using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Strategies.Services;

/// <summary>
/// IMarketDataService implementation that wraps IBrokerService with session-level caching.
/// Quotes cached for 60 seconds; indicators and analytics cached for session lifetime.
/// Regime detection is delegated to <see cref="MarketRegimeProvider"/> (composed internally),
/// which uses Claude AI when available and falls back to rule-based detection.
/// </summary>
public class CachingMarketDataService : IMarketDataService
{
    private readonly IBrokerService _broker;
    private readonly ConcurrentDictionary<string, (Quote Quote, DateTime CachedAt)> _quoteCache = new();
    private readonly ConcurrentDictionary<string, TechnicalIndicators> _indicatorCache = new();
    private readonly ConcurrentDictionary<string, OptionsAnalytics> _analyticsCache = new();

    private static readonly TimeSpan QuoteCacheDuration = TimeSpan.FromSeconds(60);

    // Regime detection (cache + stampede guard + Claude-then-rules + clamp) lives here. The provider
    // is composed internally so no DI registration changes: it reaches the cached SPY indicators /
    // VIX quote through this facade's own GetIndicatorsAsync / GetQuoteAsync, reusing the caches.
    private readonly MarketRegimeProvider _regimeProvider;

    public CachingMarketDataService(
        IBrokerService broker,
        ILogger<CachingMarketDataService> logger,
        IClaudeService? claudeService = null,
        IOptions<ClaudeConfig>? claudeOptions = null)
    {
        _broker = broker;
        _regimeProvider = new MarketRegimeProvider(
            GetIndicatorsAsync,
            GetQuoteAsync,
            logger,
            claudeService,
            claudeOptions);
    }

    public async Task<Quote> GetQuoteAsync(string symbol, CancellationToken ct = default)
    {
        if (_quoteCache.TryGetValue(symbol, out var cached) &&
            DateTime.UtcNow - cached.CachedAt < QuoteCacheDuration)
            return cached.Quote;

        var quote = await _broker.GetQuoteAsync(symbol, ct);
        _quoteCache[symbol] = (quote, DateTime.UtcNow);
        return quote;
    }

    public async Task<List<PriceBar>> GetDailyBarsAsync(string symbol, int days, CancellationToken ct = default)
    {
        var endDate = DateTime.Now;
        var startDate = endDate.AddDays(-days * 1.5); // Account for weekends/holidays
        return await _broker.GetHistoricalBarsAsync(symbol, BarTimeframe.Daily, startDate, endDate, ct);
    }

    public async Task<TechnicalIndicators> GetIndicatorsAsync(string symbol, CancellationToken ct = default)
    {
        if (_indicatorCache.TryGetValue(symbol, out var cached))
            return cached;

        var bars = await GetDailyBarsAsync(symbol, 250, ct);
        var indicators = TechnicalIndicatorCalculator.Calculate(symbol, bars);
        _indicatorCache[symbol] = indicators;
        return indicators;
    }

    public Task<MarketRegime> GetMarketRegimeAsync(CancellationToken cancellationToken = default)
        => _regimeProvider.GetMarketRegimeAsync(cancellationToken);

    public async Task<OptionsAnalytics> GetOptionsAnalyticsAsync(string symbol, CancellationToken ct = default)
    {
        if (_analyticsCache.TryGetValue(symbol, out var cached))
            return cached;

        var analytics = await _broker.GetOptionsAnalyticsAsync(symbol, ct);
        _analyticsCache[symbol] = analytics;
        return analytics;
    }

    public async Task<Dictionary<string, Quote>> GetQuotesBulkAsync(
        IEnumerable<string> symbols, CancellationToken ct = default)
    {
        var result = new Dictionary<string, Quote>();
        var toFetch = new List<string>();

        foreach (var symbol in symbols)
        {
            if (_quoteCache.TryGetValue(symbol, out var cached) &&
                DateTime.UtcNow - cached.CachedAt < QuoteCacheDuration)
                result[symbol] = cached.Quote;
            else
                toFetch.Add(symbol);
        }

        if (toFetch.Count > 0)
        {
            var quotes = await _broker.GetQuotesAsync(toFetch, ct);
            foreach (var q in quotes)
            {
                _quoteCache[q.Symbol] = (q, DateTime.UtcNow);
                result[q.Symbol] = q;
            }
        }
        return result;
    }

    public async Task<Dictionary<string, TechnicalIndicators>> GetIndicatorsBulkAsync(
        IEnumerable<string> symbols, CancellationToken ct = default)
    {
        // Order-preserving concurrent fan-out. GetIndicatorsAsync caches per symbol via a
        // ConcurrentDictionary, so issuing the per-symbol calls in parallel is concurrent-safe.
        // De-dup defensively so a repeated symbol cannot produce a duplicate dictionary key.
        var symbolList = symbols.Distinct().ToList();
        var tasks = symbolList.Select(s => GetIndicatorsAsync(s, ct)).ToList();
        var results = await Task.WhenAll(tasks);
        return symbolList.Zip(results, (s, r) => (s, r)).ToDictionary(x => x.s, x => x.r);
    }
}
