using System.Text.Json;
using System.Text.Json.Serialization;
using TradingSystem.Core.Models;
using Xunit;

namespace TradingSystem.Tests.Options;

public class OrderComboExtensionTests
{
    [Fact]
    public void Order_NullLegs_WorksForSingleLegOrders()
    {
        var order = new Order
        {
            Symbol = "AAPL",
            Action = OrderAction.Buy,
            Quantity = 100,
            OrderType = OrderType.Limit,
            LimitPrice = 175.00m
        };

        Assert.Null(order.Legs);
        Assert.Null(order.NetLimitPrice);
    }

    [Fact]
    public void Order_WithLegs_StoresMultipleLeg()
    {
        var order = new Order
        {
            Symbol = "SPY",
            SecurityType = "BAG",
            Action = OrderAction.Buy,
            Quantity = 1,
            NetLimitPrice = 0.85m,
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

        Assert.Equal(2, order.Legs.Count);
        Assert.Equal(0.85m, order.NetLimitPrice);
    }

    [Fact]
    public void Order_SerializationRoundtrip_PreservesLegs()
    {
        var order = new Order
        {
            Symbol = "SPY",
            SecurityType = "BAG",
            Action = OrderAction.Buy,
            Quantity = 2,
            NetLimitPrice = 1.50m,
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

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        var json = JsonSerializer.Serialize(order, options);
        var deserialized = JsonSerializer.Deserialize<Order>(json, options);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized!.Legs);
        Assert.Equal(2, deserialized.Legs!.Count);
        Assert.Equal(580m, deserialized.Legs[0].Strike);
        Assert.Equal(OrderAction.Sell, deserialized.Legs[0].Action);
        Assert.Equal(1.50m, deserialized.NetLimitPrice);
    }

    [Fact]
    public void Order_SerializationRoundtrip_NullLegs_PreservesNull()
    {
        var order = new Order
        {
            Symbol = "AAPL",
            Action = OrderAction.Buy,
            Quantity = 100,
            OrderType = OrderType.Limit,
            LimitPrice = 175.00m
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        var json = JsonSerializer.Serialize(order, options);
        var deserialized = JsonSerializer.Deserialize<Order>(json, options);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized!.Legs);
        Assert.Null(deserialized.NetLimitPrice);
    }

    [Fact]
    public void Order_IronCondor_FourLegs()
    {
        var expiration = new DateTime(2026, 3, 20);
        var order = new Order
        {
            Symbol = "SPY",
            SecurityType = "BAG",
            Action = OrderAction.Buy,
            Quantity = 1,
            NetLimitPrice = 1.20m,
            Legs = new List<OptionLeg>
            {
                new() { UnderlyingSymbol = "SPY", Strike = 575m, Expiration = expiration, Right = OptionRight.Put, Action = OrderAction.Buy, Quantity = 1 },
                new() { UnderlyingSymbol = "SPY", Strike = 580m, Expiration = expiration, Right = OptionRight.Put, Action = OrderAction.Sell, Quantity = 1 },
                new() { UnderlyingSymbol = "SPY", Strike = 620m, Expiration = expiration, Right = OptionRight.Call, Action = OrderAction.Sell, Quantity = 1 },
                new() { UnderlyingSymbol = "SPY", Strike = 625m, Expiration = expiration, Right = OptionRight.Call, Action = OrderAction.Buy, Quantity = 1 },
            }
        };

        Assert.Equal(4, order.Legs.Count);
        Assert.Equal(2, order.Legs.Count(l => l.Action == OrderAction.Sell));
        Assert.Equal(2, order.Legs.Count(l => l.Action == OrderAction.Buy));
    }
}
