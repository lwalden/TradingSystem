using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.MarketData.Polygon.Services;

/// <summary>
/// ICalendarService implementation using Polygon.io Benzinga partner API.
/// Earnings data is cached for the session. Dividend/macro deferred to future PRs.
/// </summary>
public class PolygonCalendarService : ICalendarService
{
    private readonly PolygonApiClient _client;
    private readonly PolygonConfig _config;
    private readonly ILogger<PolygonCalendarService> _logger;

    // Session-level cache keyed by "startDate-endDate"
    private readonly ConcurrentDictionary<string, List<EarningsEvent>> _earningsCache = new();

    public PolygonCalendarService(
        PolygonApiClient client,
        IOptions<PolygonConfig> config,
        ILogger<PolygonCalendarService> logger)
    {
        _client = client;
        _config = config.Value;
        _logger = logger;
    }

    public async Task<List<EarningsEvent>> GetEarningsCalendarAsync(
        DateTime startDate, DateTime endDate,
        IEnumerable<string>? symbols = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{startDate:yyyyMMdd}-{endDate:yyyyMMdd}-{(symbols != null ? string.Join(",", symbols) : "all")}";
        if (_earningsCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var response = await _client.GetEarningsAsync(startDate, endDate, symbols, cancellationToken);

        var events = response.Results.Select(r => new EarningsEvent
        {
            Symbol = r.Ticker,
            Date = DateTime.TryParse(r.Date, out var d) ? d : DateTime.MinValue,
            Timing = ParseTiming(r.Time),
            EstimatedEPS = r.EstimatedEps,
            ActualEPS = r.ActualEps,
            Surprise = r.EpsSurprise
        }).Where(e => e.Date != DateTime.MinValue).ToList();

        _earningsCache[cacheKey] = events;
        _logger.LogInformation("Fetched {Count} earnings events from Polygon.io ({Start} to {End})",
            events.Count, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));
        return events;
    }

    public async Task<bool> IsInNoTradeWindowAsync(
        string symbol, DateTime date,
        CancellationToken cancellationToken = default)
    {
        var start = date.AddDays(-_config.EarningsLookbackDays);
        var end = date.AddDays(_config.EarningsLookforwardDays);
        var events = await GetEarningsCalendarAsync(start, end, new[] { symbol }, cancellationToken);
        return events.Any(e => e.IsInNoTradeWindow(date));
    }

    public async Task<List<string>> GetSymbolsInNoTradeWindowAsync(
        IEnumerable<string> symbols, DateTime date,
        CancellationToken cancellationToken = default)
    {
        var symbolList = symbols.ToList();
        var start = date.AddDays(-_config.EarningsLookbackDays);
        var end = date.AddDays(_config.EarningsLookforwardDays);
        var events = await GetEarningsCalendarAsync(start, end, symbolList, cancellationToken);

        return events
            .Where(e => e.IsInNoTradeWindow(date))
            .Select(e => e.Symbol)
            .Distinct()
            .ToList();
    }

    public Task<List<DividendEvent>> GetDividendCalendarAsync(
        DateTime startDate, DateTime endDate,
        IEnumerable<string>? symbols = null,
        CancellationToken cancellationToken = default)
    {
        // Deferred to future PR — return empty list
        return Task.FromResult(new List<DividendEvent>());
    }

    public Task<List<MacroEvent>> GetMacroCalendarAsync(
        DateTime startDate, DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        // Deferred to future PR — return empty list
        return Task.FromResult(new List<MacroEvent>());
    }

    private static EarningsTiming ParseTiming(string? time)
    {
        if (string.IsNullOrEmpty(time)) return EarningsTiming.Unknown;

        // Polygon.io times are in HH:MM:SS UTC format
        if (TimeSpan.TryParse(time, out var ts))
        {
            // Before market open (US market opens 14:30 UTC)
            if (ts.TotalHours < 14)
                return EarningsTiming.BeforeMarketOpen;
            // After market close (US market closes 21:00 UTC)
            if (ts.TotalHours >= 21)
                return EarningsTiming.AfterMarketClose;
        }

        // If time starts with "before" or "after" text patterns
        var lower = time.ToLowerInvariant();
        if (lower.Contains("before") || lower.Contains("bmo"))
            return EarningsTiming.BeforeMarketOpen;
        if (lower.Contains("after") || lower.Contains("amc"))
            return EarningsTiming.AfterMarketClose;

        return EarningsTiming.Unknown;
    }
}
