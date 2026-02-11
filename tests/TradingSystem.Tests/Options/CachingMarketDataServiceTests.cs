using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Strategies.Services;
using Xunit;

namespace TradingSystem.Tests.Options;

public class CachingMarketDataServiceTests
{
    private readonly Mock<IBrokerService> _brokerMock;
    private readonly CachingMarketDataService _service;

    public CachingMarketDataServiceTests()
    {
        _brokerMock = new Mock<IBrokerService>();
        _service = new CachingMarketDataService(
            _brokerMock.Object,
            NullLogger<CachingMarketDataService>.Instance);
    }

    // === GetQuoteAsync: first call hits broker, second call within 60s returns cached ===

    [Fact]
    public async Task GetQuoteAsync_FirstCallHitsBroker_SecondCallReturnsCached()
    {
        var quote = new Quote { Symbol = "AAPL", Bid = 150m, Ask = 150.10m, Last = 150.05m };
        _brokerMock.Setup(b => b.GetQuoteAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(quote);

        var result1 = await _service.GetQuoteAsync("AAPL");
        var result2 = await _service.GetQuoteAsync("AAPL");

        Assert.Same(quote, result1);
        Assert.Same(quote, result2);
        _brokerMock.Verify(b => b.GetQuoteAsync("AAPL", It.IsAny<CancellationToken>()), Times.Once);
    }

    // === GetQuoteAsync: after 60s, cache expires and hits broker again ===

    [Fact]
    public async Task GetQuoteAsync_CacheExpires_HitsBrokerAgain()
    {
        var quote1 = new Quote { Symbol = "AAPL", Bid = 150m, Ask = 150.10m, Last = 150.05m };
        var quote2 = new Quote { Symbol = "AAPL", Bid = 151m, Ask = 151.10m, Last = 151.05m };

        var callCount = 0;
        _brokerMock.Setup(b => b.GetQuoteAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++callCount == 1 ? quote1 : quote2);

        // First call hits broker
        var result1 = await _service.GetQuoteAsync("AAPL");
        Assert.Same(quote1, result1);

        // Expire the cache by manipulating the internal dictionary via reflection
        var cacheField = typeof(CachingMarketDataService)
            .GetField("_quoteCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var cache = (System.Collections.Concurrent.ConcurrentDictionary<string, (Quote Quote, DateTime CachedAt)>)cacheField.GetValue(_service)!;
        cache["AAPL"] = (quote1, DateTime.UtcNow.AddSeconds(-61));

        // Second call should hit broker again because cache is expired
        var result2 = await _service.GetQuoteAsync("AAPL");
        Assert.Same(quote2, result2);
        _brokerMock.Verify(b => b.GetQuoteAsync("AAPL", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // === GetOptionsAnalyticsAsync: caches across calls ===

    [Fact]
    public async Task GetOptionsAnalyticsAsync_CachesAcrossCalls()
    {
        var analytics = new OptionsAnalytics
        {
            Symbol = "AAPL",
            IVRank = 45m,
            IVPercentile = 55m,
            CurrentIV = 0.30m,
            HistoricalVolatility20 = 0.25m
        };
        _brokerMock.Setup(b => b.GetOptionsAnalyticsAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(analytics);

        var result1 = await _service.GetOptionsAnalyticsAsync("AAPL");
        var result2 = await _service.GetOptionsAnalyticsAsync("AAPL");

        Assert.Same(analytics, result1);
        Assert.Same(analytics, result2);
        _brokerMock.Verify(b => b.GetOptionsAnalyticsAsync("AAPL", It.IsAny<CancellationToken>()), Times.Once);
    }

    // === GetIndicatorsAsync: calls GetHistoricalBarsAsync, returns TechnicalIndicators ===

    [Fact]
    public async Task GetIndicatorsAsync_CallsHistoricalBars_ReturnsTechnicalIndicators()
    {
        // Create enough bars for SMA20 calculation (need at least 20)
        var bars = CreatePriceBars("AAPL", 30, 150m);

        _brokerMock.Setup(b => b.GetHistoricalBarsAsync(
                "AAPL", BarTimeframe.Daily, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bars);

        var result = await _service.GetIndicatorsAsync("AAPL");

        Assert.Equal("AAPL", result.Symbol);
        Assert.NotNull(result.SMA20);
        _brokerMock.Verify(b => b.GetHistoricalBarsAsync(
            "AAPL", BarTimeframe.Daily, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);

        // Second call should return cached value
        var result2 = await _service.GetIndicatorsAsync("AAPL");
        Assert.Same(result, result2);
        // Still only one call to broker
        _brokerMock.Verify(b => b.GetHistoricalBarsAsync(
            "AAPL", BarTimeframe.Daily, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // === GetQuotesBulkAsync: mix of cached and uncached, only uncached hit broker ===

    [Fact]
    public async Task GetQuotesBulkAsync_MixCachedAndUncached_OnlyFetchesUncached()
    {
        var aaplQuote = new Quote { Symbol = "AAPL", Bid = 150m, Ask = 150.10m, Last = 150.05m };
        var msftQuote = new Quote { Symbol = "MSFT", Bid = 400m, Ask = 400.20m, Last = 400.10m };
        var googQuote = new Quote { Symbol = "GOOG", Bid = 170m, Ask = 170.15m, Last = 170.08m };

        // Pre-cache AAPL
        _brokerMock.Setup(b => b.GetQuoteAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(aaplQuote);
        await _service.GetQuoteAsync("AAPL");

        // Set up bulk fetch for uncached symbols
        _brokerMock.Setup(b => b.GetQuotesAsync(
                It.Is<IEnumerable<string>>(s => s.Contains("MSFT") && s.Contains("GOOG")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Quote> { msftQuote, googQuote });

        var result = await _service.GetQuotesBulkAsync(new[] { "AAPL", "MSFT", "GOOG" });

        Assert.Equal(3, result.Count);
        Assert.Same(aaplQuote, result["AAPL"]);
        Assert.Same(msftQuote, result["MSFT"]);
        Assert.Same(googQuote, result["GOOG"]);

        // AAPL was already cached, so GetQuotesAsync should only have been called for MSFT and GOOG
        _brokerMock.Verify(b => b.GetQuotesAsync(
            It.Is<IEnumerable<string>>(s => s.Count() == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // === GetIndicatorsBulkAsync: calls for each symbol ===

    [Fact]
    public async Task GetIndicatorsBulkAsync_CallsForEachSymbol()
    {
        var aaplBars = CreatePriceBars("AAPL", 30, 150m);
        var msftBars = CreatePriceBars("MSFT", 30, 400m);

        _brokerMock.Setup(b => b.GetHistoricalBarsAsync(
                "AAPL", BarTimeframe.Daily, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aaplBars);
        _brokerMock.Setup(b => b.GetHistoricalBarsAsync(
                "MSFT", BarTimeframe.Daily, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(msftBars);

        var result = await _service.GetIndicatorsBulkAsync(new[] { "AAPL", "MSFT" });

        Assert.Equal(2, result.Count);
        Assert.Equal("AAPL", result["AAPL"].Symbol);
        Assert.Equal("MSFT", result["MSFT"].Symbol);
        Assert.NotNull(result["AAPL"].SMA20);
        Assert.NotNull(result["MSFT"].SMA20);
    }

    // === DetermineRegime: high VIX (>35) -> RiskOff ===

    [Fact]
    public async Task GetMarketRegimeAsync_HighVix_ReturnsRiskOff()
    {
        SetupMarketRegimeMocks(vixLast: 40m, spyAbove50DMA: true, spyAbove200DMA: true);

        var regime = await _service.GetMarketRegimeAsync();

        Assert.Equal(RegimeType.RiskOff, regime.Regime);
        Assert.Equal(40m, regime.VIX);
        Assert.Equal(0.25m, regime.RiskMultiplier);
    }

    // === DetermineRegime: VIX > 25 -> Cautious ===

    [Fact]
    public async Task GetMarketRegimeAsync_ElevatedVix_ReturnsCautious()
    {
        SetupMarketRegimeMocks(vixLast: 30m, spyAbove50DMA: true, spyAbove200DMA: true);

        var regime = await _service.GetMarketRegimeAsync();

        Assert.Equal(RegimeType.Cautious, regime.Regime);
        Assert.Equal(30m, regime.VIX);
        Assert.Equal(0.5m, regime.RiskMultiplier);
    }

    // === DetermineRegime: normal conditions -> RiskOn ===

    [Fact]
    public async Task GetMarketRegimeAsync_NormalConditions_ReturnsRiskOn()
    {
        SetupMarketRegimeMocks(vixLast: 18m, spyAbove50DMA: true, spyAbove200DMA: true);

        var regime = await _service.GetMarketRegimeAsync();

        Assert.Equal(RegimeType.RiskOn, regime.Regime);
        Assert.Equal(18m, regime.VIX);
        Assert.Equal(1.0m, regime.RiskMultiplier);
    }

    // === DetermineRegime: SPY below both MAs with low VIX -> RiskOff ===

    [Fact]
    public async Task GetMarketRegimeAsync_BelowBothMAs_ReturnsRiskOff()
    {
        SetupMarketRegimeMocks(vixLast: 20m, spyAbove50DMA: false, spyAbove200DMA: false);

        var regime = await _service.GetMarketRegimeAsync();

        Assert.Equal(RegimeType.RiskOff, regime.Regime);
    }

    // Helper: create a list of price bars with a gentle uptrend
    private static List<PriceBar> CreatePriceBars(string symbol, int count, decimal basePrice)
    {
        var bars = new List<PriceBar>();
        for (int i = 0; i < count; i++)
        {
            var price = basePrice + (i * 0.5m);
            bars.Add(new PriceBar
            {
                Symbol = symbol,
                Timestamp = DateTime.UtcNow.AddDays(-count + i),
                Open = price - 0.5m,
                High = price + 1m,
                Low = price - 1m,
                Close = price,
                Volume = 1_000_000L
            });
        }
        return bars;
    }

    // Helper: set up mocks for GetMarketRegimeAsync tests
    private void SetupMarketRegimeMocks(decimal vixLast, bool spyAbove50DMA, bool spyAbove200DMA)
    {
        // VIX quote
        var vixQuote = new Quote { Symbol = "VIX", Last = vixLast };
        _brokerMock.Setup(b => b.GetQuoteAsync("VIX", It.IsAny<CancellationToken>()))
            .ReturnsAsync(vixQuote);

        // SPY historical bars -- create enough bars for TechnicalIndicatorCalculator to compute SMAs.
        // We need 250 bars to get SMA200. We'll construct prices so that the last close
        // is either above or below the 50/200 SMA as requested.
        var bars = new List<PriceBar>();
        decimal smaTarget = 450m;
        for (int i = 0; i < 250; i++)
        {
            // All bars at the SMA target level
            bars.Add(new PriceBar
            {
                Symbol = "SPY",
                Timestamp = DateTime.UtcNow.AddDays(-250 + i),
                Open = smaTarget,
                High = smaTarget + 1m,
                Low = smaTarget - 1m,
                Close = smaTarget,
                Volume = 50_000_000L
            });
        }

        // Set the last bar's close to be above or below the moving averages
        if (spyAbove50DMA && spyAbove200DMA)
        {
            // Price well above MA -- set last several bars higher to push price above SMA
            for (int i = 240; i < 250; i++)
            {
                bars[i].Close = smaTarget + 20m;
                bars[i].High = smaTarget + 21m;
                bars[i].Open = smaTarget + 19m;
            }
        }
        else if (!spyAbove50DMA && !spyAbove200DMA)
        {
            // Price well below MA -- set last several bars lower
            for (int i = 200; i < 250; i++)
            {
                bars[i].Close = smaTarget - 30m;
                bars[i].High = smaTarget - 29m;
                bars[i].Low = smaTarget - 31m;
                bars[i].Open = smaTarget - 30m;
            }
        }
        else if (!spyAbove50DMA && spyAbove200DMA)
        {
            // Above 200DMA but below 50DMA: recent decline
            // Keep most bars at target, drop last 20 slightly below recent average
            for (int i = 230; i < 250; i++)
            {
                bars[i].Close = smaTarget - 5m;
                bars[i].High = smaTarget - 4m;
                bars[i].Low = smaTarget - 6m;
                bars[i].Open = smaTarget - 5m;
            }
        }

        _brokerMock.Setup(b => b.GetHistoricalBarsAsync(
                "SPY", BarTimeframe.Daily, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bars);
    }
}
