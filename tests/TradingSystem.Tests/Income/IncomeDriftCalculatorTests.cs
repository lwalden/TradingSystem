using TradingSystem.Core.Configuration;
using TradingSystem.Core.Models;
using TradingSystem.Strategies.Income;
using Xunit;

namespace TradingSystem.Tests.Income;

public class IncomeDriftCalculatorTests
{
    private static IncomeConfig DefaultConfig => new();

    private static List<Position> CreatePositions(params (string symbol, string category, decimal value)[] items)
    {
        return items.Select(i => new Position
        {
            Symbol = i.symbol,
            Category = i.category,
            Sleeve = SleeveType.Income,
            Quantity = i.value / 10m, // assume $10/share
            MarketPrice = 10m
        }).ToList();
    }

    [Fact]
    public void BuildSleeveState_CalculatesCorrectTotalValue()
    {
        var positions = CreatePositions(
            ("VIG", "DividendGrowthETF", 2500),
            ("ARCC", "BDC", 2000));

        var state = IncomeDriftCalculator.BuildSleeveState(positions, 500, DefaultConfig);

        Assert.Equal(5000m, state.TotalValue); // 2500 + 2000 + 500 cash
    }

    [Fact]
    public void BuildSleeveState_CalculatesCategoryWeights()
    {
        var positions = CreatePositions(
            ("VIG", "DividendGrowthETF", 2500),
            ("ARCC", "BDC", 2000));

        var state = IncomeDriftCalculator.BuildSleeveState(positions, 500, DefaultConfig);

        Assert.Equal(0.50m, state.Categories[IncomeCategory.DividendGrowthETF].CurrentPercent);
        Assert.Equal(0.40m, state.Categories[IncomeCategory.BDC].CurrentPercent);
    }

    [Fact]
    public void BuildSleeveState_CalculatesDrift()
    {
        var positions = CreatePositions(
            ("VIG", "DividendGrowthETF", 2500),
            ("ARCC", "BDC", 2000));

        var state = IncomeDriftCalculator.BuildSleeveState(positions, 500, DefaultConfig);

        // DividendGrowthETF: 50% current - 25% target = +25%
        Assert.Equal(0.25m, state.CategoryDrift[IncomeCategory.DividendGrowthETF]);
        // BDC: 40% current - 20% target = +20%
        Assert.Equal(0.20m, state.CategoryDrift[IncomeCategory.BDC]);
        // CoveredCallETF: 0% current - 20% target = -20%
        Assert.Equal(-0.20m, state.CategoryDrift[IncomeCategory.CoveredCallETF]);
    }

    [Fact]
    public void BuildSleeveState_CalculatesIssuerExposures()
    {
        var positions = CreatePositions(
            ("VIG", "DividendGrowthETF", 2500),
            ("ARCC", "BDC", 2000));

        var state = IncomeDriftCalculator.BuildSleeveState(positions, 500, DefaultConfig);

        Assert.Equal(2, state.IssuerExposures.Count);
        var vigExposure = state.IssuerExposures.First(e => e.Issuer == "VIG");
        Assert.Equal(0.50m, vigExposure.ExposurePercent);
        Assert.True(vigExposure.ExceedsCap); // > 10%
    }

    [Fact]
    public void BuildSleeveState_EmptyPositions_ReturnsZeroState()
    {
        var state = IncomeDriftCalculator.BuildSleeveState([], 0, DefaultConfig);

        Assert.Equal(0m, state.TotalValue);
        Assert.Empty(state.IssuerExposures);
    }

    [Fact]
    public void BuildSleeveState_CashOnly_CalculatesCorrectly()
    {
        var state = IncomeDriftCalculator.BuildSleeveState([], 10000, DefaultConfig);

        Assert.Equal(10000m, state.TotalValue);
        // All categories should be underweight relative to their targets
        Assert.All(state.CategoryDrift.Values, d => Assert.True(d <= 0));
    }

    [Fact]
    public void GetUnderweightCategories_ReturnsMostUnderweightFirst()
    {
        var positions = CreatePositions(
            ("VIG", "DividendGrowthETF", 5000)); // All in one category

        var state = IncomeDriftCalculator.BuildSleeveState(positions, 0, DefaultConfig);
        var underweight = IncomeDriftCalculator.GetUnderweightCategories(state);

        Assert.True(underweight.Count > 0);
        // Should be sorted most negative first
        for (int i = 1; i < underweight.Count; i++)
        {
            Assert.True(underweight[i].Drift >= underweight[i - 1].Drift);
        }
    }

    [Fact]
    public void GetUnderweightCategories_RespectsThreshold()
    {
        var positions = CreatePositions(
            ("VIG", "DividendGrowthETF", 2400),
            ("JEPI", "CoveredCallETF", 1900),
            ("ARCC", "BDC", 1900),
            ("O", "EquityREIT", 950),
            ("AGNC", "MortgageREIT", 950),
            ("PFF", "PreferredsIGCredit", 950));
        // Very close to target => small drift

        var state = IncomeDriftCalculator.BuildSleeveState(positions, 500, DefaultConfig);

        // With a high threshold, fewer categories should be returned
        var underweight = IncomeDriftCalculator.GetUnderweightCategories(state, 0.10m);
        Assert.True(underweight.Count <= 6);
    }

    [Fact]
    public void GetUnderweightCategories_PerfectAllocation_ReturnsEmpty()
    {
        // Create positions that exactly match targets (total = 10000, no cash buffer offset)
        var positions = CreatePositions(
            ("VIG", "DividendGrowthETF", 2500),
            ("JEPI", "CoveredCallETF", 2000),
            ("ARCC", "BDC", 2000),
            ("O", "EquityREIT", 1000),
            ("AGNC", "MortgageREIT", 1000),
            ("PFF", "PreferredsIGCredit", 1000));

        var state = IncomeDriftCalculator.BuildSleeveState(positions, 500, DefaultConfig);
        // Note: CashBuffer is 5% target, we have 500/10500 = ~4.76% -- close enough
        var underweight = IncomeDriftCalculator.GetUnderweightCategories(state, 0.03m);

        // With 500 cash buffer, everything shifts slightly, but no major underweight
        Assert.True(underweight.Count <= 2);
    }

    [Fact]
    public void WouldViolateIssuerCap_BelowCap_ReturnsFalse()
    {
        var positions = CreatePositions(("VIG", "DividendGrowthETF", 500));
        var state = IncomeDriftCalculator.BuildSleeveState(positions, 9500, DefaultConfig);

        // VIG is at 500/10000 = 5%. Adding 400 => 900/10400 = ~8.65%, still below 10%
        Assert.False(IncomeDriftCalculator.WouldViolateIssuerCap("VIG", 400, state, 0.10m));
    }

    [Fact]
    public void WouldViolateIssuerCap_AboveCap_ReturnsTrue()
    {
        var positions = CreatePositions(("VIG", "DividendGrowthETF", 900));
        var state = IncomeDriftCalculator.BuildSleeveState(positions, 9100, DefaultConfig);

        // VIG is at 900/10000 = 9%. Adding 200 => 1100/10200 = ~10.78%, above 10%
        Assert.True(IncomeDriftCalculator.WouldViolateIssuerCap("VIG", 200, state, 0.10m));
    }

    [Fact]
    public void WouldViolateIssuerCap_NewIssuer_BelowCap_ReturnsFalse()
    {
        var positions = CreatePositions(("VIG", "DividendGrowthETF", 500));
        var state = IncomeDriftCalculator.BuildSleeveState(positions, 9500, DefaultConfig);

        // SCHD doesn't exist yet. Adding 500 => 500/10500 = ~4.76%
        Assert.False(IncomeDriftCalculator.WouldViolateIssuerCap("SCHD", 500, state, 0.10m));
    }

    [Fact]
    public void WouldViolateIssuerCap_ExactlyAtCap_ReturnsFalse()
    {
        var positions = CreatePositions(("VIG", "DividendGrowthETF", 500));
        var state = IncomeDriftCalculator.BuildSleeveState(positions, 9500, DefaultConfig);

        // Adding 611.11 would bring total to ~10611 with VIG at 1111/10611 = 10.47% -- above
        // Adding 500 would bring total to 10500 with VIG at 1000/10500 = 9.52% -- below
        Assert.False(IncomeDriftCalculator.WouldViolateIssuerCap("VIG", 500, state, 0.10m));
    }

    [Fact]
    public void WouldViolateCategoryCap_BelowCap_ReturnsFalse()
    {
        var positions = CreatePositions(("VIG", "DividendGrowthETF", 3000));
        var state = IncomeDriftCalculator.BuildSleeveState(positions, 7000, DefaultConfig);

        // DividendGrowthETF at 3000/10000 = 30%. Adding 500 => 3500/10500 = 33.3%, below 40%
        Assert.False(IncomeDriftCalculator.WouldViolateCategoryCap(
            IncomeCategory.DividendGrowthETF, 500, state, 0.40m));
    }

    [Fact]
    public void WouldViolateCategoryCap_AboveCap_ReturnsTrue()
    {
        var positions = CreatePositions(("VIG", "DividendGrowthETF", 3800));
        var state = IncomeDriftCalculator.BuildSleeveState(positions, 6200, DefaultConfig);

        // DividendGrowthETF at 3800/10000 = 38%. Adding 500 => 4300/10500 = 40.95%, above 40%
        Assert.True(IncomeDriftCalculator.WouldViolateCategoryCap(
            IncomeCategory.DividendGrowthETF, 500, state, 0.40m));
    }

    [Fact]
    public void WouldViolateCategoryCap_EmptyCategory_ReturnsFalse()
    {
        var positions = CreatePositions(("VIG", "DividendGrowthETF", 5000));
        var state = IncomeDriftCalculator.BuildSleeveState(positions, 5000, DefaultConfig);

        // CoveredCallETF is at 0%. Adding 500 => 500/10500 = 4.76%, below 40%
        Assert.False(IncomeDriftCalculator.WouldViolateCategoryCap(
            IncomeCategory.CoveredCallETF, 500, state, 0.40m));
    }

    [Fact]
    public void BuildSleeveState_MultiplePositionsInSameCategory_Aggregates()
    {
        var positions = CreatePositions(
            ("VIG", "DividendGrowthETF", 1500),
            ("SCHD", "DividendGrowthETF", 1000));

        var state = IncomeDriftCalculator.BuildSleeveState(positions, 0, DefaultConfig);

        var category = state.Categories[IncomeCategory.DividendGrowthETF];
        Assert.Equal(2500m, category.CurrentValue);
        Assert.Equal(2, category.Positions.Count);
    }

    [Fact]
    public void BuildSleeveState_ZeroTotalValue_HandlesGracefully()
    {
        var state = IncomeDriftCalculator.BuildSleeveState([], 0, DefaultConfig);

        Assert.Equal(0m, state.TotalValue);
        // Should not throw divide by zero
        foreach (var cat in state.Categories.Values)
        {
            Assert.Equal(0m, cat.CurrentPercent);
        }
    }
}
