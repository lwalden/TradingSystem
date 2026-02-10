using TradingSystem.Brokers.IBKR;
using TradingSystem.Core.Models;
using Xunit;

namespace TradingSystem.Tests.IBKR;

public class IBKRMappingTests
{
    [Fact]
    public void ToAccount_MapsAllFields()
    {
        var summary = new AccountSummaryResult
        {
            AccountId = "DU12345",
            NetLiquidation = 100000m,
            TotalCashValue = 50000m,
            BuyingPower = 200000m,
            GrossPositionValue = 50000m,
            MaintMarginReq = 10000m,
            InitMarginReq = 15000m,
            AvailableFunds = 85000m
        };

        var account = summary.ToAccount();

        Assert.Equal("DU12345", account.AccountId);
        Assert.Equal(100000m, account.NetLiquidationValue);
        Assert.Equal(50000m, account.TotalCashValue);
        Assert.Equal(200000m, account.BuyingPower);
        Assert.Equal(50000m, account.GrossPositionValue);
        Assert.Equal(10000m, account.MaintenanceMargin);
        Assert.Equal(15000m, account.InitialMargin);
        Assert.Equal(85000m, account.AvailableFunds);
        Assert.True((DateTime.UtcNow - account.LastUpdated).TotalSeconds < 5);
    }

    [Fact]
    public void ToPosition_MapsStockPosition()
    {
        var posData = new PositionData
        {
            Account = "DU12345",
            Symbol = "AAPL",
            SecType = "STK",
            Quantity = 100m,
            AverageCost = 150.50
        };

        var position = posData.ToPosition();

        Assert.Equal("AAPL", position.Symbol);
        Assert.Equal("STK", position.SecurityType);
        Assert.Equal(100m, position.Quantity);
        Assert.Equal(150.50m, position.AverageCost);
        Assert.Null(position.Strike);
        Assert.Null(position.Right);
        Assert.Null(position.Expiration);
    }

    [Fact]
    public void ToPosition_MapsOptionPosition()
    {
        var posData = new PositionData
        {
            Account = "DU12345",
            Symbol = "AAPL",
            SecType = "OPT",
            Quantity = -5m,
            AverageCost = 3.50,
            Strike = 150m,
            LastTradeDateOrContractMonth = "20250321",
            Right = "C",
            UnderlyingSymbol = "AAPL"
        };

        var position = posData.ToPosition();

        Assert.Equal("AAPL", position.Symbol);
        Assert.Equal("OPT", position.SecurityType);
        Assert.Equal(-5m, position.Quantity);
        Assert.Equal(3.50m, position.AverageCost);
        Assert.Equal(150m, position.Strike);
        Assert.Equal(OptionRight.Call, position.Right);
        Assert.Equal(new DateTime(2025, 3, 21), position.Expiration);
        Assert.Equal("AAPL", position.UnderlyingSymbol);
    }

    [Fact]
    public void ToPosition_MapsPutOption()
    {
        var posData = new PositionData
        {
            Symbol = "SPY",
            SecType = "OPT",
            Quantity = 2m,
            AverageCost = 5.00,
            Strike = 450m,
            LastTradeDateOrContractMonth = "20250620",
            Right = "P",
            UnderlyingSymbol = "SPY"
        };

        var position = posData.ToPosition();

        Assert.Equal(OptionRight.Put, position.Right);
    }

    [Fact]
    public void ToQuote_MapsAllFields()
    {
        var quoteData = new QuoteData
        {
            Bid = 150.00m,
            Ask = 150.05m,
            Last = 150.02m,
            BidSize = 100m,
            AskSize = 200m,
            Volume = 5000000,
            High = 151.00m,
            Low = 149.50m,
            Close = 149.75m
        };

        var quote = quoteData.ToQuote("AAPL");

        Assert.Equal("AAPL", quote.Symbol);
        Assert.Equal(150.00m, quote.Bid);
        Assert.Equal(150.05m, quote.Ask);
        Assert.Equal(150.02m, quote.Last);
        Assert.Equal(100, quote.BidSize);
        Assert.Equal(200, quote.AskSize);
        Assert.Equal(5000000, quote.Volume);
    }

    [Fact]
    public void ToPriceBar_MapsCorrectly()
    {
        var bar = new IBApi.Bar("20250115", 150.0, 155.0, 149.0, 153.0, 10000000m, 50000, 152.0m);

        var priceBar = bar.ToPriceBar("AAPL", BarTimeframe.Daily);

        Assert.Equal("AAPL", priceBar.Symbol);
        Assert.Equal(new DateTime(2025, 1, 15), priceBar.Timestamp);
        Assert.Equal(150.0m, priceBar.Open);
        Assert.Equal(155.0m, priceBar.High);
        Assert.Equal(149.0m, priceBar.Low);
        Assert.Equal(153.0m, priceBar.Close);
        Assert.Equal(10000000L, priceBar.Volume);
        Assert.Equal(BarTimeframe.Daily, priceBar.Timeframe);
    }

    [Fact]
    public void ToPriceBar_ParsesDateTimeFormat()
    {
        var bar = new IBApi.Bar("20250115 14:30:00", 150.0, 155.0, 149.0, 153.0, 1000m, 500, 152.0m);

        var priceBar = bar.ToPriceBar("AAPL", BarTimeframe.Minute5);

        Assert.Equal(new DateTime(2025, 1, 15, 14, 30, 0), priceBar.Timestamp);
    }

    [Fact]
    public void ToPriceBar_ParsesUnixTimestamp()
    {
        // 1705334400 = 2024-01-15 16:00:00 UTC
        var bar = new IBApi.Bar("1705334400", 150.0, 155.0, 149.0, 153.0, 1000m, 500, 152.0m);

        var priceBar = bar.ToPriceBar("AAPL", BarTimeframe.Minute1);

        Assert.Equal(2024, priceBar.Timestamp.Year);
    }

    [Theory]
    [InlineData(BarTimeframe.Minute1, "1 min")]
    [InlineData(BarTimeframe.Minute5, "5 mins")]
    [InlineData(BarTimeframe.Minute15, "15 mins")]
    [InlineData(BarTimeframe.Minute30, "30 mins")]
    [InlineData(BarTimeframe.Hour1, "1 hour")]
    [InlineData(BarTimeframe.Hour4, "4 hours")]
    [InlineData(BarTimeframe.Daily, "1 day")]
    [InlineData(BarTimeframe.Weekly, "1 week")]
    [InlineData(BarTimeframe.Monthly, "1 month")]
    public void ToIBKRBarSize_ReturnsCorrectString(BarTimeframe timeframe, string expected)
    {
        Assert.Equal(expected, timeframe.ToIBKRBarSize());
    }

    [Fact]
    public void ToIBKRDuration_ShortPeriod_ReturnsDays()
    {
        var start = new DateTime(2025, 1, 1);
        var end = new DateTime(2025, 1, 31);

        var duration = IBKRMappingExtensions.ToIBKRDuration(start, end);

        Assert.Equal("30 D", duration);
    }

    [Fact]
    public void ToIBKRDuration_LongPeriod_ReturnsYears()
    {
        var start = new DateTime(2023, 1, 1);
        var end = new DateTime(2025, 1, 1);

        // 731 days -> rounds up to 3 years with formula (days + 364) / 365
        var duration = IBKRMappingExtensions.ToIBKRDuration(start, end);

        Assert.Equal("3 Y", duration);
    }

    [Fact]
    public void ToIBKRDuration_ZeroDays_ReturnsOneDay()
    {
        var date = new DateTime(2025, 1, 15);

        var duration = IBKRMappingExtensions.ToIBKRDuration(date, date);

        Assert.Equal("1 D", duration);
    }
}
