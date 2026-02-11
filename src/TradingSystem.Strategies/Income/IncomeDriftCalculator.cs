using TradingSystem.Core.Configuration;
using TradingSystem.Core.Models;

namespace TradingSystem.Strategies.Income;

/// <summary>
/// Pure calculation class for income sleeve drift analysis.
/// No I/O or side effects -- all inputs passed in, all outputs returned.
/// </summary>
public static class IncomeDriftCalculator
{
    /// <summary>
    /// Builds a full IncomeSleeveState from positions and config.
    /// </summary>
    public static IncomeSleeveState BuildSleeveState(
        List<Position> incomePositions,
        decimal cashBuffer,
        IncomeConfig config)
    {
        var totalValue = incomePositions.Sum(p => p.MarketValue) + cashBuffer;
        var state = new IncomeSleeveState
        {
            TotalValue = totalValue,
            CashBuffer = cashBuffer,
            LastUpdated = DateTime.UtcNow
        };

        // Build category allocations
        foreach (var (categoryName, target) in config.AllocationTargets)
        {
            if (!Enum.TryParse<IncomeCategory>(categoryName, out var category))
                continue;

            var categoryPositions = incomePositions
                .Where(p => p.Category == categoryName)
                .ToList();

            var categoryValue = categoryPositions.Sum(p => p.MarketValue);
            var currentPercent = totalValue > 0 ? categoryValue / totalValue : 0;

            state.Categories[category] = new CategoryAllocation
            {
                Category = category,
                TargetPercent = target,
                CurrentPercent = currentPercent,
                CurrentValue = categoryValue,
                Positions = categoryPositions
            };

            state.CategoryDrift[category] = currentPercent - target;
        }

        // Build issuer exposures
        var symbolGroups = incomePositions
            .GroupBy(p => p.Symbol, StringComparer.OrdinalIgnoreCase);

        foreach (var group in symbolGroups)
        {
            var exposureValue = group.Sum(p => p.MarketValue);
            var exposurePercent = totalValue > 0 ? exposureValue / totalValue : 0;

            state.IssuerExposures.Add(new IssuerExposure
            {
                Issuer = group.Key,
                ExposurePercent = exposurePercent,
                ExposureValue = exposureValue,
                ExceedsCap = exposurePercent > config.MaxIssuerPercent,
                Symbols = group.Select(p => p.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            });
        }

        return state;
    }

    /// <summary>
    /// Returns categories sorted by drift (most underweight first).
    /// Only returns categories that are underweight by at least the given threshold.
    /// </summary>
    public static List<(IncomeCategory Category, decimal Drift)> GetUnderweightCategories(
        IncomeSleeveState state,
        decimal minDriftThreshold = 0.02m)
    {
        return state.CategoryDrift
            .Where(kv => kv.Value < -minDriftThreshold)
            .OrderBy(kv => kv.Value) // most negative (most underweight) first
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    /// <summary>
    /// Checks if adding the given dollar amount to a symbol would violate the issuer cap.
    /// </summary>
    public static bool WouldViolateIssuerCap(
        string symbol,
        decimal additionalValue,
        IncomeSleeveState state,
        decimal maxIssuerPercent)
    {
        var currentExposure = state.IssuerExposures
            .FirstOrDefault(e => e.Issuer.Equals(symbol, StringComparison.OrdinalIgnoreCase));

        var currentValue = currentExposure?.ExposureValue ?? 0;
        var newTotalValue = state.TotalValue + additionalValue;
        var newExposurePercent = newTotalValue > 0
            ? (currentValue + additionalValue) / newTotalValue
            : 0;

        return newExposurePercent > maxIssuerPercent;
    }

    /// <summary>
    /// Checks if adding the given dollar amount to a category would violate the category cap.
    /// </summary>
    public static bool WouldViolateCategoryCap(
        IncomeCategory category,
        decimal additionalValue,
        IncomeSleeveState state,
        decimal maxCategoryPercent)
    {
        var currentAllocation = state.Categories.GetValueOrDefault(category);
        var currentValue = currentAllocation?.CurrentValue ?? 0;
        var newTotalValue = state.TotalValue + additionalValue;
        var newCategoryPercent = newTotalValue > 0
            ? (currentValue + additionalValue) / newTotalValue
            : 0;

        return newCategoryPercent > maxCategoryPercent;
    }
}
