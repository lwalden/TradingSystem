using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Strategies.Options;

/// <summary>
/// Converts options candidates and lifecycle events into executable signals.
/// </summary>
public class OptionsCandidateConverter
{
    public Signal ConvertToEntrySignal(OptionCandidate candidate, int contracts, DateTime? now = null)
    {
        if (contracts <= 0)
            throw new ArgumentOutOfRangeException(nameof(contracts), contracts, "Contracts must be > 0.");

        var timestamp = now ?? DateTime.UtcNow;
        var strategyId = StrategyToId(candidate.Strategy);
        var securityType = candidate.Legs.Count > 1 ? "BAG" : "OPT";
        var expectedR = candidate.MaxLoss != 0
            ? candidate.MaxProfit / Math.Abs(candidate.MaxLoss)
            : 0m;

        return new Signal
        {
            StrategyId = strategyId,
            StrategyName = StrategyToName(candidate.Strategy),
            SetupType = candidate.Strategy.ToString(),
            Symbol = candidate.UnderlyingSymbol,
            SecurityType = securityType,
            Direction = candidate.NetCredit >= 0 ? SignalDirection.Short : SignalDirection.Long,
            Strength = ScoreToStrength(candidate.Score),
            SuggestedEntryPrice = candidate.NetCredit,
            SuggestedPositionSize = contracts,
            SuggestedRiskAmount = Math.Abs(candidate.MaxLoss) * contracts,
            ExpectedRMultiple = expectedR,
            SuggestedLegs = candidate.Legs.Select(CloneLeg).ToList(),
            MaxProfit = candidate.MaxProfit,
            MaxLoss = Math.Abs(candidate.MaxLoss),
            ProbabilityOfProfit = candidate.ProbabilityOfProfit,
            IVRank = candidate.IVRank,
            IVPercentile = candidate.IVPercentile,
            Rationale = $"Score {candidate.Score:F1}: {candidate.ScoreBreakdown}",
            GeneratedAt = timestamp,
            ExpiresAt = timestamp.AddHours(8),
            Indicators = new Dictionary<string, object>
            {
                ["strategyType"] = candidate.Strategy.ToString(),
                ["score"] = candidate.Score,
                ["scoreBreakdown"] = candidate.ScoreBreakdown,
                ["netCredit"] = candidate.NetCredit,
                ["underlyingPrice"] = candidate.UnderlyingPrice
            }
        };
    }

    public Signal ConvertToCloseSignal(
        OptionsPosition position,
        OptionsLifecycleDecision lifecycleDecision,
        DateTime? now = null)
    {
        var timestamp = now ?? DateTime.UtcNow;
        var strategyId = StrategyToId(position.Strategy);
        var closeLegs = position.Legs.Select(ReverseLeg).ToList();
        var securityType = closeLegs.Count > 1 ? "BAG" : "OPT";

        return new Signal
        {
            StrategyId = $"{strategyId}-close",
            StrategyName = $"{StrategyToName(position.Strategy)} Close",
            SetupType = "LifecycleClose",
            Symbol = position.UnderlyingSymbol,
            SecurityType = securityType,
            Direction = SignalDirection.ClosePosition,
            Strength = SignalStrength.Strong,
            SuggestedEntryPrice = position.CurrentValue,
            SuggestedPositionSize = position.Quantity,
            SuggestedLegs = closeLegs,
            Rationale = lifecycleDecision.Reason,
            GeneratedAt = timestamp,
            ExpiresAt = timestamp.AddHours(8),
            Indicators = new Dictionary<string, object>
            {
                ["positionId"] = position.Id,
                ["lifecycleAction"] = lifecycleDecision.Action.ToString(),
                ["exitReason"] = lifecycleDecision.Reason,
                ["strategyType"] = position.Strategy.ToString(),
                ["netCredit"] = position.CurrentValue
            }
        };
    }

    public List<Signal> ConvertToRollSignals(
        OptionsPosition currentPosition,
        OptionCandidate replacementCandidate,
        int replacementContracts,
        DateTime? now = null)
    {
        var decision = new OptionsLifecycleDecision(
            OptionsLifecycleAction.Roll,
            $"Roll requested for {currentPosition.UnderlyingSymbol}.");

        var closeSignal = ConvertToCloseSignal(currentPosition, decision, now);
        var openSignal = ConvertToEntrySignal(replacementCandidate, replacementContracts, now);
        openSignal.SetupType = "RollOpen";
        openSignal.Indicators["rollFromPositionId"] = currentPosition.Id;

        return new List<Signal> { closeSignal, openSignal };
    }

    internal static OptionLeg CloneLeg(OptionLeg leg)
    {
        return new OptionLeg
        {
            Symbol = leg.Symbol,
            UnderlyingSymbol = leg.UnderlyingSymbol,
            Strike = leg.Strike,
            Expiration = leg.Expiration,
            Right = leg.Right,
            Action = leg.Action,
            Quantity = leg.Quantity,
            Delta = leg.Delta,
            Theta = leg.Theta,
            ImpliedVolatility = leg.ImpliedVolatility,
            Bid = leg.Bid,
            Ask = leg.Ask
        };
    }

    internal static OptionLeg ReverseLeg(OptionsPositionLeg leg)
    {
        return new OptionLeg
        {
            Symbol = leg.Symbol,
            UnderlyingSymbol = ExtractUnderlyingFromOptionSymbol(leg.Symbol),
            Strike = leg.Strike,
            Expiration = leg.Expiration,
            Right = leg.Right,
            Action = leg.Action == OrderAction.Sell ? OrderAction.Buy : OrderAction.Sell,
            Quantity = leg.Quantity
        };
    }

    internal static SignalStrength ScoreToStrength(decimal score)
    {
        if (score >= 85) return SignalStrength.VeryStrong;
        if (score >= 70) return SignalStrength.Strong;
        if (score >= 55) return SignalStrength.Moderate;
        return SignalStrength.Weak;
    }

    internal static string StrategyToId(StrategyType strategy)
    {
        return strategy switch
        {
            StrategyType.CashSecuredPut => "options-csp",
            StrategyType.BullPutSpread => "options-bull-put-spread",
            StrategyType.BearCallSpread => "options-bear-call-spread",
            StrategyType.IronCondor => "options-iron-condor",
            StrategyType.CalendarSpread => "options-calendar-spread",
            StrategyType.DiagonalSpread => "options-diagonal-spread",
            StrategyType.CoveredCall => "options-covered-call",
            _ => "options"
        };
    }

    internal static string StrategyToName(StrategyType strategy)
    {
        return strategy switch
        {
            StrategyType.CashSecuredPut => "Options Cash-Secured Put",
            StrategyType.BullPutSpread => "Options Bull Put Spread",
            StrategyType.BearCallSpread => "Options Bear Call Spread",
            StrategyType.IronCondor => "Options Iron Condor",
            StrategyType.CalendarSpread => "Options Calendar Spread",
            StrategyType.DiagonalSpread => "Options Diagonal Spread",
            StrategyType.CoveredCall => "Options Covered Call",
            _ => "Options Strategy"
        };
    }

    internal static string ExtractUnderlyingFromOptionSymbol(string optionSymbol)
    {
        if (string.IsNullOrWhiteSpace(optionSymbol))
            return string.Empty;

        if (optionSymbol.Contains(' '))
            return optionSymbol.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant();

        var letters = new string(optionSymbol.TakeWhile(char.IsLetter).ToArray());
        return string.IsNullOrWhiteSpace(letters)
            ? optionSymbol.ToUpperInvariant()
            : letters.ToUpperInvariant();
    }
}
