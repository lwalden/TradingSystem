using TradingSystem.Brokers.IBKR;
using TradingSystem.Core.Models;
using Xunit;

namespace TradingSystem.Tests.IBKR;

public class IBKROrderFactoryTests
{
    [Fact]
    public void CreateOrder_BuyLimit_MapsCorrectly()
    {
        var order = new Order
        {
            Action = OrderAction.Buy,
            Quantity = 100,
            OrderType = OrderType.Limit,
            LimitPrice = 150.50m,
            TimeInForce = TimeInForce.Day
        };

        var ibOrder = IBKROrderFactory.CreateOrder(order);

        Assert.Equal("BUY", ibOrder.Action);
        Assert.Equal(100m, ibOrder.TotalQuantity);
        Assert.Equal("LMT", ibOrder.OrderType);
        Assert.Equal(150.50, ibOrder.LmtPrice);
        Assert.Equal("DAY", ibOrder.Tif);
    }

    [Fact]
    public void CreateOrder_SellMarket_MapsCorrectly()
    {
        var order = new Order
        {
            Action = OrderAction.Sell,
            Quantity = 50,
            OrderType = OrderType.Market,
            TimeInForce = TimeInForce.Day
        };

        var ibOrder = IBKROrderFactory.CreateOrder(order);

        Assert.Equal("SELL", ibOrder.Action);
        Assert.Equal(50m, ibOrder.TotalQuantity);
        Assert.Equal("MKT", ibOrder.OrderType);
    }

    [Fact]
    public void CreateOrder_StopLimit_MapsBothPrices()
    {
        var order = new Order
        {
            Action = OrderAction.Buy,
            Quantity = 25,
            OrderType = OrderType.StopLimit,
            LimitPrice = 100m,
            StopPrice = 98m,
            TimeInForce = TimeInForce.GTC
        };

        var ibOrder = IBKROrderFactory.CreateOrder(order);

        Assert.Equal("STP LMT", ibOrder.OrderType);
        Assert.Equal(100.0, ibOrder.LmtPrice);
        Assert.Equal(98.0, ibOrder.AuxPrice);
        Assert.Equal("GTC", ibOrder.Tif);
    }

    [Fact]
    public void CreateOrder_SellShort_MapsSshort()
    {
        var order = new Order
        {
            Action = OrderAction.SellShort,
            Quantity = 100,
            OrderType = OrderType.Limit,
            LimitPrice = 200m
        };

        var ibOrder = IBKROrderFactory.CreateOrder(order);

        Assert.Equal("SSHORT", ibOrder.Action);
    }

    [Fact]
    public void CreateOrder_BuyToCover_MapsBuy()
    {
        var order = new Order
        {
            Action = OrderAction.BuyToCover,
            Quantity = 100,
            OrderType = OrderType.Market
        };

        var ibOrder = IBKROrderFactory.CreateOrder(order);

        Assert.Equal("BUY", ibOrder.Action);
    }

    [Theory]
    [InlineData(OrderType.Market, "MKT")]
    [InlineData(OrderType.Limit, "LMT")]
    [InlineData(OrderType.Stop, "STP")]
    [InlineData(OrderType.StopLimit, "STP LMT")]
    [InlineData(OrderType.TrailingStop, "TRAIL")]
    public void CreateOrder_AllOrderTypes_MapCorrectly(OrderType domainType, string ibkrType)
    {
        var order = new Order
        {
            Action = OrderAction.Buy,
            Quantity = 1,
            OrderType = domainType,
            LimitPrice = 100m,
            StopPrice = 90m
        };

        var ibOrder = IBKROrderFactory.CreateOrder(order);

        Assert.Equal(ibkrType, ibOrder.OrderType);
    }

    [Theory]
    [InlineData(TimeInForce.Day, "DAY")]
    [InlineData(TimeInForce.GTC, "GTC")]
    [InlineData(TimeInForce.IOC, "IOC")]
    [InlineData(TimeInForce.FOK, "FOK")]
    public void CreateOrder_AllTimeInForce_MapCorrectly(TimeInForce domainTif, string ibkrTif)
    {
        var order = new Order
        {
            Action = OrderAction.Buy,
            Quantity = 1,
            OrderType = OrderType.Market,
            TimeInForce = domainTif
        };

        var ibOrder = IBKROrderFactory.CreateOrder(order);

        Assert.Equal(ibkrTif, ibOrder.Tif);
    }

    [Fact]
    public void CreateOrder_NoLimitPrice_DoesNotSetLmtPrice()
    {
        var order = new Order
        {
            Action = OrderAction.Buy,
            Quantity = 100,
            OrderType = OrderType.Market
        };

        var ibOrder = IBKROrderFactory.CreateOrder(order);

        Assert.Equal(double.MaxValue, ibOrder.LmtPrice); // IBKR default = not set
    }

    [Fact]
    public void CreateOrder_SetsQuantity()
    {
        var order = new Order
        {
            Action = OrderAction.Buy,
            Quantity = 42,
            OrderType = OrderType.Market
        };

        var ibOrder = IBKROrderFactory.CreateOrder(order);

        Assert.Equal(42m, ibOrder.TotalQuantity);
    }
}
