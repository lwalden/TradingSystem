using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Strategies.Options;

/// <summary>
/// Orchestrates options candidate screening:
/// 1. Filter symbols in no-trade window (via ICalendarService)
/// 2. Fetch IV analytics, filter by IV rank/percentile thresholds
/// 3. Fetch option chains for passing symbols
/// 4. Apply liquidity filters (OI, bid-ask spread)
/// 5. Screen per strategy based on market regime
/// </summary>
public class OptionsScreeningService
{
    private readonly IMarketDataService _marketData;
    private readonly IBrokerService _broker;
    private readonly ICalendarService _calendar;
    private readonly OptionsConfig _config;
    private readonly ILogger<OptionsScreeningService> _logger;

    public OptionsScreeningService(
        IMarketDataService marketData,
        IBrokerService broker,
        ICalendarService calendar,
        IOptions<TacticalConfig> tacticalConfig,
        ILogger<OptionsScreeningService> logger)
    {
        _marketData = marketData;
        _broker = broker;
        _calendar = calendar;
        _config = tacticalConfig.Value.Options;
        _logger = logger;
    }

    public async Task<OptionsScreenResult> ScanAsync(
        IEnumerable<string> symbols,
        CancellationToken ct = default)
    {
        var result = new OptionsScreenResult();
        var symbolList = symbols.ToList();
        result.SymbolsScanned = symbolList.Count;

        // Step 1: Get market regime
        var regime = await _marketData.GetMarketRegimeAsync(ct);
        result.MarketRegime = regime.Regime;

        if (regime.Regime == RegimeType.RiskOff)
        {
            _logger.LogInformation("Market regime is RiskOff — no new options trades");
            return result;
        }

        // Step 2: Filter out symbols in earnings no-trade windows
        var noTradeSymbols = await _calendar.GetSymbolsInNoTradeWindowAsync(symbolList, DateTime.Today, ct);
        result.SymbolsFilteredByNoTrade = noTradeSymbols.Count;
        var eligibleSymbols = symbolList.Where(s => !noTradeSymbols.Contains(s)).ToList();

        if (eligibleSymbols.Count == 0)
        {
            _logger.LogInformation("All symbols are in no-trade windows");
            return result;
        }

        // Step 3: Fetch IV analytics and filter by thresholds
        var symbolsPassingIV = new List<(string Symbol, OptionsAnalytics Analytics)>();
        foreach (var symbol in eligibleSymbols)
        {
            try
            {
                var analytics = await _marketData.GetOptionsAnalyticsAsync(symbol, ct);
                if (analytics.IVRank >= _config.MinIVRank && analytics.IVPercentile >= _config.MinIVPercentile)
                {
                    symbolsPassingIV.Add((symbol, analytics));
                }
                else
                {
                    result.SymbolsFilteredByIV++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch IV analytics for {Symbol}", symbol);
                result.Errors.Add($"{symbol}: IV analytics failed - {ex.Message}");
            }
        }

        if (symbolsPassingIV.Count == 0)
        {
            _logger.LogInformation("No symbols passed IV rank/percentile filters");
            return result;
        }

        // Step 4: Fetch option chains and screen per strategy
        foreach (var (symbol, analytics) in symbolsPassingIV)
        {
            try
            {
                var chain = await _broker.GetOptionChainAsync(symbol, cancellationToken: ct);
                if (chain.Count == 0) continue;

                // Filter by liquidity
                var liquidContracts = chain
                    .Where(c => PassesLiquidityFilter(c))
                    .ToList();

                if (liquidContracts.Count == 0)
                {
                    result.SymbolsFilteredByLiquidity++;
                    continue;
                }

                var quote = await _marketData.GetQuoteAsync(symbol, ct);
                var underlyingPrice = quote.Last > 0 ? quote.Last : (quote.Bid + quote.Ask) / 2;

                // Screen by strategy based on regime
                ScreenCSPs(liquidContracts, symbol, underlyingPrice, analytics, regime.Regime, result);
                ScreenBullPutSpreads(liquidContracts, symbol, underlyingPrice, analytics, regime.Regime, result);
                ScreenBearCallSpreads(liquidContracts, symbol, underlyingPrice, analytics, regime.Regime, result);
                ScreenIronCondors(liquidContracts, symbol, underlyingPrice, analytics, regime.Regime, result);
                ScreenCalendarSpreads(liquidContracts, symbol, underlyingPrice, analytics, result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to screen {Symbol}", symbol);
                result.Errors.Add($"{symbol}: screening failed - {ex.Message}");
            }
        }

        // Sort all candidates by score descending
        result.CSPCandidates = result.CSPCandidates.OrderByDescending(c => c.Score).ToList();
        result.BullPutSpreadCandidates = result.BullPutSpreadCandidates.OrderByDescending(c => c.Score).ToList();
        result.BearCallSpreadCandidates = result.BearCallSpreadCandidates.OrderByDescending(c => c.Score).ToList();
        result.IronCondorCandidates = result.IronCondorCandidates.OrderByDescending(c => c.Score).ToList();
        result.CalendarSpreadCandidates = result.CalendarSpreadCandidates.OrderByDescending(c => c.Score).ToList();

        _logger.LogInformation(
            "Options scan complete: {Total} candidates ({CSP} CSPs, {BPS} bull puts, {BCS} bear calls, {IC} iron condors, {CS} calendars)",
            result.TotalCandidates, result.CSPCandidates.Count, result.BullPutSpreadCandidates.Count,
            result.BearCallSpreadCandidates.Count, result.IronCondorCandidates.Count, result.CalendarSpreadCandidates.Count);

        return result;
    }

    internal bool PassesLiquidityFilter(OptionContract contract)
    {
        if (contract.OpenInterest < _config.MinOpenInterest) return false;

        var spread = contract.Ask - contract.Bid;
        if (spread > _config.MaxSpreadDollars) return false;

        if (contract.Mid > 0 && spread / contract.Mid > _config.MaxSpreadPercent) return false;

        return true;
    }

    internal void ScreenCSPs(
        List<OptionContract> chain, string symbol, decimal underlyingPrice,
        OptionsAnalytics analytics, RegimeType regime, OptionsScreenResult result)
    {
        // CSPs allowed in RiskOn and Cautious (bullish/neutral stance)
        if (regime != RegimeType.RiskOn && regime != RegimeType.Recovery)
            return;

        var puts = chain
            .Where(c => c.Right == OptionRight.Put &&
                        c.DTE >= _config.CSPMinDTE &&
                        c.DTE <= _config.CSPMaxDTE &&
                        c.Delta.HasValue &&
                        Math.Abs(c.Delta.Value) <= _config.CSPDeltaTarget + 0.05m &&
                        Math.Abs(c.Delta.Value) >= _config.CSPDeltaTarget - 0.10m)
            .ToList();

        foreach (var put in puts)
        {
            var credit = put.Mid;
            if (credit <= 0) continue;

            // Cash secured: risk is strike price * 100 minus credit
            var maxLoss = put.Strike * 100 - credit * 100;
            var maxProfit = credit * 100;

            // Min credit per 30 days check
            var creditPer30Days = credit / put.DTE * 30;
            if (creditPer30Days / put.Strike < _config.CSPMinCreditPercent) continue;

            // POP estimate from delta: POP ≈ 1 - |delta|
            var pop = put.Delta.HasValue ? (1 - Math.Abs(put.Delta.Value)) * 100 : 0;

            var candidate = new OptionCandidate
            {
                UnderlyingSymbol = symbol,
                Strategy = StrategyType.CashSecuredPut,
                Legs = new List<OptionLeg>
                {
                    new()
                    {
                        Symbol = put.Symbol,
                        UnderlyingSymbol = symbol,
                        Strike = put.Strike,
                        Expiration = put.Expiration,
                        Right = OptionRight.Put,
                        Action = OrderAction.Sell,
                        Delta = put.Delta,
                        Theta = put.Theta,
                        ImpliedVolatility = put.ImpliedVolatility,
                        Bid = put.Bid,
                        Ask = put.Ask
                    }
                },
                MaxProfit = maxProfit,
                MaxLoss = maxLoss,
                NetCredit = credit,
                ProbabilityOfProfit = pop,
                IVRank = analytics.IVRank,
                IVPercentile = analytics.IVPercentile,
                UnderlyingPrice = underlyingPrice
            };

            OptionCandidateScorer.Score(candidate);
            result.CSPCandidates.Add(candidate);
        }
    }

    internal void ScreenBullPutSpreads(
        List<OptionContract> chain, string symbol, decimal underlyingPrice,
        OptionsAnalytics analytics, RegimeType regime, OptionsScreenResult result)
    {
        // Bull put spreads allowed in RiskOn and Recovery
        if (regime != RegimeType.RiskOn && regime != RegimeType.Recovery)
            return;

        var puts = chain
            .Where(c => c.Right == OptionRight.Put &&
                        c.DTE >= _config.CSPMinDTE &&
                        c.DTE <= _config.CSPMaxDTE &&
                        c.Delta.HasValue)
            .OrderBy(c => c.Expiration)
            .ThenByDescending(c => c.Strike)
            .ToList();

        // Group by expiration
        var expirationGroups = puts.GroupBy(p => p.Expiration);

        foreach (var group in expirationGroups)
        {
            var sortedPuts = group.OrderByDescending(p => p.Strike).ToList();

            for (int i = 0; i < sortedPuts.Count; i++)
            {
                var shortPut = sortedPuts[i];
                if (!shortPut.Delta.HasValue) continue;
                var absShortDelta = Math.Abs(shortPut.Delta.Value);
                if (absShortDelta < _config.ShortCallDeltaMin || absShortDelta > _config.CSPDeltaTarget + 0.05m)
                    continue;

                // Find long put a few strikes below
                for (int j = i + 1; j < sortedPuts.Count && j <= i + _config.SpreadWidth; j++)
                {
                    var longPut = sortedPuts[j];
                    if (longPut.Strike >= shortPut.Strike) continue;

                    var credit = shortPut.Mid - longPut.Mid;
                    if (credit <= 0) continue;

                    var width = shortPut.Strike - longPut.Strike;
                    var maxLoss = (width - credit) * 100;
                    var maxProfit = credit * 100;
                    var pop = shortPut.Delta.HasValue ? (1 - Math.Abs(shortPut.Delta.Value)) * 100 : 0;

                    var candidate = new OptionCandidate
                    {
                        UnderlyingSymbol = symbol,
                        Strategy = StrategyType.BullPutSpread,
                        Legs = new List<OptionLeg>
                        {
                            CreateLeg(shortPut, symbol, OrderAction.Sell),
                            CreateLeg(longPut, symbol, OrderAction.Buy)
                        },
                        MaxProfit = maxProfit,
                        MaxLoss = maxLoss,
                        NetCredit = credit,
                        ProbabilityOfProfit = pop,
                        IVRank = analytics.IVRank,
                        IVPercentile = analytics.IVPercentile,
                        UnderlyingPrice = underlyingPrice
                    };

                    OptionCandidateScorer.Score(candidate);
                    result.BullPutSpreadCandidates.Add(candidate);
                    break; // One long put per short put
                }
            }
        }
    }

    internal void ScreenBearCallSpreads(
        List<OptionContract> chain, string symbol, decimal underlyingPrice,
        OptionsAnalytics analytics, RegimeType regime, OptionsScreenResult result)
    {
        // Bear call spreads allowed in Cautious and RiskOn
        if (regime != RegimeType.RiskOn && regime != RegimeType.Cautious)
            return;

        var calls = chain
            .Where(c => c.Right == OptionRight.Call &&
                        c.DTE >= _config.CSPMinDTE &&
                        c.DTE <= _config.CSPMaxDTE &&
                        c.Delta.HasValue)
            .OrderBy(c => c.Expiration)
            .ThenBy(c => c.Strike)
            .ToList();

        var expirationGroups = calls.GroupBy(c => c.Expiration);

        foreach (var group in expirationGroups)
        {
            var sortedCalls = group.OrderBy(c => c.Strike).ToList();

            for (int i = 0; i < sortedCalls.Count; i++)
            {
                var shortCall = sortedCalls[i];
                if (!shortCall.Delta.HasValue) continue;
                var absShortDelta = Math.Abs(shortCall.Delta.Value);
                if (absShortDelta < _config.ShortCallDeltaMin || absShortDelta > _config.ShortCallDeltaMax)
                    continue;

                // Find long call a few strikes above
                for (int j = i + 1; j < sortedCalls.Count && j <= i + _config.SpreadWidth; j++)
                {
                    var longCall = sortedCalls[j];
                    if (longCall.Strike <= shortCall.Strike) continue;

                    var credit = shortCall.Mid - longCall.Mid;
                    if (credit <= 0) continue;

                    var width = longCall.Strike - shortCall.Strike;
                    var maxLoss = (width - credit) * 100;
                    var maxProfit = credit * 100;
                    var pop = shortCall.Delta.HasValue ? (1 - Math.Abs(shortCall.Delta.Value)) * 100 : 0;

                    var candidate = new OptionCandidate
                    {
                        UnderlyingSymbol = symbol,
                        Strategy = StrategyType.BearCallSpread,
                        Legs = new List<OptionLeg>
                        {
                            CreateLeg(shortCall, symbol, OrderAction.Sell),
                            CreateLeg(longCall, symbol, OrderAction.Buy)
                        },
                        MaxProfit = maxProfit,
                        MaxLoss = maxLoss,
                        NetCredit = credit,
                        ProbabilityOfProfit = pop,
                        IVRank = analytics.IVRank,
                        IVPercentile = analytics.IVPercentile,
                        UnderlyingPrice = underlyingPrice
                    };

                    OptionCandidateScorer.Score(candidate);
                    result.BearCallSpreadCandidates.Add(candidate);
                    break; // One long call per short call
                }
            }
        }
    }

    internal void ScreenIronCondors(
        List<OptionContract> chain, string symbol, decimal underlyingPrice,
        OptionsAnalytics analytics, RegimeType regime, OptionsScreenResult result)
    {
        // Iron condors only in RiskOn (range-bound conditions)
        if (regime != RegimeType.RiskOn)
            return;

        // Build temporary results for put and call sides
        var putSideResult = new OptionsScreenResult();
        var callSideResult = new OptionsScreenResult();

        ScreenBullPutSpreads(chain, symbol, underlyingPrice, analytics, RegimeType.RiskOn, putSideResult);
        ScreenBearCallSpreads(chain, symbol, underlyingPrice, analytics, RegimeType.RiskOn, callSideResult);

        // Combine best put spread with best call spread of same expiration
        var putsByExp = putSideResult.BullPutSpreadCandidates
            .GroupBy(c => c.Legs[0].Expiration)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Score).First());

        var callsByExp = callSideResult.BearCallSpreadCandidates
            .GroupBy(c => c.Legs[0].Expiration)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.Score).First());

        foreach (var (expiration, putSpread) in putsByExp)
        {
            if (!callsByExp.TryGetValue(expiration, out var callSpread))
                continue;

            var allLegs = putSpread.Legs.Concat(callSpread.Legs).ToList();
            var totalCredit = putSpread.NetCredit + callSpread.NetCredit;
            var totalMaxLoss = Math.Max(putSpread.MaxLoss, callSpread.MaxLoss); // Worst case is one side

            var candidate = new OptionCandidate
            {
                UnderlyingSymbol = symbol,
                Strategy = StrategyType.IronCondor,
                Legs = allLegs,
                MaxProfit = totalCredit * 100,
                MaxLoss = totalMaxLoss,
                NetCredit = totalCredit,
                ProbabilityOfProfit = Math.Min(putSpread.ProbabilityOfProfit, callSpread.ProbabilityOfProfit),
                IVRank = analytics.IVRank,
                IVPercentile = analytics.IVPercentile,
                UnderlyingPrice = underlyingPrice
            };

            OptionCandidateScorer.Score(candidate);
            result.IronCondorCandidates.Add(candidate);
        }
    }

    internal void ScreenCalendarSpreads(
        List<OptionContract> chain, string symbol, decimal underlyingPrice,
        OptionsAnalytics analytics, OptionsScreenResult result)
    {
        // Calendar spreads allowed in any non-RiskOff regime (already filtered above)
        // Look for favorable term structure: back month IV > front month IV

        var puts = chain
            .Where(c => c.Right == OptionRight.Put &&
                        c.Delta.HasValue &&
                        Math.Abs(c.Delta.Value) >= 0.30m &&
                        Math.Abs(c.Delta.Value) <= 0.50m &&
                        c.ImpliedVolatility.HasValue)
            .GroupBy(c => c.Strike)
            .ToList();

        foreach (var strikeGroup in puts)
        {
            var byExpiration = strikeGroup.OrderBy(c => c.Expiration).ToList();
            if (byExpiration.Count < 2) continue;

            var frontMonth = byExpiration[0];
            var backMonth = byExpiration[^1];

            // Require minimum DTE separation
            if ((backMonth.Expiration - frontMonth.Expiration).Days < 14) continue;
            if (frontMonth.DTE < 14 || backMonth.DTE > 90) continue;

            // Favorable term structure: back month IV should be higher or similar
            if (backMonth.ImpliedVolatility < frontMonth.ImpliedVolatility * 0.95m)
                continue;

            var debit = backMonth.Mid - frontMonth.Mid;
            if (debit <= 0) continue;

            // Calendar spread max profit is hard to calculate precisely; estimate
            var maxProfit = frontMonth.Mid * 100; // Approximate: front month premium collected
            var maxLoss = debit * 100; // Max loss is debit paid

            var candidate = new OptionCandidate
            {
                UnderlyingSymbol = symbol,
                Strategy = StrategyType.CalendarSpread,
                Legs = new List<OptionLeg>
                {
                    CreateLeg(frontMonth, symbol, OrderAction.Sell),
                    CreateLeg(backMonth, symbol, OrderAction.Buy)
                },
                MaxProfit = maxProfit,
                MaxLoss = maxLoss,
                NetCredit = -debit, // Negative = net debit
                ProbabilityOfProfit = 50, // Calendar spreads are harder to estimate POP
                IVRank = analytics.IVRank,
                IVPercentile = analytics.IVPercentile,
                UnderlyingPrice = underlyingPrice
            };

            OptionCandidateScorer.Score(candidate);
            result.CalendarSpreadCandidates.Add(candidate);
        }
    }

    private static OptionLeg CreateLeg(OptionContract contract, string underlying, OrderAction action)
    {
        return new OptionLeg
        {
            Symbol = contract.Symbol,
            UnderlyingSymbol = underlying,
            Strike = contract.Strike,
            Expiration = contract.Expiration,
            Right = contract.Right,
            Action = action,
            Delta = contract.Delta,
            Theta = contract.Theta,
            ImpliedVolatility = contract.ImpliedVolatility,
            Bid = contract.Bid,
            Ask = contract.Ask
        };
    }
}
