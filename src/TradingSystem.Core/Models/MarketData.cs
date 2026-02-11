namespace TradingSystem.Core.Models;

/// <summary>
/// Price bar data (OHLCV)
/// </summary>
public class PriceBar
{
    public string Symbol { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
    public decimal? VWAP { get; set; }
    public BarTimeframe Timeframe { get; set; }
}

public enum BarTimeframe
{
    Minute1,
    Minute5,
    Minute15,
    Minute30,
    Hour1,
    Hour4,
    Daily,
    Weekly,
    Monthly
}

/// <summary>
/// Real-time quote data
/// </summary>
public class Quote
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public int BidSize { get; set; }
    public int AskSize { get; set; }
    public decimal Last { get; set; }
    public long Volume { get; set; }
    public decimal Spread => Ask - Bid;
    public decimal SpreadPercent => Bid != 0 ? Spread / Bid * 100 : 0;
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Options chain data for a single contract
/// </summary>
public class OptionContract
{
    public string Symbol { get; set; } = string.Empty; // Option symbol
    public string UnderlyingSymbol { get; set; } = string.Empty;
    public decimal Strike { get; set; }
    public DateTime Expiration { get; set; }
    public OptionRight Right { get; set; }
    public int DTE => (Expiration - DateTime.Today).Days;
    
    // Pricing
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public decimal Last { get; set; }
    public decimal Mid => (Bid + Ask) / 2;
    public int OpenInterest { get; set; }
    public int Volume { get; set; }
    
    // Greeks
    public decimal? Delta { get; set; }
    public decimal? Gamma { get; set; }
    public decimal? Theta { get; set; }
    public decimal? Vega { get; set; }
    public decimal? Rho { get; set; }
    
    // Implied Volatility
    public decimal? ImpliedVolatility { get; set; }
    
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Aggregated options analytics for an underlying
/// </summary>
public class OptionsAnalytics
{
    public string Symbol { get; set; } = string.Empty;
    public decimal IVRank { get; set; } // 0-100, current IV vs past year
    public decimal IVPercentile { get; set; } // 0-100, % of days with lower IV
    public decimal CurrentIV { get; set; }
    public decimal HistoricalVolatility20 { get; set; }
    public decimal HistoricalVolatility60 { get; set; }
    public decimal PutCallRatio { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Technical indicators for a security
/// </summary>
public class TechnicalIndicators
{
    public string Symbol { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    
    // Moving Averages
    public decimal? SMA20 { get; set; }
    public decimal? SMA50 { get; set; }
    public decimal? SMA200 { get; set; }
    public decimal? EMA20 { get; set; }
    
    // Momentum
    public decimal? RSI14 { get; set; }
    public decimal? RSI2 { get; set; }
    
    // Volatility
    public decimal? ATR14 { get; set; }
    public decimal? BollingerUpper { get; set; }
    public decimal? BollingerLower { get; set; }
    public decimal? BollingerMid { get; set; }
    
    // Volume
    public decimal? VolumeAvg20 { get; set; }
    public decimal? VolumeRatio { get; set; } // Current / 20-day avg
    
    // Trend
    public bool? Above20DMA { get; set; }
    public bool? Above50DMA { get; set; }
    public bool? Above200DMA { get; set; }
    public bool? SMA20Above50 { get; set; }
    public bool? SMA50Above200 { get; set; }
    
    // Calculated fields for strategy use
    public decimal? DistanceFrom20DMA { get; set; }
    public decimal? DistanceFrom50DMA { get; set; }
}

/// <summary>
/// Market regime indicators
/// </summary>
public class MarketRegime
{
    public DateTime Timestamp { get; set; }
    
    // VIX
    public decimal VIX { get; set; }
    public decimal VIXPercentile { get; set; }
    public bool VIXElevated => VIX > 28;
    
    // SPY/Market
    public decimal SPYPrice { get; set; }
    public decimal SPY50DMA { get; set; }
    public decimal SPY200DMA { get; set; }
    public decimal SPYDistanceFrom50DMA { get; set; }
    public bool MarketBelowMA => SPYDistanceFrom50DMA < -3;
    
    // Breadth
    public decimal? AdvanceDeclineRatio { get; set; }
    public decimal? PercentAbove200DMA { get; set; }
    
    // Calculated regime
    public RegimeType Regime { get; set; }
    public decimal RiskMultiplier { get; set; } = 1.0m; // 1.0 = normal, 0.5 = reduced
}

public enum RegimeType
{
    RiskOn,     // Normal conditions, full risk
    Cautious,   // Elevated VIX or market stress, reduce new positions
    RiskOff,    // High stress, defensive mode only
    Recovery    // Coming out of stress, gradually increase
}

/// <summary>
/// Result of IBKR reqSecDefOptParams -- available expirations and strikes for an underlying.
/// </summary>
public class OptionChainDefinition
{
    public string UnderlyingSymbol { get; set; } = string.Empty;
    public int UnderlyingConId { get; set; }
    public string Exchange { get; set; } = string.Empty;
    public string TradingClass { get; set; } = string.Empty;
    public string Multiplier { get; set; } = "100";
    public List<DateTime> Expirations { get; set; } = new();
    public List<decimal> Strikes { get; set; } = new();
}

/// <summary>
/// Historical IV data point for IV rank/percentile calculations.
/// </summary>
public class IVHistoryPoint
{
    public DateTime Date { get; set; }
    public decimal ImpliedVolatility { get; set; }
}

/// <summary>
/// Full IV history for a symbol, used for rank/percentile calculations.
/// </summary>
public class IVHistory
{
    public string Symbol { get; set; } = string.Empty;
    public List<IVHistoryPoint> DataPoints { get; set; } = new();
    public DateTime LastUpdated { get; set; }
    public int TradingDays => DataPoints.Count;
}
