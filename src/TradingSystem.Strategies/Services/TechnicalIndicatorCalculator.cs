using TradingSystem.Core.Models;

namespace TradingSystem.Strategies.Services;

/// <summary>
/// Pure functions for calculating technical indicators from price bar data.
/// </summary>
public static class TechnicalIndicatorCalculator
{
    public static TechnicalIndicators Calculate(string symbol, List<PriceBar> bars)
    {
        if (bars.Count == 0)
            return new TechnicalIndicators { Symbol = symbol, Timestamp = DateTime.UtcNow };

        var closes = bars.Select(b => b.Close).ToList();
        var currentPrice = closes[^1];

        var result = new TechnicalIndicators
        {
            Symbol = symbol,
            Timestamp = DateTime.UtcNow,
            SMA20 = SMA(closes, 20),
            SMA50 = SMA(closes, 50),
            SMA200 = SMA(closes, 200),
            EMA20 = EMA(closes, 20),
            RSI14 = RSI(closes, 14),
            RSI2 = RSI(closes, 2),
            ATR14 = ATR(bars, 14)
        };

        // Bollinger Bands (20-period, 2 std dev)
        if (result.SMA20.HasValue && closes.Count >= 20)
        {
            var last20 = closes.TakeLast(20).ToList();
            var stdDev = StandardDeviation(last20);
            result.BollingerMid = result.SMA20;
            result.BollingerUpper = result.SMA20 + 2 * stdDev;
            result.BollingerLower = result.SMA20 - 2 * stdDev;
        }

        // Volume
        var volumes = bars.Select(b => (decimal)b.Volume).ToList();
        result.VolumeAvg20 = SMA(volumes, 20);
        if (result.VolumeAvg20 > 0)
            result.VolumeRatio = (decimal)bars[^1].Volume / result.VolumeAvg20.Value;

        // Trend flags
        result.Above20DMA = result.SMA20.HasValue ? currentPrice > result.SMA20 : null;
        result.Above50DMA = result.SMA50.HasValue ? currentPrice > result.SMA50 : null;
        result.Above200DMA = result.SMA200.HasValue ? currentPrice > result.SMA200 : null;
        result.SMA20Above50 = result.SMA20.HasValue && result.SMA50.HasValue
            ? result.SMA20 > result.SMA50 : null;
        result.SMA50Above200 = result.SMA50.HasValue && result.SMA200.HasValue
            ? result.SMA50 > result.SMA200 : null;

        // Distance calculations
        if (result.SMA20.HasValue && result.SMA20.Value != 0)
            result.DistanceFrom20DMA = Math.Round((currentPrice - result.SMA20.Value) / result.SMA20.Value * 100, 2);
        if (result.SMA50.HasValue && result.SMA50.Value != 0)
            result.DistanceFrom50DMA = Math.Round((currentPrice - result.SMA50.Value) / result.SMA50.Value * 100, 2);

        return result;
    }

    /// <summary>
    /// Simple Moving Average over the last N values.
    /// </summary>
    public static decimal? SMA(List<decimal> data, int period)
    {
        if (data.Count < period)
            return null;
        return Math.Round(data.TakeLast(period).Average(), 4);
    }

    /// <summary>
    /// Exponential Moving Average.
    /// </summary>
    public static decimal? EMA(List<decimal> data, int period)
    {
        if (data.Count < period)
            return null;

        var multiplier = 2m / (period + 1);
        // Seed with SMA of first 'period' values
        var ema = data.Take(period).Average();

        for (int i = period; i < data.Count; i++)
        {
            ema = (data[i] - ema) * multiplier + ema;
        }

        return Math.Round(ema, 4);
    }

    /// <summary>
    /// Relative Strength Index (Wilder's smoothing method).
    /// </summary>
    public static decimal? RSI(List<decimal> closes, int period)
    {
        if (closes.Count < period + 1)
            return null;

        var gains = new List<decimal>();
        var losses = new List<decimal>();

        for (int i = 1; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            gains.Add(change > 0 ? change : 0);
            losses.Add(change < 0 ? -change : 0);
        }

        // Initial average using SMA
        var avgGain = gains.Take(period).Average();
        var avgLoss = losses.Take(period).Average();

        // Wilder's smoothing
        for (int i = period; i < gains.Count; i++)
        {
            avgGain = (avgGain * (period - 1) + gains[i]) / period;
            avgLoss = (avgLoss * (period - 1) + losses[i]) / period;
        }

        if (avgLoss == 0)
            return 100m;

        var rs = avgGain / avgLoss;
        return Math.Round(100m - (100m / (1m + rs)), 2);
    }

    /// <summary>
    /// Average True Range.
    /// </summary>
    public static decimal? ATR(List<PriceBar> bars, int period)
    {
        if (bars.Count < period + 1)
            return null;

        var trueRanges = new List<decimal>();
        for (int i = 1; i < bars.Count; i++)
        {
            var high = bars[i].High;
            var low = bars[i].Low;
            var prevClose = bars[i - 1].Close;

            var tr = Math.Max(high - low,
                Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));
            trueRanges.Add(tr);
        }

        if (trueRanges.Count < period)
            return null;

        // Wilder's smoothing for ATR
        var atr = trueRanges.Take(period).Average();
        for (int i = period; i < trueRanges.Count; i++)
        {
            atr = (atr * (period - 1) + trueRanges[i]) / period;
        }

        return Math.Round(atr, 4);
    }

    private static decimal StandardDeviation(List<decimal> data)
    {
        if (data.Count == 0) return 0;
        var avg = data.Average();
        var sumSquares = data.Sum(v => (v - avg) * (v - avg));
        return (decimal)Math.Sqrt((double)(sumSquares / data.Count));
    }
}
