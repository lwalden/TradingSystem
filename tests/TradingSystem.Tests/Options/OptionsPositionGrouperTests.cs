using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Strategies.Options;
using Xunit;

namespace TradingSystem.Tests.Options;

public class OptionsPositionGrouperTests
{
    private readonly OptionsPositionGrouper _grouper = new();

    [Fact]
    public void GroupBrokerPositions_BullPutSpread_CreatesGroupedPosition()
    {
        var brokerPositions = new List<Position>
        {
            CreateBrokerOption("SPY_PUT_100", "SPY", 100m, OptionRight.Put, qty: -1, avgCost: 1.50m, marketPrice: 0.90m),
            CreateBrokerOption("SPY_PUT_95", "SPY", 95m, OptionRight.Put, qty: 1, avgCost: 0.50m, marketPrice: 0.40m)
        };

        var grouped = _grouper.GroupBrokerPositions(brokerPositions);

        Assert.Single(grouped);
        var position = grouped[0];
        Assert.Equal(StrategyType.BullPutSpread, position.Strategy);
        Assert.Equal("SPY", position.UnderlyingSymbol);
        Assert.Equal(2, position.Legs.Count);
        Assert.Equal(1.00m, position.EntryNetCredit);
        Assert.Equal(0.50m, position.CurrentValue);
    }

    [Fact]
    public void GroupBrokerPositions_IronCondor_IdentifiesStrategy()
    {
        var brokerPositions = new List<Position>
        {
            CreateBrokerOption("SPY_PUT_390", "SPY", 390m, OptionRight.Put, qty: 1, avgCost: 0.60m, marketPrice: 0.25m),
            CreateBrokerOption("SPY_PUT_395", "SPY", 395m, OptionRight.Put, qty: -1, avgCost: 1.40m, marketPrice: 0.70m),
            CreateBrokerOption("SPY_CALL_430", "SPY", 430m, OptionRight.Call, qty: -1, avgCost: 1.30m, marketPrice: 0.65m),
            CreateBrokerOption("SPY_CALL_435", "SPY", 435m, OptionRight.Call, qty: 1, avgCost: 0.55m, marketPrice: 0.20m)
        };

        var grouped = _grouper.GroupBrokerPositions(brokerPositions);

        Assert.Single(grouped);
        Assert.Equal(StrategyType.IronCondor, grouped[0].Strategy);
        Assert.Equal(4, grouped[0].Legs.Count);
    }

    [Fact]
    public void Reconcile_UpdatesTrackedCurrentPrices()
    {
        var tracked = new OptionsPosition
        {
            Id = "pos-1",
            UnderlyingSymbol = "SPY",
            Strategy = StrategyType.BullPutSpread,
            EntryNetCredit = 1.00m,
            MaxProfit = 1.00m,
            MaxLoss = 4.00m,
            Quantity = 1,
            CurrentValue = 0.90m,
            Status = OptionsPositionStatus.Open,
            Expiration = DateTime.Today.AddDays(20),
            Legs = new List<OptionsPositionLeg>
            {
                new() { Symbol = "SPY_PUT_100", Strike = 100m, Expiration = DateTime.Today.AddDays(20), Right = OptionRight.Put, Action = OrderAction.Sell, CurrentPrice = 1.2m },
                new() { Symbol = "SPY_PUT_95", Strike = 95m, Expiration = DateTime.Today.AddDays(20), Right = OptionRight.Put, Action = OrderAction.Buy, CurrentPrice = 0.3m }
            }
        };

        var brokerPositions = new List<Position>
        {
            CreateBrokerOption("SPY_PUT_100", "SPY", 100m, OptionRight.Put, qty: -1, avgCost: 1.50m, marketPrice: 0.80m),
            CreateBrokerOption("SPY_PUT_95", "SPY", 95m, OptionRight.Put, qty: 1, avgCost: 0.50m, marketPrice: 0.20m)
        };

        var reconciliation = _grouper.Reconcile(new[] { tracked }, brokerPositions);

        Assert.Single(reconciliation.ReconciledTrackedPositions);
        Assert.Equal(0.60m, reconciliation.ReconciledTrackedPositions[0].CurrentValue);
        Assert.Empty(reconciliation.Warnings);
    }

    [Fact]
    public void Reconcile_MissingLeg_AddsWarning()
    {
        var tracked = new OptionsPosition
        {
            Id = "pos-2",
            UnderlyingSymbol = "SPY",
            Strategy = StrategyType.BullPutSpread,
            EntryNetCredit = 1.00m,
            MaxProfit = 1.00m,
            MaxLoss = 4.00m,
            Quantity = 1,
            CurrentValue = 0.90m,
            Status = OptionsPositionStatus.Open,
            Expiration = DateTime.Today.AddDays(20),
            Legs = new List<OptionsPositionLeg>
            {
                new() { Symbol = "SPY_PUT_100", Strike = 100m, Expiration = DateTime.Today.AddDays(20), Right = OptionRight.Put, Action = OrderAction.Sell, CurrentPrice = 1.2m },
                new() { Symbol = "SPY_PUT_95", Strike = 95m, Expiration = DateTime.Today.AddDays(20), Right = OptionRight.Put, Action = OrderAction.Buy, CurrentPrice = 0.3m }
            }
        };

        var brokerPositions = new List<Position>
        {
            CreateBrokerOption("SPY_PUT_100", "SPY", 100m, OptionRight.Put, qty: -1, avgCost: 1.50m, marketPrice: 0.80m)
        };

        var reconciliation = _grouper.Reconcile(new[] { tracked }, brokerPositions);

        Assert.Single(reconciliation.ReconciledTrackedPositions);
        Assert.Contains(reconciliation.Warnings, w => w.Contains("partially matched", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Reconcile_UntrackedBrokerPositions_AreGrouped()
    {
        var brokerPositions = new List<Position>
        {
            CreateBrokerOption("QQQ_PUT_400", "QQQ", 400m, OptionRight.Put, qty: -1, avgCost: 1.40m, marketPrice: 0.75m),
            CreateBrokerOption("QQQ_PUT_395", "QQQ", 395m, OptionRight.Put, qty: 1, avgCost: 0.55m, marketPrice: 0.30m)
        };

        var reconciliation = _grouper.Reconcile(Array.Empty<OptionsPosition>(), brokerPositions);

        Assert.Single(reconciliation.UntrackedBrokerPositions);
        Assert.Contains(reconciliation.Warnings, w => w.Contains("untracked broker options", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IdentifyStrategy_CalendarSpread_Detected()
    {
        var legs = new List<OptionsPositionLeg>
        {
            new() { Strike = 100m, Expiration = DateTime.Today.AddDays(21), Right = OptionRight.Put, Action = OrderAction.Sell },
            new() { Strike = 100m, Expiration = DateTime.Today.AddDays(45), Right = OptionRight.Put, Action = OrderAction.Buy }
        };

        var strategy = OptionsPositionGrouper.IdentifyStrategy(legs);

        Assert.Equal(StrategyType.CalendarSpread, strategy);
    }

    [Fact]
    public void ParseUnderlyingFromOptionSymbol_ExtractsPrefix()
    {
        var underlying = OptionsPositionGrouper.ParseUnderlyingFromSymbol("AAPL260320P00150000");
        Assert.Equal("AAPL", underlying);
    }

    private static Position CreateBrokerOption(
        string symbol,
        string underlying,
        decimal strike,
        OptionRight right,
        decimal qty,
        decimal avgCost,
        decimal marketPrice)
    {
        return new Position
        {
            Symbol = symbol,
            SecurityType = "OPT",
            UnderlyingSymbol = underlying,
            Strike = strike,
            Expiration = DateTime.Today.AddDays(30),
            Right = right,
            Quantity = qty,
            AverageCost = avgCost,
            MarketPrice = marketPrice
        };
    }
}
