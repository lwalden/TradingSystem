using Xunit;
using TradingSystem.Core.Models;
using TradingSystem.Strategies.Services;

namespace TradingSystem.Tests.Options;

public class TechnicalIndicatorCalculatorTests
{
    // ---------------------------------------------------------------
    // SMA
    // ---------------------------------------------------------------

    [Fact]
    public void SMA_SimpleAverage_ReturnsCorrectValue()
    {
        var data = new List<decimal> { 10m, 20m, 30m, 40m, 50m };
        var result = TechnicalIndicatorCalculator.SMA(data, 5);
        Assert.Equal(30m, result);
    }

    [Fact]
    public void SMA_InsufficientData_ReturnsNull()
    {
        var data = new List<decimal> { 10m, 20m };
        var result = TechnicalIndicatorCalculator.SMA(data, 5);
        Assert.Null(result);
    }

    [Fact]
    public void SMA_UsesLastNValues()
    {
        // SMA(3) of [1, 2, 3, 4, 5] should use last 3: avg(3, 4, 5) = 4
        var data = new List<decimal> { 1m, 2m, 3m, 4m, 5m };
        var result = TechnicalIndicatorCalculator.SMA(data, 3);
        Assert.Equal(4m, result);
    }

    // ---------------------------------------------------------------
    // EMA
    // ---------------------------------------------------------------

    [Fact]
    public void EMA_KnownValues_ReturnsExpected()
    {
        // 5-period EMA with constant data should equal that constant
        var data = new List<decimal> { 10m, 10m, 10m, 10m, 10m };
        var result = TechnicalIndicatorCalculator.EMA(data, 5);
        Assert.NotNull(result);
        Assert.Equal(10m, result!.Value);
    }

    [Fact]
    public void EMA_InsufficientData_ReturnsNull()
    {
        var data = new List<decimal> { 10m, 20m };
        var result = TechnicalIndicatorCalculator.EMA(data, 5);
        Assert.Null(result);
    }

    [Fact]
    public void EMA_IncreasingData_HigherThanSMA()
    {
        // EMA weights recent data more, so with an uptrend it should be >= SMA
        var data = new List<decimal>();
        for (int i = 1; i <= 20; i++)
            data.Add(i * 10m);

        var ema = TechnicalIndicatorCalculator.EMA(data, 5);
        var sma = TechnicalIndicatorCalculator.SMA(data, 5);
        Assert.NotNull(ema);
        Assert.NotNull(sma);
        Assert.True(ema!.Value >= sma!.Value,
            $"EMA ({ema}) should be >= SMA ({sma}) in an uptrend");
    }

    // ---------------------------------------------------------------
    // RSI
    // ---------------------------------------------------------------

    [Fact]
    public void RSI_AllGains_Returns100()
    {
        // Strictly increasing series -- every change is a gain
        var closes = new List<decimal>();
        for (int i = 0; i <= 15; i++) // Need period+1 (14+1=15) data points
            closes.Add(100m + i);

        var result = TechnicalIndicatorCalculator.RSI(closes, 14);
        Assert.NotNull(result);
        Assert.Equal(100m, result!.Value);
    }

    [Fact]
    public void RSI_AllLosses_Returns0()
    {
        // Strictly decreasing series -- every change is a loss
        var closes = new List<decimal>();
        for (int i = 0; i <= 15; i++)
            closes.Add(200m - i);

        var result = TechnicalIndicatorCalculator.RSI(closes, 14);
        Assert.NotNull(result);
        Assert.Equal(0m, result!.Value);
    }

    [Fact]
    public void RSI_MixedData_ReturnsBetween0And100()
    {
        // Alternating up/down
        var closes = new List<decimal>();
        for (int i = 0; i <= 30; i++)
            closes.Add(100m + (i % 2 == 0 ? 5m : -3m));

        var result = TechnicalIndicatorCalculator.RSI(closes, 14);
        Assert.NotNull(result);
        Assert.True(result!.Value > 0m && result.Value < 100m,
            $"RSI should be between 0 and 100, got {result.Value}");
    }

    [Fact]
    public void RSI_InsufficientData_ReturnsNull()
    {
        // Need at least period + 1 data points
        var closes = new List<decimal> { 100m, 101m, 102m };
        var result = TechnicalIndicatorCalculator.RSI(closes, 14);
        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // ATR
    // ---------------------------------------------------------------

    [Fact]
    public void ATR_KnownValues_ReturnsExpected()
    {
        // Build bars where true range is consistently 5 (High - Low = 5, no gaps)
        var bars = new List<PriceBar>();
        for (int i = 0; i <= 15; i++) // need period+1 (14+1=15) bars
        {
            var close = 100m + i;
            bars.Add(CreateBar("TEST", close - 2.5m, close + 2.5m, close - 2.5m, close));
        }

        var result = TechnicalIndicatorCalculator.ATR(bars, 14);
        Assert.NotNull(result);
        // True range should be ~5 for each bar (High - Low = 5, close-to-close gaps are small)
        Assert.True(result!.Value > 4m && result.Value < 6m,
            $"ATR should be approximately 5.0, got {result.Value}");
    }

    [Fact]
    public void ATR_InsufficientData_ReturnsNull()
    {
        var bars = new List<PriceBar>
        {
            CreateBar("TEST", 100m, 105m, 95m, 102m),
            CreateBar("TEST", 102m, 107m, 97m, 104m)
        };

        var result = TechnicalIndicatorCalculator.ATR(bars, 14);
        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // Calculate (integration)
    // ---------------------------------------------------------------

    [Fact]
    public void Calculate_EmptyBars_ReturnsDefaultIndicators()
    {
        var result = TechnicalIndicatorCalculator.Calculate("AAPL", new List<PriceBar>());

        Assert.Equal("AAPL", result.Symbol);
        Assert.Null(result.SMA20);
        Assert.Null(result.SMA50);
        Assert.Null(result.SMA200);
        Assert.Null(result.EMA20);
        Assert.Null(result.RSI14);
        Assert.Null(result.ATR14);
        Assert.Null(result.Above20DMA);
        Assert.Null(result.Above50DMA);
        Assert.Null(result.Above200DMA);
    }

    [Fact]
    public void Calculate_FullAscendingBars_TrendFlagsTrue()
    {
        // 210 ascending bars -- enough for SMA200 and all other indicators
        var bars = BuildAscendingBars("SPY", count: 210, startPrice: 400m, increment: 0.50m);

        var result = TechnicalIndicatorCalculator.Calculate("SPY", bars);

        Assert.Equal("SPY", result.Symbol);
        Assert.NotNull(result.SMA20);
        Assert.NotNull(result.SMA50);
        Assert.NotNull(result.SMA200);
        Assert.NotNull(result.EMA20);
        Assert.NotNull(result.RSI14);
        Assert.NotNull(result.ATR14);

        // In an ascending series, current price should be above all moving averages
        Assert.True(result.Above20DMA, "Price should be above 20 DMA in ascending series");
        Assert.True(result.Above50DMA, "Price should be above 50 DMA in ascending series");
        Assert.True(result.Above200DMA, "Price should be above 200 DMA in ascending series");
        Assert.True(result.SMA20Above50, "SMA20 should be above SMA50 in ascending series");
        Assert.True(result.SMA50Above200, "SMA50 should be above SMA200 in ascending series");

        // RSI should be high in a strong uptrend
        Assert.True(result.RSI14 > 50m, $"RSI should be above 50 in uptrend, got {result.RSI14}");

        // Distance from 20 DMA should be positive
        Assert.NotNull(result.DistanceFrom20DMA);
        Assert.True(result.DistanceFrom20DMA > 0, "Distance from 20 DMA should be positive in uptrend");
    }

    [Fact]
    public void Calculate_FullDescendingBars_TrendFlagsFalse()
    {
        // 210 descending bars
        var bars = BuildDescendingBars("QQQ", count: 210, startPrice: 500m, decrement: 0.50m);

        var result = TechnicalIndicatorCalculator.Calculate("QQQ", bars);

        Assert.NotNull(result.SMA20);
        Assert.NotNull(result.SMA50);
        Assert.NotNull(result.SMA200);

        // In a descending series, current price should be below all moving averages
        Assert.False(result.Above20DMA, "Price should be below 20 DMA in descending series");
        Assert.False(result.Above50DMA, "Price should be below 50 DMA in descending series");
        Assert.False(result.Above200DMA, "Price should be below 200 DMA in descending series");
        Assert.False(result.SMA20Above50, "SMA20 should be below SMA50 in descending series");
        Assert.False(result.SMA50Above200, "SMA50 should be below SMA200 in descending series");
    }

    [Fact]
    public void Calculate_ShortBars_OnlyShortPeriodIndicators()
    {
        // Only 5 bars -- not enough for any standard indicator
        var bars = BuildAscendingBars("MSFT", count: 5, startPrice: 300m, increment: 1m);

        var result = TechnicalIndicatorCalculator.Calculate("MSFT", bars);

        Assert.Equal("MSFT", result.Symbol);
        Assert.Null(result.SMA20);
        Assert.Null(result.SMA50);
        Assert.Null(result.SMA200);
        Assert.Null(result.ATR14);
        Assert.Null(result.RSI14);
        // RSI2 needs only 3 data points (period + 1), so with 5 bars it should be populated
        Assert.NotNull(result.RSI2);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static PriceBar CreateBar(string symbol, decimal open, decimal high, decimal low, decimal close)
    {
        return new PriceBar
        {
            Symbol = symbol,
            Timestamp = DateTime.UtcNow,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = 1_000_000,
            Timeframe = BarTimeframe.Daily
        };
    }

    private static List<PriceBar> BuildAscendingBars(string symbol, int count, decimal startPrice, decimal increment)
    {
        var bars = new List<PriceBar>();
        for (int i = 0; i < count; i++)
        {
            var price = startPrice + (i * increment);
            bars.Add(new PriceBar
            {
                Symbol = symbol,
                Timestamp = DateTime.UtcNow.AddDays(-count + i),
                Open = price - 0.5m,
                High = price + 1m,
                Low = price - 1m,
                Close = price,
                Volume = 1_000_000 + (i * 1000),
                Timeframe = BarTimeframe.Daily
            });
        }
        return bars;
    }

    private static List<PriceBar> BuildDescendingBars(string symbol, int count, decimal startPrice, decimal decrement)
    {
        var bars = new List<PriceBar>();
        for (int i = 0; i < count; i++)
        {
            var price = startPrice - (i * decrement);
            bars.Add(new PriceBar
            {
                Symbol = symbol,
                Timestamp = DateTime.UtcNow.AddDays(-count + i),
                Open = price + 0.5m,
                High = price + 1m,
                Low = price - 1m,
                Close = price,
                Volume = 1_000_000 + (i * 1000),
                Timeframe = BarTimeframe.Daily
            });
        }
        return bars;
    }
}
