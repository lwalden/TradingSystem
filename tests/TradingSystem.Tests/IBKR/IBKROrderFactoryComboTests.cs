using TradingSystem.Brokers.IBKR;
using TradingSystem.Core.Models;
using Xunit;
using Order = TradingSystem.Core.Models.Order;

namespace TradingSystem.Tests.IBKR;

public class IBKROrderFactoryComboTests
{
    [Fact]
    public void CreateComboOrder_SetsOrderTypeToLMT()
    {
        var order = CreateTestComboOrder();
        var ibOrder = IBKROrderFactory.CreateComboOrder(order);

        Assert.Equal("LMT", ibOrder.OrderType);
    }

    [Fact]
    public void CreateComboOrder_SetsLimitPriceFromNetLimitPrice()
    {
        var order = CreateTestComboOrder(netLimitPrice: 0.85m);
        var ibOrder = IBKROrderFactory.CreateComboOrder(order);

        Assert.Equal(0.85, ibOrder.LmtPrice, 2);
    }

    [Fact]
    public void CreateComboOrder_SetsActionFromDomainOrder()
    {
        var order = CreateTestComboOrder();
        order.Action = OrderAction.Buy;
        var ibOrder = IBKROrderFactory.CreateComboOrder(order);

        Assert.Equal("BUY", ibOrder.Action);
    }

    [Fact]
    public void CreateComboOrder_SetsSellAction()
    {
        var order = CreateTestComboOrder();
        order.Action = OrderAction.Sell;
        var ibOrder = IBKROrderFactory.CreateComboOrder(order);

        Assert.Equal("SELL", ibOrder.Action);
    }

    [Fact]
    public void CreateComboOrder_SetsQuantity()
    {
        var order = CreateTestComboOrder();
        order.Quantity = 5;
        var ibOrder = IBKROrderFactory.CreateComboOrder(order);

        Assert.Equal(5m, ibOrder.TotalQuantity);
    }

    [Fact]
    public void CreateComboOrder_SetsNonGuaranteedRouting()
    {
        var order = CreateTestComboOrder();
        var ibOrder = IBKROrderFactory.CreateComboOrder(order);

        Assert.NotNull(ibOrder.SmartComboRoutingParams);
        Assert.Single(ibOrder.SmartComboRoutingParams);
        Assert.Equal("NonGuaranteed", ibOrder.SmartComboRoutingParams[0].Tag);
        Assert.Equal("1", ibOrder.SmartComboRoutingParams[0].Value);
    }

    [Fact]
    public void CreateComboOrder_DefaultTimeInForceIsDAY()
    {
        var order = CreateTestComboOrder();
        var ibOrder = IBKROrderFactory.CreateComboOrder(order);

        Assert.Equal("DAY", ibOrder.Tif);
    }

    [Fact]
    public void CreateComboOrder_SetsGTCTimeInForce()
    {
        var order = CreateTestComboOrder();
        order.TimeInForce = TimeInForce.GTC;
        var ibOrder = IBKROrderFactory.CreateComboOrder(order);

        Assert.Equal("GTC", ibOrder.Tif);
    }

    [Fact]
    public void CreateComboOrder_NullNetLimitPrice_SetsZero()
    {
        var order = CreateTestComboOrder(netLimitPrice: null);
        var ibOrder = IBKROrderFactory.CreateComboOrder(order);

        Assert.Equal(0.0, ibOrder.LmtPrice);
    }

    [Fact]
    public void CreateComboOrder_NegativeNetLimitPrice_ForDebitSpread()
    {
        var order = CreateTestComboOrder(netLimitPrice: -1.50m);
        var ibOrder = IBKROrderFactory.CreateComboOrder(order);

        Assert.Equal(-1.50, ibOrder.LmtPrice, 2);
    }

    private static Order CreateTestComboOrder(decimal? netLimitPrice = 0.85m)
    {
        return new Order
        {
            Symbol = "SPY",
            SecurityType = "BAG",
            Action = OrderAction.Buy,
            Quantity = 1,
            NetLimitPrice = netLimitPrice,
            TimeInForce = TimeInForce.Day,
            Legs = new List<OptionLeg>
            {
                new()
                {
                    UnderlyingSymbol = "SPY",
                    Strike = 580m,
                    Expiration = new DateTime(2026, 3, 20),
                    Right = OptionRight.Put,
                    Action = OrderAction.Sell,
                    Quantity = 1
                },
                new()
                {
                    UnderlyingSymbol = "SPY",
                    Strike = 575m,
                    Expiration = new DateTime(2026, 3, 20),
                    Right = OptionRight.Put,
                    Action = OrderAction.Buy,
                    Quantity = 1
                }
            }
        };
    }
}
