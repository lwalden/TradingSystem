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
            tacticalConfig: new TacticalConfig { OptionUniverse = new List<string>() });

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
            });

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
        TacticalConfig tacticalConfig)
    {
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
            Mock.Of<IMarketDataService>(),
            brokerMock.Object,
            Mock.Of<ICalendarService>(),
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
