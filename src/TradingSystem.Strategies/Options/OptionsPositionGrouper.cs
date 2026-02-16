using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Strategies.Options;

/// <summary>
/// Reconciles tracked options positions with broker positions and groups untracked broker legs.
/// </summary>
public class OptionsPositionGrouper
{
    public OptionsPositionReconciliationResult Reconcile(
        IEnumerable<OptionsPosition> trackedPositions,
        IEnumerable<Position> brokerPositions)
    {
        var tracked = trackedPositions.ToList();
        var optionBrokerPositions = FilterOptionsPositions(brokerPositions).ToList();
        var brokerBySymbol = optionBrokerPositions
            .GroupBy(p => NormalizeSymbol(p.Symbol))
            .ToDictionary(g => g.Key, g => g.First());

        var matchedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reconciled = new List<OptionsPosition>();
        var warnings = new List<string>();

        foreach (var trackedPosition in tracked)
        {
            var matchedLegCount = 0;
            foreach (var leg in trackedPosition.Legs)
            {
                var legSymbol = NormalizeSymbol(leg.Symbol);
                if (!brokerBySymbol.TryGetValue(legSymbol, out var brokerLeg))
                    continue;

                leg.CurrentPrice = Math.Abs(brokerLeg.MarketPrice);
                matchedLegCount++;
                matchedSymbols.Add(legSymbol);
            }

            if (matchedLegCount == 0)
            {
                warnings.Add($"Tracked position {trackedPosition.Id} ({trackedPosition.UnderlyingSymbol}) not found at broker.");
                continue;
            }

            if (matchedLegCount != trackedPosition.Legs.Count)
            {
                warnings.Add(
                    $"Tracked position {trackedPosition.Id} partially matched ({matchedLegCount}/{trackedPosition.Legs.Count} legs).");
            }

            trackedPosition.CurrentValue = CalculateNetPrice(trackedPosition.Legs);
            trackedPosition.LastUpdated = DateTime.UtcNow;
            reconciled.Add(trackedPosition);
        }

        var untracked = optionBrokerPositions
            .Where(p => !matchedSymbols.Contains(NormalizeSymbol(p.Symbol)))
            .ToList();

        var groupedUntracked = GroupBrokerPositions(untracked);
        if (groupedUntracked.Count > 0)
        {
            warnings.Add($"Detected {groupedUntracked.Count} untracked broker options position group(s).");
        }

        return new OptionsPositionReconciliationResult
        {
            ReconciledTrackedPositions = reconciled,
            UntrackedBrokerPositions = groupedUntracked,
            Warnings = warnings
        };
    }

    public List<OptionsPosition> GroupBrokerPositions(IEnumerable<Position> brokerPositions)
    {
        var options = FilterOptionsPositions(brokerPositions).ToList();
        if (options.Count == 0)
            return new List<OptionsPosition>();

        var groups = options
            .GroupBy(p => BuildGroupingKey(p))
            .ToList();

        var result = new List<OptionsPosition>();
        foreach (var group in groups)
        {
            var legs = group
                .OrderBy(p => p.Expiration ?? DateTime.MaxValue)
                .ThenBy(p => p.Right)
                .ThenBy(p => p.Strike ?? 0m)
                .Select(CreatePositionLeg)
                .ToList();

            if (legs.Count == 0)
                continue;

            var quantity = Math.Max(1, group.Min(p => (int)Math.Abs(p.Quantity)));
            var strategy = IdentifyStrategy(legs);
            var entryNetCredit = CalculateEntryNetCredit(group);
            var currentValue = CalculateCurrentValue(group);
            var (maxProfit, maxLoss) = EstimateMaxProfitAndLoss(strategy, legs, entryNetCredit);
            var expiration = legs.Min(l => l.Expiration);

            result.Add(new OptionsPosition
            {
                UnderlyingSymbol = group.First().UnderlyingSymbol ?? ParseUnderlyingFromSymbol(group.First().Symbol),
                Strategy = strategy,
                Sleeve = SleeveType.Tactical,
                Legs = legs,
                EntryNetCredit = entryNetCredit,
                MaxProfit = maxProfit,
                MaxLoss = maxLoss,
                Quantity = quantity,
                CurrentValue = currentValue,
                Status = OptionsPositionStatus.Open,
                Expiration = expiration,
                OpenedAt = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            });
        }

        return result;
    }

    internal static IEnumerable<Position> FilterOptionsPositions(IEnumerable<Position> positions)
    {
        return positions.Where(p =>
            p.SecurityType.Equals("OPT", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(p.Symbol) &&
            p.Quantity != 0);
    }

    internal static string BuildGroupingKey(Position position)
    {
        if (!string.IsNullOrWhiteSpace(position.TradeId))
            return position.TradeId;

        var underlying = position.UnderlyingSymbol ?? ParseUnderlyingFromSymbol(position.Symbol);
        var expiration = position.Expiration?.Date ?? DateTime.MinValue;
        return $"{underlying}|{expiration:yyyyMMdd}";
    }

    internal static string NormalizeSymbol(string symbol) => symbol.Trim().ToUpperInvariant();

    internal static string ParseUnderlyingFromSymbol(string symbol)
    {
        var trimmed = symbol.Trim();
        if (trimmed.Contains(' '))
            return trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant();

        // Fallback for OCC-like compact symbols where prefix starts with letters.
        var letters = new string(trimmed.TakeWhile(char.IsLetter).ToArray());
        return string.IsNullOrWhiteSpace(letters) ? trimmed.ToUpperInvariant() : letters.ToUpperInvariant();
    }

    internal static OptionsPositionLeg CreatePositionLeg(Position brokerPosition)
    {
        var quantity = Math.Max(1, (int)Math.Abs(brokerPosition.Quantity));
        return new OptionsPositionLeg
        {
            Symbol = brokerPosition.Symbol,
            Strike = brokerPosition.Strike ?? 0m,
            Expiration = brokerPosition.Expiration ?? DateTime.Today,
            Right = brokerPosition.Right ?? OptionRight.Call,
            Action = brokerPosition.Quantity < 0 ? OrderAction.Sell : OrderAction.Buy,
            Quantity = quantity,
            EntryPrice = Math.Abs(brokerPosition.AverageCost),
            CurrentPrice = Math.Abs(brokerPosition.MarketPrice)
        };
    }

    internal static StrategyType IdentifyStrategy(IReadOnlyList<OptionsPositionLeg> legs)
    {
        if (legs.Count == 1)
        {
            var leg = legs[0];
            if (leg.Right == OptionRight.Put && leg.Action == OrderAction.Sell)
                return StrategyType.CashSecuredPut;
            if (leg.Right == OptionRight.Call && leg.Action == OrderAction.Sell)
                return StrategyType.CoveredCall;
            return StrategyType.DiagonalSpread;
        }

        if (legs.Count == 2)
        {
            var first = legs[0];
            var second = legs[1];

            if (first.Right == second.Right && first.Strike == second.Strike && first.Expiration != second.Expiration)
                return StrategyType.CalendarSpread;

            if (first.Right == OptionRight.Put && second.Right == OptionRight.Put)
            {
                var shortPut = legs.FirstOrDefault(l => l.Action == OrderAction.Sell);
                var longPut = legs.FirstOrDefault(l => l.Action == OrderAction.Buy);
                if (shortPut != null && longPut != null && shortPut.Strike > longPut.Strike)
                    return StrategyType.BullPutSpread;
            }

            if (first.Right == OptionRight.Call && second.Right == OptionRight.Call)
            {
                var shortCall = legs.FirstOrDefault(l => l.Action == OrderAction.Sell);
                var longCall = legs.FirstOrDefault(l => l.Action == OrderAction.Buy);
                if (shortCall != null && longCall != null && shortCall.Strike < longCall.Strike)
                    return StrategyType.BearCallSpread;
            }
        }

        if (legs.Count == 4 &&
            legs.Count(l => l.Right == OptionRight.Put) == 2 &&
            legs.Count(l => l.Right == OptionRight.Call) == 2)
        {
            return StrategyType.IronCondor;
        }

        return StrategyType.DiagonalSpread;
    }

    internal static decimal CalculateEntryNetCredit(IEnumerable<Position> brokerLegs)
    {
        return brokerLegs.Sum(p => p.Quantity < 0
            ? Math.Abs(p.AverageCost)
            : -Math.Abs(p.AverageCost));
    }

    internal static decimal CalculateCurrentValue(IEnumerable<Position> brokerLegs)
    {
        return brokerLegs.Sum(p => p.Quantity < 0
            ? Math.Abs(p.MarketPrice)
            : -Math.Abs(p.MarketPrice));
    }

    internal static decimal CalculateNetPrice(IEnumerable<OptionsPositionLeg> legs)
    {
        return legs.Sum(l => l.Action == OrderAction.Sell ? l.CurrentPrice : -l.CurrentPrice);
    }

    internal static (decimal MaxProfit, decimal MaxLoss) EstimateMaxProfitAndLoss(
        StrategyType strategy,
        IReadOnlyList<OptionsPositionLeg> legs,
        decimal entryNetCredit)
    {
        var credit = Math.Abs(entryNetCredit);
        if (strategy == StrategyType.CalendarSpread)
        {
            var debit = Math.Max(-entryNetCredit, 0m);
            return (Math.Max(credit, debit), Math.Max(debit, credit));
        }

        if (strategy == StrategyType.CashSecuredPut || strategy == StrategyType.CoveredCall)
        {
            var strike = legs.First().Strike;
            return (Math.Max(entryNetCredit, 0m), Math.Max(strike - Math.Max(entryNetCredit, 0m), 0m));
        }

        if (strategy == StrategyType.BullPutSpread || strategy == StrategyType.BearCallSpread)
        {
            var width = Math.Abs(legs.Max(l => l.Strike) - legs.Min(l => l.Strike));
            if (entryNetCredit >= 0)
                return (entryNetCredit, Math.Max(width - entryNetCredit, 0m));

            var debit = Math.Abs(entryNetCredit);
            return (Math.Max(width - debit, 0m), debit);
        }

        if (strategy == StrategyType.IronCondor)
        {
            var putStrikes = legs.Where(l => l.Right == OptionRight.Put).Select(l => l.Strike).ToList();
            var callStrikes = legs.Where(l => l.Right == OptionRight.Call).Select(l => l.Strike).ToList();
            var putWidth = putStrikes.Count == 2 ? Math.Abs(putStrikes[0] - putStrikes[1]) : 0m;
            var callWidth = callStrikes.Count == 2 ? Math.Abs(callStrikes[0] - callStrikes[1]) : 0m;
            var widest = Math.Max(putWidth, callWidth);
            return (Math.Max(entryNetCredit, 0m), Math.Max(widest - Math.Max(entryNetCredit, 0m), 0m));
        }

        return (Math.Max(entryNetCredit, 0m), credit);
    }
}

public class OptionsPositionReconciliationResult
{
    public List<OptionsPosition> ReconciledTrackedPositions { get; set; } = new();
    public List<OptionsPosition> UntrackedBrokerPositions { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
