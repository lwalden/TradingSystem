using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Strategies.Options;
using Xunit;

namespace TradingSystem.Tests.Options;

public class OptionsPositionSizerTests
{
    private readonly OptionsPositionSizer _sizer;

    public OptionsPositionSizerTests()
    {
        _sizer = new OptionsPositionSizer(new RiskConfig
        {
            RiskPerTradePercent = 0.004m,    // $400 risk on $100k
            MaxSingleSpreadPercent = 0.02m   // $2,000 max single spread exposure
        });
    }

    [Fact]
    public void CalculateContracts_RespectsRiskBudget()
    {
        var candidate = CreateCandidate(maxLoss: 350m);

        var result = _sizer.CalculateContracts(candidate, 100_000m);

        Assert.Equal(1, result.Contracts); // 400 / 350 = 1
        Assert.Equal("risk-budget", result.LimitedBy);
    }

    [Fact]
    public void CalculateContracts_ZeroWhenPerContractRiskTooHigh()
    {
        var candidate = CreateCandidate(maxLoss: 3_500m);

        var result = _sizer.CalculateContracts(candidate, 100_000m);

        Assert.Equal(0, result.Contracts);
    }

    [Fact]
    public void CalculateContracts_AppliesAvailableCapitalLimit()
    {
        var candidate = CreateCandidate(maxLoss: 250m);

        var result = _sizer.CalculateContracts(candidate, 100_000m, availableCapital: 300m);

        Assert.Equal(1, result.Contracts); // capital limits size to 1
        Assert.Contains(result.LimitedBy, new[] { "available-capital", "risk-budget" });
    }

    [Fact]
    public void CalculateContracts_UsesMaxLossAbsoluteValue()
    {
        var candidate = CreateCandidate(maxLoss: -200m);

        var result = _sizer.CalculateContracts(candidate, 100_000m);

        Assert.Equal(2, result.Contracts); // 400 / 200 = 2
    }

    [Fact]
    public void GetPerContractRisk_WhenMaxLossMissing_UsesSpreadMath()
    {
        var candidate = new OptionCandidate
        {
            UnderlyingSymbol = "SPY",
            Strategy = StrategyType.BullPutSpread,
            MaxLoss = 0m,
            NetCredit = 1.50m,
            Legs = new List<OptionLeg>
            {
                new() { Strike = 100m, Right = OptionRight.Put, Action = OrderAction.Sell },
                new() { Strike = 95m, Right = OptionRight.Put, Action = OrderAction.Buy }
            }
        };

        var risk = OptionsPositionSizer.GetPerContractRisk(candidate);

        Assert.Equal(350m, risk); // (5 - 1.5) * 100
    }

    [Fact]
    public void CalculateContracts_InvalidInputs_ReturnsZero()
    {
        var candidate = CreateCandidate(maxLoss: 0m);

        var result = _sizer.CalculateContracts(candidate, 0m);

        Assert.Equal(0, result.Contracts);
        Assert.Equal("invalid-risk-input", result.LimitedBy);
    }

    private static OptionCandidate CreateCandidate(decimal maxLoss)
    {
        return new OptionCandidate
        {
            UnderlyingSymbol = "SPY",
            Strategy = StrategyType.BullPutSpread,
            MaxProfit = 100m,
            MaxLoss = maxLoss,
            NetCredit = 1.00m,
            Legs = new List<OptionLeg>
            {
                new() { Strike = 100m, Expiration = DateTime.Today.AddDays(30), Right = OptionRight.Put, Action = OrderAction.Sell },
                new() { Strike = 95m, Expiration = DateTime.Today.AddDays(30), Right = OptionRight.Put, Action = OrderAction.Buy }
            }
        };
    }
}
