using TradingSystem.Brokers.IBKR;
using TradingSystem.Core.Models;
using Xunit;

namespace TradingSystem.Tests.IBKR;

public class IBKRContractFactoryComboTests
{
    [Fact]
    public void CreateCombo_SetsSecTypeToBAG()
    {
        var legs = CreateBullPutSpreadLegs();
        var contract = IBKRContractFactory.CreateCombo("SPY", legs);

        Assert.Equal("BAG", contract.SecType);
    }

    [Fact]
    public void CreateCombo_SetsSymbolToUnderlying()
    {
        var legs = CreateBullPutSpreadLegs();
        var contract = IBKRContractFactory.CreateCombo("SPY", legs);

        Assert.Equal("SPY", contract.Symbol);
    }

    [Fact]
    public void CreateCombo_SetsCurrencyToUSD()
    {
        var legs = CreateBullPutSpreadLegs();
        var contract = IBKRContractFactory.CreateCombo("SPY", legs);

        Assert.Equal("USD", contract.Currency);
    }

    [Fact]
    public void CreateCombo_SetsExchangeToSMART()
    {
        var legs = CreateBullPutSpreadLegs();
        var contract = IBKRContractFactory.CreateCombo("SPY", legs);

        Assert.Equal("SMART", contract.Exchange);
    }

    [Fact]
    public void CreateCombo_CreatesCorrectNumberOfComboLegs()
    {
        var legs = CreateBullPutSpreadLegs();
        var contract = IBKRContractFactory.CreateCombo("SPY", legs);

        Assert.Equal(2, contract.ComboLegs.Count);
    }

    [Fact]
    public void CreateCombo_SetsConIdOnComboLegs()
    {
        var legs = CreateBullPutSpreadLegs();
        var contract = IBKRContractFactory.CreateCombo("SPY", legs);

        Assert.Equal(100001, contract.ComboLegs[0].ConId);
        Assert.Equal(100002, contract.ComboLegs[1].ConId);
    }

    [Fact]
    public void CreateCombo_MapsActionCorrectly_SellLeg()
    {
        var legs = CreateBullPutSpreadLegs();
        var contract = IBKRContractFactory.CreateCombo("SPY", legs);

        // First leg: short put (Sell)
        Assert.Equal("SELL", contract.ComboLegs[0].Action);
    }

    [Fact]
    public void CreateCombo_MapsActionCorrectly_BuyLeg()
    {
        var legs = CreateBullPutSpreadLegs();
        var contract = IBKRContractFactory.CreateCombo("SPY", legs);

        // Second leg: long put (Buy)
        Assert.Equal("BUY", contract.ComboLegs[1].Action);
    }

    [Fact]
    public void CreateCombo_SetsRatioOnComboLegs()
    {
        var legs = CreateBullPutSpreadLegs();
        var contract = IBKRContractFactory.CreateCombo("SPY", legs);

        Assert.Equal(1, contract.ComboLegs[0].Ratio);
        Assert.Equal(1, contract.ComboLegs[1].Ratio);
    }

    [Fact]
    public void CreateCombo_SetsExchangeOnEachComboLeg()
    {
        var legs = CreateBullPutSpreadLegs();
        var contract = IBKRContractFactory.CreateCombo("SPY", legs);

        Assert.All(contract.ComboLegs, leg => Assert.Equal("SMART", leg.Exchange));
    }

    [Fact]
    public void CreateCombo_FourLeg_IronCondor()
    {
        var legs = new List<ComboLegInfo>
        {
            new() { ConId = 200001, Action = OrderAction.Sell, Ratio = 1 }, // Short put
            new() { ConId = 200002, Action = OrderAction.Buy, Ratio = 1 },  // Long put
            new() { ConId = 200003, Action = OrderAction.Sell, Ratio = 1 }, // Short call
            new() { ConId = 200004, Action = OrderAction.Buy, Ratio = 1 },  // Long call
        };

        var contract = IBKRContractFactory.CreateCombo("SPY", legs);

        Assert.Equal("BAG", contract.SecType);
        Assert.Equal(4, contract.ComboLegs.Count);
        Assert.Equal("SELL", contract.ComboLegs[0].Action);
        Assert.Equal("BUY", contract.ComboLegs[1].Action);
        Assert.Equal("SELL", contract.ComboLegs[2].Action);
        Assert.Equal("BUY", contract.ComboLegs[3].Action);
    }

    [Fact]
    public void CreateCombo_CustomRatio()
    {
        var legs = new List<ComboLegInfo>
        {
            new() { ConId = 300001, Action = OrderAction.Sell, Ratio = 2 },
            new() { ConId = 300002, Action = OrderAction.Buy, Ratio = 1 },
        };

        var contract = IBKRContractFactory.CreateCombo("AAPL", legs);

        Assert.Equal(2, contract.ComboLegs[0].Ratio);
        Assert.Equal(1, contract.ComboLegs[1].Ratio);
    }

    [Fact]
    public void CreateCombo_ThrowsOnNullLegs()
    {
        Assert.Throws<ArgumentException>(() => IBKRContractFactory.CreateCombo("SPY", null!));
    }

    [Fact]
    public void CreateCombo_ThrowsOnEmptyLegs()
    {
        Assert.Throws<ArgumentException>(() => IBKRContractFactory.CreateCombo("SPY", new List<ComboLegInfo>()));
    }

    private static List<ComboLegInfo> CreateBullPutSpreadLegs()
    {
        return new List<ComboLegInfo>
        {
            new() { ConId = 100001, Action = OrderAction.Sell, Ratio = 1 }, // Short put (higher strike)
            new() { ConId = 100002, Action = OrderAction.Buy, Ratio = 1 },  // Long put (lower strike)
        };
    }
}
