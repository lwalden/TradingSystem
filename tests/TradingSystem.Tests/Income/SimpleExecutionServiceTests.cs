using Microsoft.Extensions.Logging;
using Moq;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Core.Services;
using Xunit;

namespace TradingSystem.Tests.Income;

public class SimpleExecutionServiceTests
{
    private readonly Mock<IBrokerService> _mockBroker;
    private readonly Mock<IOrderRepository> _mockOrderRepo;
    private readonly Mock<ISignalRepository> _mockSignalRepo;
    private readonly SimpleExecutionService _service;

    public SimpleExecutionServiceTests()
    {
        _mockBroker = new Mock<IBrokerService>();
        _mockOrderRepo = new Mock<IOrderRepository>();
        _mockSignalRepo = new Mock<ISignalRepository>();

        _service = new SimpleExecutionService(
            _mockBroker.Object,
            _mockOrderRepo.Object,
            _mockSignalRepo.Object,
            Mock.Of<ILogger<SimpleExecutionService>>());
    }

    private static Signal CreateTestSignal(string symbol = "VIG", decimal price = 180m, int shares = 5)
    {
        return new Signal
        {
            StrategyId = "income-monthly-reinvest",
            StrategyName = "Test",
            Symbol = symbol,
            Direction = SignalDirection.Long,
            Strength = SignalStrength.Moderate,
            SuggestedEntryPrice = price,
            SuggestedPositionSize = shares,
            Rationale = "Test reinvest",
            GeneratedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(8)
        };
    }

    [Fact]
    public async Task ExecuteSignalAsync_PlacesOrderAndReturnsSuccess()
    {
        var signal = CreateTestSignal();
        var placedOrder = new Order
        {
            Id = "order-1",
            BrokerId = "1001",
            Symbol = "VIG",
            Status = OrderStatus.Submitted
        };

        _mockBroker.Setup(b => b.PlaceOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(placedOrder);
        _mockOrderRepo.Setup(r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(placedOrder);

        var result = await _service.ExecuteSignalAsync(signal);

        Assert.True(result.Success);
        Assert.Single(result.Orders);
        Assert.Equal("order-1", result.Orders[0].Id);
    }

    [Fact]
    public async Task ExecuteSignalAsync_PersistsOrder()
    {
        var signal = CreateTestSignal();
        var placedOrder = new Order { Id = "order-1", BrokerId = "1001", Symbol = "VIG" };

        _mockBroker.Setup(b => b.PlaceOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(placedOrder);
        _mockOrderRepo.Setup(r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(placedOrder);

        await _service.ExecuteSignalAsync(signal);

        _mockOrderRepo.Verify(r => r.SaveAsync(placedOrder, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteSignalAsync_UpdatesSignalStatus()
    {
        var signal = CreateTestSignal();
        var placedOrder = new Order { Id = "order-1", BrokerId = "1001", Symbol = "VIG" };

        _mockBroker.Setup(b => b.PlaceOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(placedOrder);
        _mockOrderRepo.Setup(r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(placedOrder);

        await _service.ExecuteSignalAsync(signal);

        _mockSignalRepo.Verify(r => r.UpdateStatusAsync(
            signal.Id, SignalStatus.Executed, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteSignalAsync_BrokerFailure_ReturnsFailure()
    {
        var signal = CreateTestSignal();

        _mockBroker.Setup(b => b.PlaceOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection lost"));

        var result = await _service.ExecuteSignalAsync(signal);

        Assert.False(result.Success);
        Assert.Equal("Connection lost", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteSignalAsync_BrokerFailure_SetsSignalRejected()
    {
        var signal = CreateTestSignal();

        _mockBroker.Setup(b => b.PlaceOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection lost"));

        await _service.ExecuteSignalAsync(signal);

        _mockSignalRepo.Verify(r => r.UpdateStatusAsync(
            signal.Id, SignalStatus.Rejected, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteSignalAsync_CreatesCorrectOrder()
    {
        var signal = CreateTestSignal("ARCC", 20.50m, 25);
        var placedOrder = new Order { Id = "order-1", BrokerId = "1001", Symbol = "ARCC" };

        _mockBroker.Setup(b => b.PlaceOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(placedOrder);
        _mockOrderRepo.Setup(r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(placedOrder);

        await _service.ExecuteSignalAsync(signal);

        _mockBroker.Verify(b => b.PlaceOrderAsync(
            It.Is<Order>(o =>
                o.Symbol == "ARCC" &&
                o.Action == OrderAction.Buy &&
                o.Quantity == 25 &&
                o.OrderType == OrderType.Limit &&
                o.LimitPrice == 20.50m &&
                o.TimeInForce == TimeInForce.Day &&
                o.Sleeve == SleeveType.Income &&
                o.SignalId == signal.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteSignalsAsync_ExecutesAll()
    {
        var signals = new List<Signal> { CreateTestSignal("VIG"), CreateTestSignal("ARCC") };
        var placedOrder = new Order { Id = "order-1" };

        _mockBroker.Setup(b => b.PlaceOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(placedOrder);
        _mockOrderRepo.Setup(r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(placedOrder);

        var results = await _service.ExecuteSignalsAsync(signals);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task ExecuteSignalAsync_MarketOrder_WhenNoEntryPrice()
    {
        var signal = CreateTestSignal();
        signal.SuggestedEntryPrice = null; // No limit price

        var placedOrder = new Order { Id = "order-1" };
        _mockBroker.Setup(b => b.PlaceOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(placedOrder);
        _mockOrderRepo.Setup(r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(placedOrder);

        await _service.ExecuteSignalAsync(signal);

        _mockBroker.Verify(b => b.PlaceOrderAsync(
            It.Is<Order>(o => o.OrderType == OrderType.Market),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
