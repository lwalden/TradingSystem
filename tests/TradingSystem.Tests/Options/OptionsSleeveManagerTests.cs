using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Core.Services;
using TradingSystem.Strategies.Options;
using Xunit;

namespace TradingSystem.Tests.Options;

public class OptionsSleeveManagerTests
{
    [Fact]
    public async Task RunDailyAsync_TradingHalted_ReturnsEarly()
    {
        var brokerMock = new Mock<IBrokerService>();
        var riskMock = CreateRiskManagerMock(isHalted: true);
        var optionsRepo = new InMemoryOptionsPositionRepository();

        var manager = CreateManager(
            brokerMock,
            riskMock,
            optionsRepo,
            out _,
            out _,
            out _,
            out _,
            tacticalConfig: new TacticalConfig());

        var result = await manager.RunDailyAsync(new[] { "SPY" });

        Assert.True(result.TradingHalted);
        Assert.Contains(result.Warnings, w => w.Contains("halted", StringComparison.OrdinalIgnoreCase));
        brokerMock.Verify(b => b.GetAccountAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunDailyAsync_LifecycleNearExpiration_ExecutesCloseSignal()
    {
        var brokerMock = new Mock<IBrokerService>();
        var riskMock = CreateRiskManagerMock(isHalted: false);
        var optionsRepo = new InMemoryOptionsPositionRepository();

        var trackedPosition = new OptionsPosition
        {
            Id = "pos-1",
            UnderlyingSymbol = "SPY",
            Strategy = StrategyType.BullPutSpread,
            EntryNetCredit = 1.00m,
            MaxProfit = 1.00m,
            MaxLoss = 4.00m,
            Quantity = 1,
            CurrentValue = 0.90m,
            Status = OptionsPositionStatus.Open,
            Expiration = DateTime.Today.AddDays(2),
            Legs = new List<OptionsPositionLeg>
            {
                new() { Symbol = "SPY_PUT_500", Strike = 500m, Expiration = DateTime.Today.AddDays(2), Right = OptionRight.Put, Action = OrderAction.Sell, Quantity = 1, CurrentPrice = 1.20m },
                new() { Symbol = "SPY_PUT_495", Strike = 495m, Expiration = DateTime.Today.AddDays(2), Right = OptionRight.Put, Action = OrderAction.Buy, Quantity = 1, CurrentPrice = 0.30m }
            }
        };
        await optionsRepo.SaveAsync(trackedPosition);

        brokerMock.Setup(b => b.GetAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Account { NetLiquidationValue = 100_000m, AvailableFunds = 80_000m });
        brokerMock.Setup(b => b.GetPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>
            {
                CreateBrokerOption("SPY_PUT_500", "SPY", 500m, OptionRight.Put, qty: -1, avgCost: 1.20m, marketPrice: 1.00m),
                CreateBrokerOption("SPY_PUT_495", "SPY", 495m, OptionRight.Put, qty: 1, avgCost: 0.30m, marketPrice: 0.35m)
            });
        brokerMock.Setup(b => b.PlaceComboOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Order { Id = "order-close-1", BrokerId = "1001", Status = OrderStatus.Submitted });

        var manager = CreateManager(
            brokerMock,
            riskMock,
            optionsRepo,
            out _,
            out _,
            out _,
            out _,
            tacticalConfig: new TacticalConfig
            {
                Options = new OptionsConfig
                {
                    CloseDTEThreshold = 3,
                    RollDTEThreshold = 7,
                    MaxOpenPositions = 10,
                    MaxPositionsPerUnderlying = 2
                }
            });

        var result = await manager.RunDailyAsync(Array.Empty<string>());

        Assert.Equal(1, result.LifecycleActionsTriggered);
        Assert.Equal(1, result.SuccessfulExecutions);
        var updated = await optionsRepo.GetByIdAsync("pos-1");
        Assert.NotNull(updated);
        Assert.Equal(OptionsPositionStatus.Closing, updated!.Status);
    }

    [Fact]
    public async Task RunDailyAsync_WithCapacity_OpensNewEntry()
    {
        var brokerMock = new Mock<IBrokerService>();
        var riskMock = CreateRiskManagerMock(isHalted: false, validationValid: true);
        var optionsRepo = new InMemoryOptionsPositionRepository();

        brokerMock.Setup(b => b.GetAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Account
            {
                NetLiquidationValue = 100_000m,
                AvailableFunds = 100_000m,
                BuyingPower = 100_000m
            });
        brokerMock.Setup(b => b.GetPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>());
        brokerMock.Setup(b => b.PlaceComboOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Order { Id = "order-open-1", BrokerId = "2001", Status = OrderStatus.Submitted });

        var manager = CreateManager(
            brokerMock,
            riskMock,
            optionsRepo,
            out var marketDataMock,
            out var calendarMock,
            out _,
            out _,
            tacticalConfig: new TacticalConfig());

        marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketRegime { Regime = RegimeType.RiskOn, Timestamp = DateTime.UtcNow });
        calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OptionsAnalytics { Symbol = "SPY", IVRank = 60m, IVPercentile = 70m, CurrentIV = 0.25m, Timestamp = DateTime.UtcNow });
        marketDataMock.Setup(m => m.GetQuoteAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Quote { Symbol = "SPY", Last = 500m, Bid = 499.5m, Ask = 500.5m, Timestamp = DateTime.UtcNow });
        brokerMock.Setup(b => b.GetOptionChainAsync("SPY", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateBullPutSpreadChain("SPY"));

        var result = await manager.RunDailyAsync(new[] { "SPY" });

        Assert.True(result.NewEntriesOpened >= 1);
        Assert.True(result.SuccessfulExecutions >= 1);
        var open = await optionsRepo.GetOpenPositionsAsync();
        Assert.NotEmpty(open);
        Assert.Contains(open, p => p.UnderlyingSymbol == "SPY");
    }

    [Fact]
    public async Task RunDailyAsync_RespectsMaxPositionsPerUnderlying()
    {
        var brokerMock = new Mock<IBrokerService>();
        var riskMock = CreateRiskManagerMock(isHalted: false, validationValid: true);
        var optionsRepo = new InMemoryOptionsPositionRepository();

        await optionsRepo.SaveAsync(new OptionsPosition
        {
            Id = "existing-spy",
            UnderlyingSymbol = "SPY",
            Strategy = StrategyType.BullPutSpread,
            EntryNetCredit = 1.00m,
            MaxProfit = 1.00m,
            MaxLoss = 4.00m,
            Quantity = 1,
            CurrentValue = 0.70m,
            Status = OptionsPositionStatus.Open,
            Expiration = DateTime.Today.AddDays(20),
            Legs = new List<OptionsPositionLeg>()
        });

        brokerMock.Setup(b => b.GetAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Account { NetLiquidationValue = 100_000m, AvailableFunds = 100_000m });
        brokerMock.Setup(b => b.GetPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>());

        var manager = CreateManager(
            brokerMock,
            riskMock,
            optionsRepo,
            out var marketDataMock,
            out var calendarMock,
            out _,
            out _,
            tacticalConfig: new TacticalConfig
            {
                Options = new OptionsConfig
                {
                    MaxOpenPositions = 10,
                    MaxPositionsPerUnderlying = 1
                }
            });

        marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketRegime { Regime = RegimeType.RiskOn, Timestamp = DateTime.UtcNow });
        calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OptionsAnalytics { Symbol = "SPY", IVRank = 60m, IVPercentile = 70m, CurrentIV = 0.25m, Timestamp = DateTime.UtcNow });
        marketDataMock.Setup(m => m.GetQuoteAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Quote { Symbol = "SPY", Last = 500m, Bid = 499.5m, Ask = 500.5m, Timestamp = DateTime.UtcNow });
        brokerMock.Setup(b => b.GetOptionChainAsync("SPY", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateBullPutSpreadChain("SPY"));

        var result = await manager.RunDailyAsync(new[] { "SPY" });

        Assert.Equal(0, result.NewEntriesOpened);
        var open = await optionsRepo.GetOpenPositionsAsync();
        Assert.Single(open); // only existing position remains
    }

    private static Mock<IRiskManager> CreateRiskManagerMock(bool isHalted, bool validationValid = true)
    {
        var riskMock = new Mock<IRiskManager>();
        riskMock.Setup(r => r.IsTradingHaltedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(isHalted);
        riskMock.Setup(r => r.ValidateSignalAsync(It.IsAny<Signal>(), It.IsAny<Account>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskValidationResult
            {
                IsValid = validationValid,
                PassedChecks = validationValid ? new List<string> { "ok" } : new List<string>(),
                FailedChecks = validationValid ? new List<string>() : new List<string> { "invalid" }
            });
        return riskMock;
    }

    private static OptionsSleeveManager CreateManager(
        Mock<IBrokerService> brokerMock,
        Mock<IRiskManager> riskMock,
        InMemoryOptionsPositionRepository optionsRepo,
        out Mock<IMarketDataService> marketDataMock,
        out Mock<ICalendarService> calendarMock,
        out Mock<IOrderRepository> orderRepoMock,
        out Mock<ISignalRepository> signalRepoMock,
        TacticalConfig? tacticalConfig = null)
    {
        tacticalConfig ??= new TacticalConfig();
        marketDataMock = new Mock<IMarketDataService>();
        calendarMock = new Mock<ICalendarService>();
        orderRepoMock = new Mock<IOrderRepository>();
        signalRepoMock = new Mock<ISignalRepository>();

        signalRepoMock.Setup(r => r.SaveAsync(It.IsAny<Signal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Signal s, CancellationToken _) => s);
        orderRepoMock.Setup(r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order o, CancellationToken _) => o);

        var screening = new OptionsScreeningService(
            marketDataMock.Object,
            brokerMock.Object,
            calendarMock.Object,
            Microsoft.Extensions.Options.Options.Create(tacticalConfig),
            NullLogger<OptionsScreeningService>.Instance);

        var executionService = new OptionsExecutionService(
            brokerMock.Object,
            orderRepoMock.Object,
            signalRepoMock.Object,
            optionsRepo,
            Mock.Of<ILogger<OptionsExecutionService>>());

        var lifecycleRules = new OptionsLifecycleRules(tacticalConfig.Options);
        var positionSizer = new OptionsPositionSizer(new RiskConfig
        {
            RiskPerTradePercent = 0.004m,
            MaxSingleSpreadPercent = 0.02m
        });

        return new OptionsSleeveManager(
            brokerMock.Object,
            riskMock.Object,
            optionsRepo,
            screening,
            lifecycleRules,
            new OptionsPositionGrouper(),
            new OptionsCandidateConverter(),
            positionSizer,
            executionService,
            tacticalConfig,
            Mock.Of<ILogger<OptionsSleeveManager>>());
    }

    private static List<OptionContract> CreateBullPutSpreadChain(string symbol)
    {
        var expiration = DateTime.Today.AddDays(30);
        return new List<OptionContract>
        {
            new()
            {
                Symbol = $"{symbol}_PUT_500",
                UnderlyingSymbol = symbol,
                Strike = 500m,
                Expiration = expiration,
                Right = OptionRight.Put,
                Delta = -0.20m,
                Bid = 2.02m,
                Ask = 2.06m,
                Last = 2.04m,
                OpenInterest = 500,
                Volume = 100,
                ImpliedVolatility = 0.30m,
                Theta = -0.05m,
                Timestamp = DateTime.UtcNow
            },
            new()
            {
                Symbol = $"{symbol}_PUT_495",
                UnderlyingSymbol = symbol,
                Strike = 495m,
                Expiration = expiration,
                Right = OptionRight.Put,
                Delta = -0.10m,
                Bid = 1.02m,
                Ask = 1.04m,
                Last = 1.03m,
                OpenInterest = 500,
                Volume = 100,
                ImpliedVolatility = 0.30m,
                Theta = -0.04m,
                Timestamp = DateTime.UtcNow
            }
        };
    }

    private static Position CreateBrokerOption(
        string symbol,
        string underlying,
        decimal strike,
        OptionRight right,
        decimal qty,
        decimal avgCost,
        decimal marketPrice)
    {
        return new Position
        {
            Symbol = symbol,
            SecurityType = "OPT",
            UnderlyingSymbol = underlying,
            Strike = strike,
            Expiration = DateTime.Today.AddDays(30),
            Right = right,
            Quantity = qty,
            AverageCost = avgCost,
            MarketPrice = marketPrice
        };
    }

    private sealed class InMemoryOptionsPositionRepository : IOptionsPositionRepository
    {
        private readonly List<OptionsPosition> _positions = new();

        public Task<OptionsPosition> SaveAsync(OptionsPosition position, CancellationToken ct = default)
        {
            var existing = _positions.FindIndex(p => p.Id == position.Id);
            if (existing >= 0)
                _positions[existing] = position;
            else
                _positions.Add(position);

            return Task.FromResult(position);
        }

        public Task<OptionsPosition?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            return Task.FromResult(_positions.FirstOrDefault(p => p.Id == id));
        }

        public Task<List<OptionsPosition>> GetOpenPositionsAsync(CancellationToken ct = default)
        {
            var open = _positions.Where(p => p.Status == OptionsPositionStatus.Open).ToList();
            return Task.FromResult(open);
        }

        public Task<List<OptionsPosition>> GetByUnderlyingAsync(string symbol, CancellationToken ct = default)
        {
            var matches = _positions.Where(p => p.UnderlyingSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)).ToList();
            return Task.FromResult(matches);
        }

        public Task<List<OptionsPosition>> GetByDateRangeAsync(DateTime start, DateTime end, CancellationToken ct = default)
        {
            var matches = _positions.Where(p => p.OpenedAt >= start && p.OpenedAt <= end).ToList();
            return Task.FromResult(matches);
        }

        public Task UpdateAsync(OptionsPosition position, CancellationToken ct = default)
        {
            var index = _positions.FindIndex(p => p.Id == position.Id);
            if (index < 0)
                _positions.Add(position);
            else
                _positions[index] = position;

            return Task.CompletedTask;
        }
    }
}
