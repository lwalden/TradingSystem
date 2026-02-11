using TradingSystem.Core.Models;

namespace TradingSystem.Strategies.Services;

/// <summary>
/// Pure functions for IV Rank and IV Percentile calculations.
/// Per ADR-006: Calculate from IBKR historical volatility data.
/// </summary>
public static class IVCalculator
{
    /// <summary>
    /// IV Rank = (Current IV - 52wk Low IV) / (52wk High IV - 52wk Low IV) * 100
    /// Range: 0-100. High IV Rank means current IV is near its 52-week high.
    /// </summary>
    public static decimal CalculateIVRank(decimal currentIV, decimal high52WeekIV, decimal low52WeekIV)
    {
        if (high52WeekIV == low52WeekIV)
            return 50m; // No range, assume middle

        var rank = (currentIV - low52WeekIV) / (high52WeekIV - low52WeekIV) * 100m;
        return Math.Clamp(Math.Round(rank, 1), 0, 100);
    }

    /// <summary>
    /// IV Percentile = % of trading days in past year where IV was lower than current IV * 100
    /// Range: 0-100. High IV Percentile means current IV is higher than most historical readings.
    /// </summary>
    public static decimal CalculateIVPercentile(decimal currentIV, IReadOnlyList<IVHistoryPoint> history)
    {
        if (history.Count == 0)
            return 50m; // No data, assume middle

        var daysBelow = history.Count(p => p.ImpliedVolatility < currentIV);
        return Math.Round((decimal)daysBelow / history.Count * 100m, 1);
    }

    /// <summary>
    /// Build OptionsAnalytics from a full IV history series.
    /// The last data point is treated as the current IV.
    /// </summary>
    public static OptionsAnalytics BuildAnalytics(string symbol, IReadOnlyList<IVHistoryPoint> history)
    {
        if (history.Count == 0)
            return new OptionsAnalytics { Symbol = symbol, Timestamp = DateTime.UtcNow };

        var currentIV = history[^1].ImpliedVolatility;
        var high52 = history.Max(p => p.ImpliedVolatility);
        var low52 = history.Min(p => p.ImpliedVolatility);

        return new OptionsAnalytics
        {
            Symbol = symbol,
            CurrentIV = currentIV,
            IVRank = CalculateIVRank(currentIV, high52, low52),
            IVPercentile = CalculateIVPercentile(currentIV, history),
            HistoricalVolatility20 = AverageIV(history, 20),
            HistoricalVolatility60 = AverageIV(history, 60),
            Timestamp = DateTime.UtcNow
        };
    }

    private static decimal AverageIV(IReadOnlyList<IVHistoryPoint> history, int period)
    {
        if (history.Count < period)
            return 0;
        return Math.Round(history.TakeLast(period).Average(p => p.ImpliedVolatility), 4);
    }
}
