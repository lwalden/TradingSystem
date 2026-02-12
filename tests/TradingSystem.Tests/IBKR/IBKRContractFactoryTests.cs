using TradingSystem.Brokers.IBKR;
using TradingSystem.Core.Models;
using Xunit;

namespace TradingSystem.Tests.IBKR;

public class IBKRContractFactoryTests
{
    [Theory]
    [InlineData("VIX")]
    [InlineData("SPX")]
    [InlineData("NDX")]
    [InlineData("RUT")]
    [InlineData("DJX")]
    [InlineData("OEX")]
    public void IsIndex_ReturnsTrue_ForKnownIndices(string symbol)
    {
        Assert.True(IBKRContractFactory.IsIndex(symbol));
    }

    [Theory]
    [InlineData("vix")]
    [InlineData("Vix")]
    [InlineData("spx")]
    public void IsIndex_IsCaseInsensitive(string symbol)
    {
        Assert.True(IBKRContractFactory.IsIndex(symbol));
    }

    [Theory]
    [InlineData("AAPL")]
    [InlineData("SPY")]
    [InlineData("QQQ")]
    [InlineData("MSFT")]
    public void IsIndex_ReturnsFalse_ForStocks(string symbol)
    {
        Assert.False(IBKRContractFactory.IsIndex(symbol));
    }

    [Fact]
    public void CreateIndex_SetsCorrectContractFields()
    {
        var contract = IBKRContractFactory.CreateIndex("VIX");

        Assert.Equal("VIX", contract.Symbol);
        Assert.Equal("IND", contract.SecType);
        Assert.Equal("CBOE", contract.Exchange);
        Assert.Equal("USD", contract.Currency);
    }

    [Fact]
    public void CreateStock_SetsCorrectContractFields()
    {
        var contract = IBKRContractFactory.CreateStock("AAPL");

        Assert.Equal("AAPL", contract.Symbol);
        Assert.Equal("STK", contract.SecType);
        Assert.Equal("SMART", contract.Exchange);
        Assert.Equal("USD", contract.Currency);
    }

    [Fact]
    public void CreateEquity_ReturnsIndex_ForVIX()
    {
        var contract = IBKRContractFactory.CreateEquity("VIX");

        Assert.Equal("IND", contract.SecType);
        Assert.Equal("CBOE", contract.Exchange);
    }

    [Fact]
    public void CreateEquity_ReturnsStock_ForAAPL()
    {
        var contract = IBKRContractFactory.CreateEquity("AAPL");

        Assert.Equal("STK", contract.SecType);
        Assert.Equal("SMART", contract.Exchange);
    }

    [Fact]
    public void CreateOption_SetsCorrectFields()
    {
        var expiration = new DateTime(2026, 3, 20);
        var contract = IBKRContractFactory.CreateOption("SPY", 600m, expiration, OptionRight.Put);

        Assert.Equal("SPY", contract.Symbol);
        Assert.Equal("OPT", contract.SecType);
        Assert.Equal("SMART", contract.Exchange);
        Assert.Equal("USD", contract.Currency);
        Assert.Equal(600.0, contract.Strike);
        Assert.Equal("20260320", contract.LastTradeDateOrContractMonth);
        Assert.Equal("P", contract.Right);
        Assert.Equal("100", contract.Multiplier);
    }
}
