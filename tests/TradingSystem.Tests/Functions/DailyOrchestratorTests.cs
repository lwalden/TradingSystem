using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Core.Services;
using TradingSystem.Functions;
using TradingSystem.Strategies.Options;
using Xunit;

namespace TradingSystem.Tests.Functions;

public class DailyOrchestratorTests
{
    [Fact]
    public async Task RunPreMarket_NoBrokerRegistered_DoesNotThrow()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var orchestrator = CreateOrchestrator(new TradingSystemConfig(), provider);

        await orchestrator.RunPreMarket(timer: null!, CancellationToken.None);
    }

    [Fact]
    public async Task RunPreMarket_BrokerConnectFails_DoesNotResolveSleeveManager()
    {
        var brokerMock = new Mock<IBrokerService>();
        brokerMock
            .Setup(b => b.ConnectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var provider = new ServiceCollection()
            .AddSingleton(brokerMock.Object)
            .BuildServiceProvider();
        var orchestrator = CreateOrchestrator(new TradingSystemConfig(), provider);

        await orchestrator.RunPreMarket(timer: null!, CancellationToken.None);

        brokerMock.Verify(b => b.ConnectAsync(It.IsAny<CancellationToken>()), Times.Once);
        brokerMock.Verify(b => b.DisconnectAsync(), Times.Never);
    }

    [Fact]
    public async Task RunPreMarket_ManagerUnavailable_DisconnectsAndContinues()
    {
        var brokerMock = new Mock<IBrokerService>();
        brokerMock
            .Setup(b => b.ConnectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        brokerMock
            .Setup(b => b.DisconnectAsync())
            .Returns(Task.CompletedTask);

        var provider = new ServiceCollection()
            .AddSingleton(brokerMock.Object)
            .BuildServiceProvider();
        var orchestrator = CreateOrchestrator(new TradingSystemConfig(), provider);

        await orchestrator.RunPreMarket(timer: null!, CancellationToken.None);

        brokerMock.Verify(b => b.ConnectAsync(It.IsAny<CancellationToken>()), Times.Once);
        brokerMock.Verify(b => b.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task RunPreMarket_OptionUniverseEmpty_SkipsOptionsManagerRun()
    {
        var brokerMock = new Mock<IBrokerService>();
        brokerMock
            .Setup(b => b.ConnectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        brokerMock
            .Setup(b => b.DisconnectAsync())
            .Returns(Task.CompletedTask);

        var riskMock = CreateRiskManagerMock(isTradingHalted: true);
        var provider = BuildProviderWithOptionsManager(
            brokerMock,
            riskMock,
            tacticalConfig: new TacticalConfig { OptionUniverse = new List<string>() },
            out _,
            out _);

        var config = new TradingSystemConfig
        {
            Tactical = new TacticalConfig { OptionUniverse = new List<string>() }
        };
        var orchestrator = CreateOrchestrator(config, provider);

        await orchestrator.RunPreMarket(timer: null!, CancellationToken.None);

        riskMock.Verify(r => r.IsTradingHaltedAsync(It.IsAny<CancellationToken>()), Times.Never);
        brokerMock.Verify(b => b.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task RunPreMarket_WithOptionsManager_InvokesSleeveRunAndDisconnects()
    {
        var brokerMock = new Mock<IBrokerService>();
        brokerMock
            .Setup(b => b.ConnectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        brokerMock
            .Setup(b => b.DisconnectAsync())
            .Returns(Task.CompletedTask);

        var riskMock = CreateRiskManagerMock(isTradingHalted: true);
        var provider = BuildProviderWithOptionsManager(
            brokerMock,
            riskMock,
            tacticalConfig: new TacticalConfig
            {
                OptionUniverse = new List<string> { " spy ", "SPY", "QQQ" }
            },
            out _,
            out _);

        var config = new TradingSystemConfig
        {
            Tactical = new TacticalConfig
            {
                OptionUniverse = new List<string> { " spy ", "SPY", "QQQ" }
            }
        };
        var orchestrator = CreateOrchestrator(config, provider);

        await orchestrator.RunPreMarket(timer: null!, CancellationToken.None);

        riskMock.Verify(r => r.IsTradingHaltedAsync(It.IsAny<CancellationToken>()), Times.Once);
        brokerMock.Verify(b => b.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task RunPreMarket_ExecutableOptionsPath_ExecutesCandidateAndNormalizesSymbols()
    {
        var brokerMock = new Mock<IBrokerService>();
        brokerMock
            .Setup(b => b.ConnectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        brokerMock
            .Setup(b => b.DisconnectAsync())
            .Returns(Task.CompletedTask);
        brokerMock
            .Setup(b => b.GetAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Account
            {
                NetLiquidationValue = 100_000m,
                AvailableFunds = 100_000m,
                BuyingPower = 100_000m
            });
        brokerMock
            .Setup(b => b.GetPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>());
        brokerMock
            .Setup(b => b.GetOptionChainAsync("SPY", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateBullPutSpreadChain("SPY"));
        brokerMock
            .Setup(b => b.PlaceComboOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Order
            {
                Id = "order-1",
                BrokerId = "101",
                Status = OrderStatus.Submitted
            });

        var riskMock = CreateRiskManagerMock(isTradingHalted: false);
        var provider = BuildProviderWithOptionsManager(
            brokerMock,
            riskMock,
            tacticalConfig: new TacticalConfig
            {
                OptionUniverse = new List<string> { " spy ", "SPY" }
            },
            out var marketDataMock,
            out var calendarMock);

        marketDataMock
            .Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketRegime
            {
                Regime = RegimeType.RiskOn,
                Timestamp = DateTime.UtcNow
            });
        marketDataMock
            .Setup(m => m.GetOptionsAnalyticsAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OptionsAnalytics
            {
                Symbol = "SPY",
                IVRank = 60m,
                IVPercentile = 70m,
                CurrentIV = 0.25m,
                Timestamp = DateTime.UtcNow
            });
        marketDataMock
            .Setup(m => m.GetQuoteAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Quote
            {
                Symbol = "SPY",
                Last = 500m,
                Bid = 499.5m,
                Ask = 500.5m,
                Timestamp = DateTime.UtcNow
            });

        List<string>? symbolsSeenByCalendar = null;
        calendarMock
            .Setup(c => c.GetSymbolsInNoTradeWindowAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, DateTime, CancellationToken>((symbols, _, _) =>
            {
                symbolsSeenByCalendar = symbols.ToList();
            })
            .ReturnsAsync(new List<string>());

        var config = new TradingSystemConfig
        {
            Tactical = new TacticalConfig
            {
                OptionUniverse = new List<string> { " spy ", "SPY" }
            }
        };
        var orchestrator = CreateOrchestrator(config, provider);

        await orchestrator.RunPreMarket(timer: null!, CancellationToken.None);

        Assert.NotNull(symbolsSeenByCalendar);
        Assert.Single(symbolsSeenByCalendar!);
        Assert.Equal("SPY", symbolsSeenByCalendar![0]);
        riskMock.Verify(r => r.ValidateSignalAsync(It.IsAny<Signal>(), It.IsAny<Account>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        brokerMock.Verify(b => b.PlaceComboOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        brokerMock.Verify(b => b.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task RunPreMarket_WhenOptionsPathThrows_DisconnectsAndRethrows()
    {
        var brokerMock = new Mock<IBrokerService>();
        brokerMock
            .Setup(b => b.ConnectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        brokerMock
            .Setup(b => b.DisconnectAsync())
            .Returns(Task.CompletedTask);
        brokerMock
            .Setup(b => b.GetAccountAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        var riskMock = CreateRiskManagerMock(isTradingHalted: false);
        var provider = BuildProviderWithOptionsManager(
            brokerMock,
            riskMock,
            tacticalConfig: new TacticalConfig { OptionUniverse = new List<string> { "SPY" } },
            out _,
            out _);

        var config = new TradingSystemConfig
        {
            Tactical = new TacticalConfig { OptionUniverse = new List<string> { "SPY" } }
        };
        var orchestrator = CreateOrchestrator(config, provider);

        await Assert.ThrowsAsync<Exception>(() => orchestrator.RunPreMarket(timer: null!, CancellationToken.None));
        brokerMock.Verify(b => b.DisconnectAsync(), Times.Once);
    }

    private static DailyOrchestrator CreateOrchestrator(
        TradingSystemConfig config,
        IServiceProvider provider)
    {
        return new DailyOrchestrator(
            NullLogger<DailyOrchestrator>.Instance,
            Microsoft.Extensions.Options.Options.Create(config),
            provider);
    }

    private static Mock<IRiskManager> CreateRiskManagerMock(bool isTradingHalted)
    {
        var riskMock = new Mock<IRiskManager>();
        riskMock
            .Setup(r => r.IsTradingHaltedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(isTradingHalted);
        riskMock
            .Setup(r => r.ValidateSignalAsync(It.IsAny<Signal>(), It.IsAny<Account>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskValidationResult { IsValid = true });

        return riskMock;
    }

    private static IServiceProvider BuildProviderWithOptionsManager(
        Mock<IBrokerService> brokerMock,
        Mock<IRiskManager> riskMock,
        TacticalConfig tacticalConfig,
        out Mock<IMarketDataService> marketDataMock,
        out Mock<ICalendarService> calendarMock)
    {
        marketDataMock = new Mock<IMarketDataService>();
        calendarMock = new Mock<ICalendarService>();

        var optionsRepo = new InMemoryOptionsPositionRepository();
        var signalRepo = new Mock<ISignalRepository>();
        var orderRepo = new Mock<IOrderRepository>();

        signalRepo
            .Setup(r => r.SaveAsync(It.IsAny<Signal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Signal signal, CancellationToken _) => signal);
        signalRepo
            .Setup(r => r.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<SignalStatus>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        orderRepo
            .Setup(r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order order, CancellationToken _) => order);

        var screeningService = new OptionsScreeningService(
            marketDataMock.Object,
            brokerMock.Object,
            calendarMock.Object,
            Microsoft.Extensions.Options.Options.Create(tacticalConfig),
            NullLogger<OptionsScreeningService>.Instance);

        var executionService = new OptionsExecutionService(
            brokerMock.Object,
            orderRepo.Object,
            signalRepo.Object,
            optionsRepo,
            NullLogger<OptionsExecutionService>.Instance);

        var manager = new OptionsSleeveManager(
            brokerMock.Object,
            riskMock.Object,
            optionsRepo,
            screeningService,
            new OptionsLifecycleRules(tacticalConfig.Options),
            new OptionsPositionGrouper(),
            new OptionsCandidateConverter(),
            new OptionsPositionSizer(new RiskConfig
            {
                RiskPerTradePercent = 0.004m,
                MaxSingleSpreadPercent = 0.02m
            }),
            executionService,
            tacticalConfig,
            NullLogger<OptionsSleeveManager>.Instance);

        return new ServiceCollection()
            .AddSingleton(brokerMock.Object)
            .AddSingleton(manager)
            .BuildServiceProvider();
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
            => Task.FromResult(_positions.FirstOrDefault(p => p.Id == id));

        public Task<List<OptionsPosition>> GetOpenPositionsAsync(CancellationToken ct = default)
            => Task.FromResult(_positions.Where(p => p.Status == OptionsPositionStatus.Open).ToList());

        public Task<List<OptionsPosition>> GetByUnderlyingAsync(string symbol, CancellationToken ct = default)
            => Task.FromResult(_positions.Where(p =>
                p.UnderlyingSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)).ToList());

        public Task<List<OptionsPosition>> GetByDateRangeAsync(DateTime start, DateTime end, CancellationToken ct = default)
            => Task.FromResult(_positions.Where(p => p.OpenedAt >= start && p.OpenedAt <= end).ToList());

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
