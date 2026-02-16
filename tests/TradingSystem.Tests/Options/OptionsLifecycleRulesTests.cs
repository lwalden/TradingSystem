using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Strategies.Options;
using Xunit;

namespace TradingSystem.Tests.Options;

public class OptionsLifecycleRulesTests
{
    private readonly OptionsLifecycleRules _rules;

    public OptionsLifecycleRulesTests()
    {
        _rules = new OptionsLifecycleRules(new OptionsConfig
        {
            ProfitTakeMin = 0.50m,
            ProfitTakeMax = 0.75m,
            StopMultipleCredit = 2.0m,
            RollDTEThreshold = 7,
            CloseDTEThreshold = 3
        });
    }

    [Fact]
    public void Evaluate_NoTrigger_ReturnsHold()
    {
        var position = CreatePosition(currentValue: 0.90m, dte: 20);

        var decision = _rules.Evaluate(position);

        Assert.Equal(OptionsLifecycleAction.Hold, decision.Action);
        Assert.False(decision.ShouldClose);
    }

    [Fact]
    public void Evaluate_ProfitTakeMinHit_ReturnsTakeProfit()
    {
        var position = CreatePosition(currentValue: 0.40m, dte: 25); // 60% of max profit

        var decision = _rules.Evaluate(position);

        Assert.Equal(OptionsLifecycleAction.TakeProfit, decision.Action);
        Assert.True(decision.ShouldClose);
    }

    [Fact]
    public void Evaluate_ProfitTakeMaxHit_ReturnsMandatoryTakeProfit()
    {
        var position = CreatePosition(currentValue: 0.20m, dte: 25); // 80% of max profit

        var decision = _rules.Evaluate(position);

        Assert.Equal(OptionsLifecycleAction.MandatoryTakeProfit, decision.Action);
    }

    [Fact]
    public void Evaluate_StopLossHit_ReturnsStopOut()
    {
        var position = CreatePosition(currentValue: 3.10m, dte: 25); // -$210 vs $200 stop threshold

        var decision = _rules.Evaluate(position);

        Assert.Equal(OptionsLifecycleAction.StopOut, decision.Action);
    }

    [Fact]
    public void Evaluate_NearExpirationAndProfitable_ReturnsCloseNearExpiration()
    {
        var position = CreatePosition(currentValue: 0.30m, dte: 2);

        var decision = _rules.Evaluate(position);

        Assert.Equal(OptionsLifecycleAction.CloseNearExpiration, decision.Action);
    }

    [Fact]
    public void Evaluate_RollWindowAndProfitable_ReturnsRoll()
    {
        var position = CreatePosition(currentValue: 0.80m, dte: 6); // profitable but below take-profit threshold

        var decision = _rules.Evaluate(position);

        Assert.Equal(OptionsLifecycleAction.Roll, decision.Action);
        Assert.True(decision.ShouldOpenReplacement);
    }

    [Fact]
    public void Evaluate_RollWindowButNotProfitable_DoesNotRoll()
    {
        var position = CreatePosition(currentValue: 1.05m, dte: 6);

        var decision = _rules.Evaluate(position);

        Assert.Equal(OptionsLifecycleAction.Hold, decision.Action);
    }

    [Fact]
    public void Evaluate_NonOpenStatus_ReturnsHold()
    {
        var position = CreatePosition(currentValue: 0.40m, dte: 20);
        position.Status = OptionsPositionStatus.RollPending;

        var decision = _rules.Evaluate(position);

        Assert.Equal(OptionsLifecycleAction.Hold, decision.Action);
    }

    private static OptionsPosition CreatePosition(decimal currentValue, int dte)
    {
        return new OptionsPosition
        {
            UnderlyingSymbol = "SPY",
            Strategy = StrategyType.BullPutSpread,
            EntryNetCredit = 1.00m,
            MaxProfit = 1.00m,
            MaxLoss = 4.00m,
            CurrentValue = currentValue,
            Quantity = 1,
            Status = OptionsPositionStatus.Open,
            Expiration = DateTime.Today.AddDays(dte),
            Legs = new List<OptionsPositionLeg>
            {
                new() { Symbol = "SPY_PUT_100", Strike = 100m, Expiration = DateTime.Today.AddDays(dte), Right = OptionRight.Put, Action = OrderAction.Sell, CurrentPrice = currentValue + 0.5m },
                new() { Symbol = "SPY_PUT_95", Strike = 95m, Expiration = DateTime.Today.AddDays(dte), Right = OptionRight.Put, Action = OrderAction.Buy, CurrentPrice = 0.5m }
            }
        };
    }
}
