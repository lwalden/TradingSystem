using TradingSystem.Core.Models;
using Order = TradingSystem.Core.Models.Order;

namespace TradingSystem.Brokers.IBKR;

/// <summary>
/// Maps domain Order to IBApi.Order for TWS API calls.
/// </summary>
internal static class IBKROrderFactory
{
    public static IBApi.Order CreateOrder(Order domainOrder)
    {
        var ibOrder = new IBApi.Order
        {
            Action = MapAction(domainOrder.Action),
            TotalQuantity = domainOrder.Quantity,
            OrderType = MapOrderType(domainOrder.OrderType),
            Tif = MapTimeInForce(domainOrder.TimeInForce)
        };

        if (domainOrder.LimitPrice.HasValue)
            ibOrder.LmtPrice = (double)domainOrder.LimitPrice.Value;

        if (domainOrder.StopPrice.HasValue)
            ibOrder.AuxPrice = (double)domainOrder.StopPrice.Value;

        return ibOrder;
    }

    internal static string MapAction(OrderAction action)
    {
        return action switch
        {
            OrderAction.Buy => "BUY",
            OrderAction.Sell => "SELL",
            OrderAction.SellShort => "SSHORT",
            OrderAction.BuyToCover => "BUY",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown order action")
        };
    }

    internal static string MapOrderType(OrderType orderType)
    {
        return orderType switch
        {
            OrderType.Market => "MKT",
            OrderType.Limit => "LMT",
            OrderType.Stop => "STP",
            OrderType.StopLimit => "STP LMT",
            OrderType.TrailingStop => "TRAIL",
            _ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, "Unknown order type")
        };
    }

    internal static string MapTimeInForce(TimeInForce tif)
    {
        return tif switch
        {
            TimeInForce.Day => "DAY",
            TimeInForce.GTC => "GTC",
            TimeInForce.IOC => "IOC",
            TimeInForce.FOK => "FOK",
            _ => throw new ArgumentOutOfRangeException(nameof(tif), tif, "Unknown time in force")
        };
    }
}
