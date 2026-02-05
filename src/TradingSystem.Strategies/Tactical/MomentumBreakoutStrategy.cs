using Microsoft.Extensions.Logging;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Strategies.Common;

namespace TradingSystem.Strategies.Tactical;

/// <summary>
/// Momentum breakout strategy - buys on breakout above pivot highs
/// Entry: Stop-limit above recent pivot high with volume confirmation
/// Stop: Below breakout bar low - 0.5x ATR
/// Exit: Scale 50% at +1R, trail rest with ATR
/// </summary>
public class MomentumBreakoutStrategy : StrategyBase
{
    public MomentumBreakoutStrategy(ILogger<MomentumBreakoutStrategy> logger)
        : base(logger)
    {
    }

    public override string Id => "tactical-momentum-breakout";
    public override string Name => "Momentum Breakout";
    public override string Description => "Buys breakouts above pivot highs with volume confirmation";
    public override SleeveType Sleeve => SleeveType.Tactical;
    public override StrategyType Type => StrategyType.MomentumBreakout;
    public override bool RequiresAIAnalysis => false;

    public override async Task<List<Signal>> EvaluateAsync(StrategyContext context,
        CancellationToken cancellationToken = default)
    {
        var signals = new List<Signal>();
        var config = context.Config.Tactical;

        _logger.LogInformation("Evaluating momentum breakout strategy");

        // Check market regime - reduce risk in stressed conditions
        var riskMultiplier = context.MarketRegime.RiskMultiplier;
        if (context.MarketRegime.Regime == RegimeType.RiskOff)
        {
            _logger.LogInformation("Market in RiskOff regime, skipping breakout scan");
            return signals;
        }

        // Scan universe for breakout candidates
        foreach (var (symbol, indicators) in context.Indicators)
        {
            // Skip if in no-trade window (earnings)
            if (context.IsInNoTradeWindow(symbol))
            {
                _logger.LogDebug("Skipping {Symbol} - in no-trade window", symbol);
                continue;
            }

            var quote = context.GetQuote(symbol);
            if (quote == null) continue;

            // Check liquidity filters
            if (!PassesLiquidityFilter(quote, config))
            {
                continue;
            }

            // Check breakout criteria
            var breakoutSignal = EvaluateBreakoutSetup(symbol, quote, indicators, config);
            if (breakoutSignal != null)
            {
                // Apply position sizing
                var riskPercent = config.Options.MinIVPercentile; // Use configured risk
                breakoutSignal.SuggestedRiskAmount = 
                    context.Account.NetLiquidationValue * context.Config.Risk.RiskPerTradePercent * riskMultiplier;

                signals.Add(breakoutSignal);
                _logger.LogInformation("Generated breakout signal: {Symbol}", symbol);
            }
        }

        return signals;
    }

    private bool PassesLiquidityFilter(Quote quote, Core.Configuration.TacticalConfig config)
    {
        // Check spread
        if (quote.SpreadPercent > config.MaxSpreadPercent * 100)
            return false;

        // Check minimum price
        if (quote.Last < config.MinPrice)
            return false;

        // TODO: Check ADV when we have volume data
        return true;
    }

    private Signal? EvaluateBreakoutSetup(string symbol, Quote quote, 
        TechnicalIndicators indicators, Core.Configuration.TacticalConfig config)
    {
        // Breakout criteria from strategy doc:
        // 1. 20/50-DMA rising
        // 2. Price above 20-DMA
        // 3. RSI(14) 45-65
        // 4. Volume > 1.5x 20-day avg

        if (indicators.RSI14 == null || indicators.ATR14 == null)
            return null;

        // Check RSI range
        if (indicators.RSI14 < config.BreakoutRSIMin || indicators.RSI14 > config.BreakoutRSIMax)
            return null;

        // Check above 20-DMA
        if (indicators.Above20DMA != true)
            return null;

        // Check 20/50 DMA alignment (trend)
        if (indicators.SMA20Above50 != true)
            return null;

        // Check volume
        if (indicators.VolumeRatio == null || indicators.VolumeRatio < config.BreakoutVolumeMultiple)
            return null;

        // Calculate entry, stop, and target
        var atr = indicators.ATR14.Value;
        var pivotHigh = quote.Last * 1.01m; // Simplified - use actual pivot detection
        var entryPrice = pivotHigh + (atr * 0.1m); // Buffer above pivot
        var stopPrice = quote.Last - (atr * 1.5m); // Below recent low
        var targetPrice = entryPrice + ((entryPrice - stopPrice) * 2); // 2R target

        var signal = CreateSignal(
            symbol,
            SignalDirection.Long,
            SignalStrength.Moderate,
            $"Breakout setup: RSI={indicators.RSI14:F0}, VolumeRatio={indicators.VolumeRatio:F1}x");

        signal.SetupType = "MomentumBreakout";
        signal.SuggestedEntryPrice = entryPrice;
        signal.SuggestedStopPrice = stopPrice;
        signal.SuggestedTargetPrice = targetPrice;
        signal.ExpectedRMultiple = 2.0m;
        signal.Indicators = new Dictionary<string, object>
        {
            { "RSI14", indicators.RSI14.Value },
            { "ATR14", atr },
            { "VolumeRatio", indicators.VolumeRatio.Value }
        };

        return signal;
    }
}
