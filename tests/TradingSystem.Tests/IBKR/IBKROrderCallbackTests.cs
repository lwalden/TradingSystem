using IBApi;
using Microsoft.Extensions.Logging;
using Moq;
using TradingSystem.Brokers.IBKR;
using TradingSystem.Core.Models;
using Xunit;

namespace TradingSystem.Tests.IBKR;

public class IBKROrderCallbackTests
{
    private readonly IBKRCallbackHandler _handler;
    private readonly Mock<ILogger> _mockLogger;

    public IBKROrderCallbackTests()
    {
        _mockLogger = new Mock<ILogger>();
        _handler = new IBKRCallbackHandler(_mockLogger.Object);
    }

    [Fact]
    public async Task OpenOrder_CompletesPlacementTask()
    {
        var task = _handler.RegisterOrderPlacementRequest(1001);
        var contract = new Contract { Symbol = "AAPL", SecType = "STK" };
        var order = new IBApi.Order { Action = "BUY", TotalQuantity = 100, OrderType = "LMT", Tif = "DAY" };
        var orderState = new OrderState { Status = "Submitted" };

        _handler.openOrder(1001, contract, order, orderState);

        var result = await task;
        Assert.Equal(1001, result.OrderId);
        Assert.Equal("AAPL", result.Symbol);
        Assert.Equal("BUY", result.Action);
        Assert.Equal(100m, result.TotalQuantity);
        Assert.Equal("Submitted", result.Status);
    }

    [Fact]
    public void OpenOrder_IgnoresUnknownOrderId()
    {
        // No TCS registered for orderId 9999
        var contract = new Contract { Symbol = "AAPL", SecType = "STK" };
        var order = new IBApi.Order { Action = "BUY", TotalQuantity = 100, OrderType = "LMT" };
        var orderState = new OrderState { Status = "Submitted" };

        // Should not throw
        _handler.openOrder(9999, contract, order, orderState);
    }

    [Fact]
    public void OrderStatus_FiresOnOrderStatusChanged()
    {
        int receivedOrderId = 0;
        OrderData? receivedData = null;

        _handler.OnOrderStatusChanged += (id, data) =>
        {
            receivedOrderId = id;
            receivedData = data;
        };

        _handler.orderStatus(1001, "Filled", 100m, 0m, 150.25, 12345, 0, 150.25, 1, "", 0);

        Assert.Equal(1001, receivedOrderId);
        Assert.NotNull(receivedData);
        Assert.Equal("Filled", receivedData.Status);
        Assert.Equal(100m, receivedData.Filled);
        Assert.Equal(0m, receivedData.Remaining);
        Assert.Equal(150.25, receivedData.AvgFillPrice);
    }

    [Fact]
    public void OrderStatus_PartialFill_ReportsCorrectQuantities()
    {
        OrderData? receivedData = null;
        _handler.OnOrderStatusChanged += (_, data) => receivedData = data;

        _handler.orderStatus(1001, "Submitted", 50m, 50m, 149.50, 12345, 0, 149.50, 1, "", 0);

        Assert.NotNull(receivedData);
        Assert.Equal("Submitted", receivedData.Status);
        Assert.Equal(50m, receivedData.Filled);
        Assert.Equal(50m, receivedData.Remaining);
    }

    [Fact]
    public void OrderStatus_Cancelled_ReportsCorrectly()
    {
        OrderData? receivedData = null;
        _handler.OnOrderStatusChanged += (_, data) => receivedData = data;

        _handler.orderStatus(1001, "Cancelled", 0m, 100m, 0, 12345, 0, 0, 1, "", 0);

        Assert.NotNull(receivedData);
        Assert.Equal("Cancelled", receivedData.Status);
        Assert.Equal(0m, receivedData.Filled);
    }

    [Fact]
    public async Task OpenOrderEnd_CompletesOpenOrdersTask()
    {
        var task = _handler.RegisterOpenOrdersRequest();

        var contract1 = new Contract { Symbol = "AAPL", SecType = "STK" };
        var order1 = new IBApi.Order { Action = "BUY", TotalQuantity = 100, OrderType = "LMT", Tif = "DAY" };
        var state1 = new OrderState { Status = "Submitted" };

        var contract2 = new Contract { Symbol = "MSFT", SecType = "STK" };
        var order2 = new IBApi.Order { Action = "BUY", TotalQuantity = 50, OrderType = "LMT", Tif = "DAY" };
        var state2 = new OrderState { Status = "Submitted" };

        _handler.openOrder(1001, contract1, order1, state1);
        _handler.openOrder(1002, contract2, order2, state2);
        _handler.openOrderEnd();

        var result = await task;
        Assert.Equal(2, result.Count);
        Assert.Equal("AAPL", result[0].Symbol);
        Assert.Equal("MSFT", result[1].Symbol);
    }

    [Fact]
    public async Task OpenOrderEnd_NoOrders_ReturnsEmptyList()
    {
        var task = _handler.RegisterOpenOrdersRequest();

        _handler.openOrderEnd();

        var result = await task;
        Assert.Empty(result);
    }

    [Fact]
    public async Task Error_FaultsOrderRequest()
    {
        var task = _handler.RegisterOrderPlacementRequest(1001);

        _handler.error(1001, 0, 201, "Order rejected", "");

        var ex = await Assert.ThrowsAsync<IBKRApiException>(() => task);
        Assert.Equal(201, ex.ErrorCode);
    }

    [Fact]
    public async Task ConnectionError_FaultsOrderRequests()
    {
        var task = _handler.RegisterOrderPlacementRequest(1001);

        _handler.error(-1, 0, 1100, "Connectivity lost", "");

        await Assert.ThrowsAsync<IBKRApiException>(() => task);
    }

    [Fact]
    public async Task ConnectionError_FaultsOpenOrdersRequest()
    {
        var task = _handler.RegisterOpenOrdersRequest();

        _handler.error(-1, 0, 1100, "Connectivity lost", "");

        await Assert.ThrowsAsync<IBKRApiException>(() => task);
    }

    [Fact]
    public async Task CleanupRequest_RemovesOrderTracking()
    {
        var task1 = _handler.RegisterOrderPlacementRequest(1001);
        _handler.CleanupRequest(1001);

        // After cleanup, completing the callback should not cause issues
        var contract = new Contract { Symbol = "AAPL", SecType = "STK" };
        var order = new IBApi.Order { Action = "BUY", TotalQuantity = 100, OrderType = "LMT" };
        var orderState = new OrderState { Status = "Submitted" };
        _handler.openOrder(1001, contract, order, orderState);

        // Task should still be pending (TCS was removed)
        Assert.False(task1.IsCompleted);
    }
}
