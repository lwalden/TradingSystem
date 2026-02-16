using Microsoft.Extensions.Logging;
using Moq;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Core.Services;
using Xunit;

namespace TradingSystem.Tests.Options;

public class OptionsExecutionServiceTests
{
    private readonly Mock<IBrokerService> _brokerMock;
    private readonly Mock<IOrderRepository> _orderRepoMock;
    private readonly Mock<ISignalRepository> _signalRepoMock;
    private readonly Mock<IOptionsPositionRepository> _optionsPositionRepoMock;
    private readonly OptionsExecutionService _service;

    public OptionsExecutionServiceTests()
    {
        _brokerMock = new Mock<IBrokerService>();
        _orderRepoMock = new Mock<IOrderRepository>();
        _signalRepoMock = new Mock<ISignalRepository>();
        _optionsPositionRepoMock = new Mock<IOptionsPositionRepository>();

        _signalRepoMock
            .Setup(r => r.SaveAsync(It.IsAny<Signal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Signal s, CancellationToken _) => s);
        _orderRepoMock
            .Setup(r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order o, CancellationToken _) => o);
        _optionsPositionRepoMock
            .Setup(r => r.SaveAsync(It.IsAny<OptionsPosition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OptionsPosition p, CancellationToken _) => p);

        _service = new OptionsExecutionService(
            _brokerMock.Object,
            _orderRepoMock.Object,
            _signalRepoMock.Object,
            _optionsPositionRepoMock.Object,
            Mock.Of<ILogger<OptionsExecutionService>>());
    }

    [Fact]
    public async Task ExecuteSignalAsync_ComboEntry_UsesComboOrderAndSavesPosition()
    {
        var signal = CreateEntrySignal();
        var placedOrder = new Order { Id = "order-1", BrokerId = "1001", Symbol = "SPY", Status = OrderStatus.Submitted };

        _brokerMock
            .Setup(b => b.PlaceComboOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(placedOrder);

        var result = await _service.ExecuteSignalAsync(signal);

        Assert.True(result.Success);
        Assert.Single(result.Orders);
        _brokerMock.Verify(b => b.PlaceComboOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Once);
        _brokerMock.Verify(b => b.PlaceOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
        _optionsPositionRepoMock.Verify(r => r.SaveAsync(
            It.Is<OptionsPosition>(p =>
                p.UnderlyingSymbol == "SPY" &&
                p.Strategy == StrategyType.BullPutSpread &&
                p.OrderIds.Contains("order-1")),
            It.IsAny<CancellationToken>()), Times.Once);
        _signalRepoMock.Verify(r => r.UpdateStatusAsync(
            signal.Id, SignalStatus.Executed, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteSignalAsync_CloseSignal_UpdatesExistingPositionToClosing()
    {
        var signal = new Signal
        {
            Id = "sig-close",
            StrategyId = "options-bull-put-spread-close",
            StrategyName = "Close",
            SetupType = "LifecycleClose",
            Symbol = "SPY",
            SecurityType = "BAG",
            Direction = SignalDirection.ClosePosition,
            Strength = SignalStrength.Strong,
            SuggestedEntryPrice = 0.45m,
            SuggestedPositionSize = 1,
            SuggestedLegs = new List<OptionLeg>
            {
                new() { Symbol = "SPY_PUT_100", UnderlyingSymbol = "SPY", Strike = 100m, Expiration = DateTime.Today.AddDays(10), Right = OptionRight.Put, Action = OrderAction.Buy, Quantity = 1 },
                new() { Symbol = "SPY_PUT_95", UnderlyingSymbol = "SPY", Strike = 95m, Expiration = DateTime.Today.AddDays(10), Right = OptionRight.Put, Action = OrderAction.Sell, Quantity = 1 }
            },
            Rationale = "Close now",
            GeneratedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(8),
            Indicators = new Dictionary<string, object>
            {
                ["positionId"] = "pos-1",
                ["exitReason"] = "Rule trigger"
            }
        };

        var trackedPosition = new OptionsPosition
        {
            Id = "pos-1",
            UnderlyingSymbol = "SPY",
            Strategy = StrategyType.BullPutSpread,
            Status = OptionsPositionStatus.Open,
            Expiration = DateTime.Today.AddDays(10),
            Legs = new List<OptionsPositionLeg>()
        };

        _optionsPositionRepoMock
            .Setup(r => r.GetByIdAsync("pos-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(trackedPosition);
        _brokerMock
            .Setup(b => b.PlaceComboOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Order { Id = "order-close", BrokerId = "2001", Symbol = "SPY", Status = OrderStatus.Submitted });

        var result = await _service.ExecuteSignalAsync(signal);

        Assert.True(result.Success);
        _optionsPositionRepoMock.Verify(r => r.UpdateAsync(
            It.Is<OptionsPosition>(p =>
                p.Id == "pos-1" &&
                p.Status == OptionsPositionStatus.Closing &&
                p.OrderIds.Contains("order-close") &&
                p.ExitReason == "Rule trigger"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteSignalAsync_BrokerFailure_ReturnsRejected()
    {
        var signal = CreateEntrySignal();

        _brokerMock
            .Setup(b => b.PlaceComboOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Broker unavailable"));

        var result = await _service.ExecuteSignalAsync(signal);

        Assert.False(result.Success);
        Assert.Equal("Broker unavailable", result.ErrorMessage);
        _signalRepoMock.Verify(r => r.UpdateStatusAsync(
            signal.Id, SignalStatus.Rejected, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _optionsPositionRepoMock.Verify(r => r.SaveAsync(It.IsAny<OptionsPosition>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteSignalAsync_InvalidSize_RejectsSignal()
    {
        var signal = CreateEntrySignal();
        signal.SuggestedPositionSize = 0;

        var result = await _service.ExecuteSignalAsync(signal);

        Assert.False(result.Success);
        Assert.Contains("invalid position size", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        _signalRepoMock.Verify(r => r.UpdateStatusAsync(
            signal.Id, SignalStatus.Rejected, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Signal CreateEntrySignal()
    {
        return new Signal
        {
            Id = "sig-1",
            StrategyId = "options-bull-put-spread",
            StrategyName = "Bull Put Spread",
            SetupType = "BullPutSpread",
            Symbol = "SPY",
            SecurityType = "BAG",
            Direction = SignalDirection.Short,
            Strength = SignalStrength.Strong,
            SuggestedEntryPrice = 1.05m,
            SuggestedPositionSize = 1,
            SuggestedLegs = new List<OptionLeg>
            {
                new() { Symbol = "SPY_PUT_100", UnderlyingSymbol = "SPY", Strike = 100m, Expiration = DateTime.Today.AddDays(30), Right = OptionRight.Put, Action = OrderAction.Sell, Quantity = 1, Bid = 1.20m, Ask = 1.30m },
                new() { Symbol = "SPY_PUT_95", UnderlyingSymbol = "SPY", Strike = 95m, Expiration = DateTime.Today.AddDays(30), Right = OptionRight.Put, Action = OrderAction.Buy, Quantity = 1, Bid = 0.20m, Ask = 0.30m }
            },
            MaxProfit = 105m,
            MaxLoss = 395m,
            IVRank = 60m,
            Rationale = "Test signal",
            GeneratedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(8),
            Indicators = new Dictionary<string, object>
            {
                ["strategyType"] = StrategyType.BullPutSpread.ToString(),
                ["netCredit"] = 1.05m
            }
        };
    }
}
