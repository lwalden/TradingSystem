using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Strategies.Services;

/// <summary>
/// IMarketDataService implementation that wraps IBrokerService with session-level caching.
/// Quotes cached for 60 seconds; indicators and analytics cached for session lifetime.
/// Regime detection uses Claude AI when available, falls back to rule-based.
/// </summary>
public class CachingMarketDataService : IMarketDataService
{
    private readonly IBrokerService _broker;
    private readonly ILogger<CachingMarketDataService> _logger;
    private readonly IClaudeService? _claudeService;

    private readonly ConcurrentDictionary<string, (Quote Quote, DateTime CachedAt)> _quoteCache = new();
    private readonly ConcurrentDictionary<string, TechnicalIndicators> _indicatorCache = new();
    private readonly ConcurrentDictionary<string, OptionsAnalytics> _analyticsCache = new();

    private static readonly TimeSpan QuoteCacheDuration = TimeSpan.FromSeconds(60);

    // Safety bounds (NOT config) for the Claude-supplied RiskMultiplier. The clamp applies
    // ONLY to the AI regime-detection path; the rule-based regime multiplier table
    // (RiskOn=1.0 .. RiskOff=0.25 in GetMarketRegimeAsync) is intentionally left unchanged.
    private const decimal MinRiskMultiplier = 0.5m;
    private const decimal MaxRiskMultiplier = 1.0m;

    public CachingMarketDataService(
        IBrokerService broker,
        ILogger<CachingMarketDataService> logger,
        IClaudeService? claudeService = null)
    {
        _broker = broker;
        _logger = logger;
        _claudeService = claudeService;
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
        var spyIndicators = await GetIndicatorsAsync("SPY", ct);
        var vixQuote = await GetQuoteAsync("VIX", ct);

        // Try Claude-enhanced regime detection if available
        if (_claudeService != null)
        {
            try
            {
                var claudeRegime = await DetectRegimeWithClaudeAsync(vixQuote, spyIndicators, ct);
                if (claudeRegime != null)
                {
                    _logger.LogInformation(
                        "Claude regime: {Regime}, RiskMultiplier: {Mult}, Rationale: {Rationale}",
                        claudeRegime.Regime, claudeRegime.RiskMultiplier,
                        claudeRegime.Rationale ?? "none");
                    return claudeRegime;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Claude regime detection failed, falling back to rules");
            }
        }

        // Rule-based fallback (always available)
        var regime = DetermineRegime(vixQuote.Last, spyIndicators);
        var riskMultiplier = regime switch
        {
            RegimeType.RiskOn => 1.0m,
            RegimeType.Recovery => 0.75m,
            RegimeType.Cautious => 0.5m,
            RegimeType.RiskOff => 0.25m,
            _ => 1.0m
        };

        _logger.LogInformation("Rule-based regime: {Regime}, RiskMultiplier: {Mult}", regime, riskMultiplier);

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

    private async Task<MarketRegime?> DetectRegimeWithClaudeAsync(
        Quote vixQuote, TechnicalIndicators spyIndicators, CancellationToken ct)
    {
        var spyVs50dma = spyIndicators.DistanceFrom50DMA ?? 0m;
        var spyVs200dma = spyIndicators.SMA200.HasValue && spyIndicators.SMA200.Value > 0
            ? ((spyIndicators.SMA20 ?? 0m) - spyIndicators.SMA200.Value) / spyIndicators.SMA200.Value * 100m
            : 0m;

        var userPrompt = $@"Analyze current market conditions:
- VIX: {vixQuote.Last:F1}
- SPY vs 50-DMA: {spyVs50dma:F1}%
- SPY vs 200-DMA: {spyVs200dma:F1}%
- Advance/Decline Ratio: N/A
- % Stocks above 200-DMA: N/A

Classify the regime and recommend a risk multiplier (0.5 = half risk, 1.0 = full risk).";

        var request = new AIAnalysisRequest
        {
            StrategyId = "regime-detection",
            SystemPrompt = @"You are a market analyst assistant for an automated trading system. Your role is to assess current market conditions and provide a regime classification.

Respond ONLY with valid JSON in this exact format:
{
    ""regime"": ""RiskOn|Cautious|RiskOff|Recovery"",
    ""riskMultiplier"": 0.5-1.0,
    ""rationale"": ""Brief explanation"",
    ""keyFactors"": [""factor1"", ""factor2""]
}",
            UserPrompt = userPrompt,
            MaxTokens = 500
        };

        var response = await _claudeService!.AnalyzeAsync<ClaudeRegimeResponse>(request, ct);

        var regime = response.Regime?.ToLowerInvariant() switch
        {
            "riskon" => RegimeType.RiskOn,
            "cautious" => RegimeType.Cautious,
            "riskoff" => RegimeType.RiskOff,
            "recovery" => RegimeType.Recovery,
            _ => (RegimeType?)null
        };

        if (regime == null)
        {
            _logger.LogWarning("Claude returned unrecognized regime: {Regime}", response.Regime);
            return null;
        }

        // Bound the AI-supplied multiplier to [Min, Max] before it can influence position
        // sizing. Absent values default to 1.0 (in range, no warn); out-of-range values
        // clamp to the nearest bound and emit a Warning (fail-closed: never riskier).
        // NOTE: this clamp applies ONLY to the AI path; the rule-based multiplier table is untouched.
        var rawMultiplier = (decimal)(response.RiskMultiplier ?? 1.0);
        var clampedMultiplier = Math.Clamp(rawMultiplier, MinRiskMultiplier, MaxRiskMultiplier);
        if (clampedMultiplier != rawMultiplier)
        {
            _logger.LogWarning(
                "Claude RiskMultiplier {Raw} out of bounds [{Min},{Max}]; clamped to {Clamped}",
                rawMultiplier, MinRiskMultiplier, MaxRiskMultiplier, clampedMultiplier);
        }

        return new MarketRegime
        {
            VIX = vixQuote.Last,
            SPYPrice = spyIndicators.SMA20 ?? 0,
            SPY50DMA = spyIndicators.SMA50 ?? 0,
            SPY200DMA = spyIndicators.SMA200 ?? 0,
            SPYDistanceFrom50DMA = spyVs50dma,
            Regime = regime.Value,
            RiskMultiplier = clampedMultiplier,
            Rationale = response.Rationale,
            Source = "claude",
            Timestamp = DateTime.UtcNow
        };
    }

    // Internal (not private) so unit tests can construct and mock AnalyzeAsync&lt;ClaudeRegimeResponse&gt;.
    // The Strategies csproj exposes internals to TradingSystem.Tests via InternalsVisibleTo.
    internal class ClaudeRegimeResponse
    {
        public string? Regime { get; set; }
        public double? RiskMultiplier { get; set; }
        public string? Rationale { get; set; }
        public List<string>? KeyFactors { get; set; }
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
