using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Strategies.Options;
using Xunit;

namespace TradingSystem.Tests.Options;

public class OptionsCandidateConverterTests
{
    private readonly OptionsCandidateConverter _converter = new();

    [Fact]
    public void ConvertToEntrySignal_MapsCoreFields()
    {
        var candidate = CreateBullPutCandidate();

        var signal = _converter.ConvertToEntrySignal(candidate, contracts: 2, now: new DateTime(2026, 2, 16, 14, 0, 0, DateTimeKind.Utc));

        Assert.Equal("SPY", signal.Symbol);
        Assert.Equal("BAG", signal.SecurityType);
        Assert.Equal("options-bull-put-spread", signal.StrategyId);
        Assert.Equal(SignalDirection.Short, signal.Direction);
        Assert.Equal(2m, signal.SuggestedPositionSize);
        Assert.Equal(candidate.NetCredit, signal.SuggestedEntryPrice);
        Assert.Equal(candidate.MaxLoss * 2, signal.SuggestedRiskAmount);
        Assert.Equal(candidate.ProbabilityOfProfit, signal.ProbabilityOfProfit);
        Assert.Equal(candidate.IVRank, signal.IVRank);
        Assert.Equal(candidate.IVPercentile, signal.IVPercentile);
        Assert.Equal(2, signal.SuggestedLegs!.Count);
    }

    [Fact]
    public void ConvertToCloseSignal_ReversesLegActionsAndAddsPositionReference()
    {
        var decision = new OptionsLifecycleDecision(OptionsLifecycleAction.TakeProfit, "Target reached");
        var position = new OptionsPosition
        {
            Id = "pos-1",
            UnderlyingSymbol = "SPY",
            Strategy = StrategyType.BullPutSpread,
            Quantity = 1,
            CurrentValue = 0.45m,
            Expiration = DateTime.Today.AddDays(10),
            Legs = new List<OptionsPositionLeg>
            {
                new() { Symbol = "SPY_PUT_100", Strike = 100m, Expiration = DateTime.Today.AddDays(10), Right = OptionRight.Put, Action = OrderAction.Sell, Quantity = 1 },
                new() { Symbol = "SPY_PUT_95", Strike = 95m, Expiration = DateTime.Today.AddDays(10), Right = OptionRight.Put, Action = OrderAction.Buy, Quantity = 1 }
            }
        };

        var signal = _converter.ConvertToCloseSignal(position, decision);

        Assert.Equal(SignalDirection.ClosePosition, signal.Direction);
        Assert.Equal("options-bull-put-spread-close", signal.StrategyId);
        Assert.Equal("pos-1", signal.Indicators["positionId"]);
        Assert.Equal("TakeProfit", signal.Indicators["lifecycleAction"]);
        Assert.Equal(2, signal.SuggestedLegs!.Count);
        Assert.Equal(OrderAction.Buy, signal.SuggestedLegs[0].Action);
        Assert.Equal(OrderAction.Sell, signal.SuggestedLegs[1].Action);
    }

    [Fact]
    public void ConvertToRollSignals_ReturnsCloseAndOpenPair()
    {
        var currentPosition = new OptionsPosition
        {
            Id = "pos-7",
            UnderlyingSymbol = "SPY",
            Strategy = StrategyType.BullPutSpread,
            Quantity = 1,
            CurrentValue = 0.60m,
            Expiration = DateTime.Today.AddDays(5),
            Legs = new List<OptionsPositionLeg>
            {
                new() { Symbol = "SPY_PUT_100", Strike = 100m, Expiration = DateTime.Today.AddDays(5), Right = OptionRight.Put, Action = OrderAction.Sell, Quantity = 1 },
                new() { Symbol = "SPY_PUT_95", Strike = 95m, Expiration = DateTime.Today.AddDays(5), Right = OptionRight.Put, Action = OrderAction.Buy, Quantity = 1 }
            }
        };

        var replacement = CreateBullPutCandidate();
        var signals = _converter.ConvertToRollSignals(currentPosition, replacement, replacementContracts: 1);

        Assert.Equal(2, signals.Count);
        Assert.Equal(SignalDirection.ClosePosition, signals[0].Direction);
        Assert.Equal("RollOpen", signals[1].SetupType);
        Assert.Equal("pos-7", signals[1].Indicators["rollFromPositionId"]);
    }

    [Theory]
    [InlineData(90, SignalStrength.VeryStrong)]
    [InlineData(72, SignalStrength.Strong)]
    [InlineData(58, SignalStrength.Moderate)]
    [InlineData(40, SignalStrength.Weak)]
    public void ScoreToStrength_MapsExpectedBands(decimal score, SignalStrength expected)
    {
        var actual = OptionsCandidateConverter.ScoreToStrength(score);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(StrategyType.CashSecuredPut, "options-csp")]
    [InlineData(StrategyType.IronCondor, "options-iron-condor")]
    [InlineData(StrategyType.CalendarSpread, "options-calendar-spread")]
    public void StrategyToId_ReturnsExpectedSlug(StrategyType strategy, string expected)
    {
        Assert.Equal(expected, OptionsCandidateConverter.StrategyToId(strategy));
    }

    private static OptionCandidate CreateBullPutCandidate()
    {
        return new OptionCandidate
        {
            UnderlyingSymbol = "SPY",
            Strategy = StrategyType.BullPutSpread,
            MaxProfit = 100m,
            MaxLoss = 350m,
            NetCredit = 1.00m,
            ProbabilityOfProfit = 72m,
            IVRank = 61m,
            IVPercentile = 68m,
            Score = 78m,
            ScoreBreakdown = "test-score",
            UnderlyingPrice = 500m,
            Legs = new List<OptionLeg>
            {
                new() { Symbol = "SPY_PUT_100", UnderlyingSymbol = "SPY", Strike = 100m, Expiration = DateTime.Today.AddDays(30), Right = OptionRight.Put, Action = OrderAction.Sell, Quantity = 1 },
                new() { Symbol = "SPY_PUT_95", UnderlyingSymbol = "SPY", Strike = 95m, Expiration = DateTime.Today.AddDays(30), Right = OptionRight.Put, Action = OrderAction.Buy, Quantity = 1 }
            }
        };
    }
}
