using Microsoft.Extensions.Logging;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Strategies.Common;

namespace TradingSystem.Strategies.Income;

/// <summary>
/// Monthly reinvestment strategy for Income sleeve
/// Allocates dividends/interest to reduce drift from target allocations
/// </summary>
public class MonthlyReinvestStrategy : StrategyBase
{
    private readonly IncomeUniverse _universe;

    public MonthlyReinvestStrategy(ILogger<MonthlyReinvestStrategy> logger)
        : base(logger)
    {
        _universe = new IncomeUniverse();
    }

    public override string Id => "income-monthly-reinvest";
    public override string Name => "Income Monthly Reinvest";
    public override string Description => "Reinvests dividends/interest to maintain target allocations";
    public override SleeveType Sleeve => SleeveType.Income;
    public override StrategyType Type => StrategyType.MonthlyReinvest;
    public override bool RequiresAIAnalysis => false;

    public override async Task<List<Signal>> EvaluateAsync(StrategyContext context,
        CancellationToken cancellationToken = default)
    {
        var signals = new List<Signal>();
        var config = context.Config.Income;

        _logger.LogInformation("Evaluating monthly reinvest strategy");

        // Step 1: Calculate current sleeve state
        var sleevePositions = context.Account.Positions
            .Where(p => p.Sleeve == SleeveType.Income)
            .ToList();

        var sleeveValue = sleevePositions.Sum(p => p.MarketValue);
        var cashAvailable = context.Account.TotalCashValue * 0.7m; // Assume 70% allocated to income

        if (cashAvailable < config.MinLotDollars())
        {
            _logger.LogInformation("Insufficient cash for reinvestment: {Cash}", cashAvailable);
            return signals;
        }

        // Step 2: Calculate current weights by category
        var currentWeights = CalculateCategoryWeights(sleevePositions, sleeveValue);

        // Step 3: Find categories below target (drift)
        var driftAnalysis = AnalyzeDrift(currentWeights, config.AllocationTargets);

        // Step 4: Build buy candidates from underweight categories
        foreach (var (category, drift) in driftAnalysis.Where(d => d.Value < -0.02m)) // >2% underweight
        {
            var candidates = _universe.GetByCategory(ParseCategory(category))
                .Where(s => s.IsEnabled)
                .ToList();

            if (!candidates.Any()) continue;

            // Simple allocation: buy the first candidate that passes quality gates
            foreach (var candidate in candidates)
            {
                if (!PassesQualityGates(candidate, config.QualityGates))
                    continue;

                // Check issuer cap
                var issuerExposure = GetIssuerExposure(candidate.Symbol, sleevePositions, sleeveValue);
                if (issuerExposure >= config.MaxIssuerPercent)
                    continue;

                // Calculate buy amount
                var targetAmount = Math.Abs(drift) * sleeveValue;
                var buyAmount = Math.Min(targetAmount, cashAvailable * 0.3m); // Max 30% of cash per security

                if (buyAmount < 100m) continue; // Min $100

                var quote = context.GetQuote(candidate.Symbol);
                if (quote == null) continue;

                var shares = (int)(buyAmount / quote.Ask);
                if (shares < 1) continue;

                var signal = CreateSignal(
                    candidate.Symbol,
                    SignalDirection.Long,
                    SignalStrength.Moderate,
                    $"Reinvest to reduce {category} drift of {drift:P1}");

                signal.SuggestedPositionSize = shares;
                signal.SuggestedEntryPrice = quote.Ask;
                signal.SuggestedRiskAmount = buyAmount;

                signals.Add(signal);
                _logger.LogInformation("Generated reinvest signal: {Symbol} {Shares} shares", 
                    candidate.Symbol, shares);

                break; // One signal per category
            }
        }

        return signals;
    }

    private Dictionary<string, decimal> CalculateCategoryWeights(
        List<Position> positions, decimal totalValue)
    {
        var weights = new Dictionary<string, decimal>();
        
        foreach (var pos in positions)
        {
            var category = pos.Category;
            if (string.IsNullOrEmpty(category)) continue;

            if (!weights.ContainsKey(category))
                weights[category] = 0;
            
            weights[category] += totalValue > 0 ? pos.MarketValue / totalValue : 0;
        }

        return weights;
    }

    private Dictionary<string, decimal> AnalyzeDrift(
        Dictionary<string, decimal> current, 
        Dictionary<string, decimal> targets)
    {
        var drift = new Dictionary<string, decimal>();
        
        foreach (var (category, target) in targets)
        {
            var currentWeight = current.GetValueOrDefault(category, 0);
            drift[category] = currentWeight - target;
        }

        return drift;
    }

    private bool PassesQualityGates(IncomeSecurity security, IncomeQualityGates gates)
    {
        // TODO: Implement actual quality gate checks when we have data
        // For now, assume all pass
        return true;
    }

    private decimal GetIssuerExposure(string symbol, List<Position> positions, decimal totalValue)
    {
        var exposure = positions
            .Where(p => p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .Sum(p => p.MarketValue);
        
        return totalValue > 0 ? exposure / totalValue : 0;
    }

    private IncomeCategory ParseCategory(string category)
    {
        return Enum.TryParse<IncomeCategory>(category, out var result) 
            ? result 
            : IncomeCategory.CashBuffer;
    }
}

// Extension method for config
public static class IncomeConfigExtensions
{
    public static decimal MinLotDollars(this IncomeConfig config) => 100m;
}
