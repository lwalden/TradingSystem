using Xunit;
using TradingSystem.Core.Models;
using TradingSystem.Strategies.Services;

namespace TradingSystem.Tests.Options;

public class IVCalculatorTests
{
    // ---------------------------------------------------------------
    // CalculateIVRank
    // ---------------------------------------------------------------

    [Fact]
    public void IVRank_MidRange_Returns50()
    {
        var result = IVCalculator.CalculateIVRank(0.30m, 0.45m, 0.15m);
        Assert.Equal(50.0m, result);
    }

    [Fact]
    public void IVRank_AtHigh_Returns100()
    {
        var result = IVCalculator.CalculateIVRank(0.45m, 0.45m, 0.15m);
        Assert.Equal(100.0m, result);
    }

    [Fact]
    public void IVRank_AtLow_Returns0()
    {
        var result = IVCalculator.CalculateIVRank(0.15m, 0.45m, 0.15m);
        Assert.Equal(0.0m, result);
    }

    [Fact]
    public void IVRank_EqualHighAndLow_Returns50()
    {
        var result = IVCalculator.CalculateIVRank(0.30m, 0.30m, 0.30m);
        Assert.Equal(50m, result);
    }

    [Fact]
    public void IVRank_AboveHigh_ClampedTo100()
    {
        // Current IV exceeds the 52-week high -- should clamp to 100
        var result = IVCalculator.CalculateIVRank(0.50m, 0.45m, 0.15m);
        Assert.Equal(100m, result);
    }

    [Fact]
    public void IVRank_BelowLow_ClampedTo0()
    {
        // Current IV below the 52-week low -- should clamp to 0
        var result = IVCalculator.CalculateIVRank(0.10m, 0.45m, 0.15m);
        Assert.Equal(0m, result);
    }

    [Theory]
    [InlineData(0.30, 0.45, 0.15, 50.0)]
    [InlineData(0.15, 0.45, 0.15, 0.0)]
    [InlineData(0.45, 0.45, 0.15, 100.0)]
    [InlineData(0.225, 0.45, 0.15, 25.0)]
    [InlineData(0.375, 0.45, 0.15, 75.0)]
    public void IVRank_Theory_KnownValues(
        double currentIV, double high52, double low52, double expected)
    {
        var result = IVCalculator.CalculateIVRank(
            (decimal)currentIV, (decimal)high52, (decimal)low52);
        Assert.Equal((decimal)expected, result);
    }

    // ---------------------------------------------------------------
    // CalculateIVPercentile
    // ---------------------------------------------------------------

    [Fact]
    public void IVPercentile_AllBelow_Returns100()
    {
        // Current IV is higher than all history points
        var history = BuildIVHistory(0.10m, 0.11m, 0.12m, 0.13m, 0.14m);
        var result = IVCalculator.CalculateIVPercentile(0.20m, history);
        Assert.Equal(100.0m, result);
    }

    [Fact]
    public void IVPercentile_AllAbove_Returns0()
    {
        // Current IV is lower than all history points
        var history = BuildIVHistory(0.30m, 0.31m, 0.32m, 0.33m, 0.34m);
        var result = IVCalculator.CalculateIVPercentile(0.10m, history);
        Assert.Equal(0.0m, result);
    }

    [Fact]
    public void IVPercentile_EmptyHistory_Returns50()
    {
        var result = IVCalculator.CalculateIVPercentile(0.25m, new List<IVHistoryPoint>());
        Assert.Equal(50m, result);
    }

    [Fact]
    public void IVPercentile_HalfBelow_Returns50()
    {
        // 2 below, 2 at-or-above (at or above does NOT count as "below")
        var history = BuildIVHistory(0.10m, 0.20m, 0.30m, 0.40m);
        var result = IVCalculator.CalculateIVPercentile(0.25m, history);
        // 2 out of 4 are below 0.25 => 50.0
        Assert.Equal(50.0m, result);
    }

    // ---------------------------------------------------------------
    // BuildAnalytics
    // ---------------------------------------------------------------

    [Fact]
    public void BuildAnalytics_FullHistory_PopulatesAllFields()
    {
        // Build 60+ data points so HV20 and HV60 are both populated
        var history = new List<IVHistoryPoint>();
        for (int i = 0; i < 60; i++)
        {
            history.Add(new IVHistoryPoint
            {
                Date = DateTime.UtcNow.AddDays(-60 + i),
                ImpliedVolatility = 0.20m + (i * 0.001m) // slowly increasing
            });
        }

        var result = IVCalculator.BuildAnalytics("AAPL", history);

        Assert.Equal("AAPL", result.Symbol);
        Assert.Equal(history[^1].ImpliedVolatility, result.CurrentIV);
        Assert.True(result.IVRank > 0, "IV Rank should be positive with increasing data");
        Assert.True(result.IVPercentile > 0, "IV Percentile should be positive");
        Assert.True(result.HistoricalVolatility20 > 0, "HV20 should be populated with 60 points");
        Assert.True(result.HistoricalVolatility60 > 0, "HV60 should be populated with 60 points");
    }

    [Fact]
    public void BuildAnalytics_EmptyHistory_ReturnsDefaultAnalytics()
    {
        var result = IVCalculator.BuildAnalytics("SPY", new List<IVHistoryPoint>());

        Assert.Equal("SPY", result.Symbol);
        Assert.Equal(0m, result.CurrentIV);
        Assert.Equal(0m, result.IVRank);
        Assert.Equal(0m, result.IVPercentile);
        Assert.Equal(0m, result.HistoricalVolatility20);
        Assert.Equal(0m, result.HistoricalVolatility60);
    }

    [Fact]
    public void BuildAnalytics_ShortHistory_HasZeroHV()
    {
        // Only 5 data points -- not enough for HV20 or HV60
        var history = BuildIVHistory(0.20m, 0.22m, 0.24m, 0.26m, 0.28m);

        var result = IVCalculator.BuildAnalytics("QQQ", history);

        Assert.Equal("QQQ", result.Symbol);
        Assert.Equal(0.28m, result.CurrentIV); // Last element
        Assert.Equal(0m, result.HistoricalVolatility20); // Insufficient data
        Assert.Equal(0m, result.HistoricalVolatility60); // Insufficient data
        Assert.True(result.IVRank >= 0 && result.IVRank <= 100);
        Assert.True(result.IVPercentile >= 0 && result.IVPercentile <= 100);
    }

    // ---------------------------------------------------------------
    // Helper
    // ---------------------------------------------------------------

    private static List<IVHistoryPoint> BuildIVHistory(params decimal[] ivValues)
    {
        var list = new List<IVHistoryPoint>();
        for (int i = 0; i < ivValues.Length; i++)
        {
            list.Add(new IVHistoryPoint
            {
                Date = DateTime.UtcNow.AddDays(-ivValues.Length + i),
                ImpliedVolatility = ivValues[i]
            });
        }
        return list;
    }
}
