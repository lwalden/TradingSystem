using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Strategies.Services;

/// <summary>
/// IMarketDataService implementation that wraps IBrokerService with session-level caching.
/// Quotes cached for 60 seconds; indicators and analytics cached for session lifetime.
/// </summary>
public class CachingMarketDataService : IMarketDataService
{
    private readonly IBrokerService _broker;
    private readonly ILogger<CachingMarketDataService> _logger;

    private readonly ConcurrentDictionary<string, (Quote Quote, DateTime CachedAt)> _quoteCache = new();
    private readonly ConcurrentDictionary<string, TechnicalIndicators> _indicatorCache = new();
    private readonly ConcurrentDictionary<string, OptionsAnalytics> _analyticsCache = new();

    private static readonly TimeSpan QuoteCacheDuration = TimeSpan.FromSeconds(60);

    public CachingMarketDataService(
        IBrokerService broker,
        ILogger<CachingMarketDataService> logger)
    {
        _broker = broker;
        _logger = logger;
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

    public async Task<MarketRegime> GetMarketRegimeAsync(CancellationToken ct = default)
    {
        // Basic algorithmic regime detection (placeholder until Claude AI in Week 9)
        var spyIndicators = await GetIndicatorsAsync("SPY", ct);
        var vixQuote = await GetQuoteAsync("VIX", ct);

        var regime = DetermineRegime(vixQuote.Last, spyIndicators);
        var riskMultiplier = regime switch
        {
            RegimeType.RiskOn => 1.0m,
            RegimeType.Recovery => 0.75m,
            RegimeType.Cautious => 0.5m,
            RegimeType.RiskOff => 0.25m,
            _ => 1.0m
        };

        return new MarketRegime
        {
            VIX = vixQuote.Last,
            SPYPrice = spyIndicators.SMA20 ?? 0,
            SPY50DMA = spyIndicators.SMA50 ?? 0,
            SPY200DMA = spyIndicators.SMA200 ?? 0,
            SPYDistanceFrom50DMA = spyIndicators.DistanceFrom50DMA ?? 0,
            Regime = regime,
            RiskMultiplier = riskMultiplier,
            Timestamp = DateTime.UtcNow
        };
    }

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
        var result = new Dictionary<string, TechnicalIndicators>();
        foreach (var symbol in symbols)
        {
            result[symbol] = await GetIndicatorsAsync(symbol, ct);
        }
        return result;
    }

    private static RegimeType DetermineRegime(decimal vix, TechnicalIndicators spyIndicators)
    {
        if (vix > 35) return RegimeType.RiskOff;
        if (vix > 25) return RegimeType.Cautious;
        if (spyIndicators.Above50DMA == false && spyIndicators.Above200DMA == false)
            return RegimeType.RiskOff;
        if (spyIndicators.Above50DMA == false)
            return RegimeType.Cautious;
        return RegimeType.RiskOn;
    }
}
