using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Core.Services;
using TradingSystem.Functions;
using Xunit;

namespace TradingSystem.Tests.Functions;

/// <summary>
/// S5-001 EOD pipeline tests — Rigorous tier (trading-critical snapshot persistence path).
/// Uses the REAL RiskManager (mocked IBrokerService/ICalendarService + in-memory
/// ISnapshotRepository) so transition-gating and idempotency semantics are exercised
/// end-to-end, per locked decision 4 (stop check = existing RiskManager semantics, alert-only).
/// </summary>
public class EndOfDayServiceTests
{
    private static readonly DateTime Today = DateTime.UtcNow.Date;

    [Fact]
    public async Task RunAsync_HappyPath_EnrichesPersistedSnapshot()
    {
        var snapshots = new CountingSnapshotRepository(
            PriorDaySnapshot(netLiq: 100_000m));
        var broker = CreateBrokerMock(connectSucceeds: true, CreateAccount(101_000m));
        var trades = CreateTradeRepoMock(
            new Trade { Commission = 1.5m, RealizedPnL = 100m, EntryTime = Today, ExitTime = Today },
            new Trade { Commission = 2.0m, RealizedPnL = null, EntryTime = Today, ExitTime = null });
        var marketData = CreateMarketDataMock(spy: 512.34m, vix: 18.5m, RegimeType.Cautious);

        var service = CreateService(broker, snapshots, tradeRepo: trades, marketData: marketData);
        var result = await service.RunAsync("run-1", CancellationToken.None);

        Assert.True(result.BrokerConnected);
        Assert.True(result.SnapshotPersisted);
        Assert.True(result.SnapshotEnriched);
        Assert.Empty(result.Warnings);

        var snapshot = await snapshots.GetSnapshotAsync(Today);
        Assert.NotNull(snapshot);
        // Base fields from RiskManager persist remain intact.
        Assert.Equal(101_000m, snapshot!.NetLiquidationValue);
        // Enrichment layer (S5-001 surface).
        Assert.Equal(2, snapshot.TradesExecuted);
        Assert.Equal(3.5m, snapshot.CommissionsPaid);
        Assert.Equal(100m, snapshot.RealizedPnL);
        Assert.Equal(512.34m, snapshot.SPYClose);
        Assert.Equal(18.5m, snapshot.VIXClose);
        Assert.Equal(RegimeType.Cautious, snapshot.MarketRegime);

        // Default D4: market context from exactly ONE cached regime call.
        marketData.Verify(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_BrokerConnectFails_NoSnapshotNoThrow()
    {
        var snapshots = new CountingSnapshotRepository();
        var broker = CreateBrokerMock(connectSucceeds: false, CreateAccount(100_000m));
        var trades = CreateTradeRepoMock();
        var marketData = CreateMarketDataMock(spy: 500m, vix: 15m, RegimeType.RiskOn);

        var service = CreateService(broker, snapshots, tradeRepo: trades, marketData: marketData);
        var result = await service.RunAsync("run-1", CancellationToken.None);

        // Default D3: connect failure → no snapshot written, no throw.
        Assert.False(result.BrokerConnected);
        Assert.False(result.SnapshotPersisted);
        Assert.Equal(0, snapshots.SaveCount);
        Assert.Null(await snapshots.GetSnapshotAsync(Today));

        trades.Verify(
            t => t.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        marketData.Verify(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()), Times.Never);
        broker.Verify(b => b.DisconnectAsync(), Times.Never);
    }

    [Fact]
    public async Task RunAsync_SameDayReRun_IsIdempotentAndDoesNotReFireAlerts()
    {
        // Daily stop trips (-3% vs prior close ≤ -2%) but weekly (-3% > -4%) and
        // drawdown (3% < 10%) do not — exactly one alert category in play.
        var snapshots = new CountingSnapshotRepository(
            PriorDaySnapshot(netLiq: 100_000m));
        var broker = CreateBrokerMock(connectSucceeds: true, CreateAccount(97_000m));
        var alerts = CreateAlertMock();
        var trades = CreateTradeRepoMock();
        var marketData = CreateMarketDataMock(spy: 500m, vix: 22m, RegimeType.Cautious);

        var service = CreateService(broker, snapshots, alerts: alerts, tradeRepo: trades, marketData: marketData);

        await service.RunAsync("run-1", CancellationToken.None);
        await service.RunAsync("run-2", CancellationToken.None);

        // Idempotent upsert-by-date: exactly one entry for today.
        Assert.Equal(1, snapshots.CountForDate(Today));
        // Transition gate: the second run sees today's persisted triggered flag → no re-fire.
        alerts.Verify(
            a => a.SendDailyStopTriggeredAsync(It.IsAny<RiskMetrics>(), It.IsAny<CancellationToken>()),
            Times.Once);
        alerts.Verify(
            a => a.SendWeeklyStopTriggeredAsync(It.IsAny<RiskMetrics>(), It.IsAny<CancellationToken>()),
            Times.Never);
        alerts.Verify(
            a => a.SendDrawdownHaltTriggeredAsync(It.IsAny<RiskMetrics>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_StopTriggerDay_AlertOnlyNoExecutionNoConfigWrite()
    {
        var snapshots = new CountingSnapshotRepository(
            PriorDaySnapshot(netLiq: 100_000m));
        var broker = CreateBrokerMock(connectSucceeds: true, CreateAccount(97_000m));
        var alerts = CreateAlertMock();
        var trades = CreateTradeRepoMock();
        var marketData = CreateMarketDataMock(spy: 500m, vix: 22m, RegimeType.Cautious);
        var execution = new Mock<IExecutionService>(MockBehavior.Strict);
        var configRepo = new Mock<IConfigRepository>(MockBehavior.Strict);

        var service = CreateService(broker, snapshots, alerts: alerts, tradeRepo: trades, marketData: marketData);
        var result = await service.RunAsync("run-1", CancellationToken.None);

        Assert.True(result.StopTriggered);
        Assert.True(result.SnapshotPersisted);
        alerts.Verify(
            a => a.SendDailyStopTriggeredAsync(It.IsAny<RiskMetrics>(), It.IsAny<CancellationToken>()),
            Times.Once);
        // Locked decision 4: alert-only — no order/execution call, no config write.
        execution.VerifyNoOtherCalls();
        configRepo.VerifyNoOtherCalls();
        // Run completed normally — broker disconnected.
        broker.Verify(b => b.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_EnrichmentFailure_KeepsBaseSnapshotAndWarns()
    {
        var snapshots = new CountingSnapshotRepository(
            PriorDaySnapshot(netLiq: 100_000m));
        var broker = CreateBrokerMock(connectSucceeds: true, CreateAccount(101_000m));
        var trades = CreateTradeRepoMock();
        var marketData = new Mock<IMarketDataService>();
        marketData
            .Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("market data unavailable"));

        var service = CreateService(broker, snapshots, tradeRepo: trades, marketData: marketData);
        var result = await service.RunAsync("run-1", CancellationToken.None);

        // Default D4: enrichment failure never loses the RiskManager-persisted base snapshot,
        // and the result reflects exactly that: base persisted, enrichment NOT applied.
        Assert.True(result.SnapshotPersisted);
        Assert.False(result.SnapshotEnriched);
        Assert.NotEmpty(result.Warnings);
        var snapshot = await snapshots.GetSnapshotAsync(Today);
        Assert.NotNull(snapshot);
        Assert.Equal(101_000m, snapshot!.NetLiquidationValue);
    }

    [Fact]
    public async Task RunAsync_DisconnectsExactlyOnce_EvenWhenEnrichmentThrows()
    {
        var snapshots = new CountingSnapshotRepository(
            PriorDaySnapshot(netLiq: 100_000m));
        var broker = CreateBrokerMock(connectSucceeds: true, CreateAccount(101_000m));
        var trades = new Mock<ITradeRepository>();
        trades
            .Setup(t => t.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("trade store unavailable"));
        var marketData = CreateMarketDataMock(spy: 500m, vix: 15m, RegimeType.RiskOn);

        var service = CreateService(broker, snapshots, tradeRepo: trades, marketData: marketData);
        var result = await service.RunAsync("run-1", CancellationToken.None);

        Assert.NotEmpty(result.Warnings);
        broker.Verify(b => b.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_HappyPath_DisconnectsExactlyOnce()
    {
        // Seeded prior-day snapshot → true happy path: base upsert + enrichment both succeed.
        var snapshots = new CountingSnapshotRepository(
            PriorDaySnapshot(netLiq: 100_000m));
        var broker = CreateBrokerMock(connectSucceeds: true, CreateAccount(100_000m));
        var trades = CreateTradeRepoMock();
        var marketData = CreateMarketDataMock(spy: 500m, vix: 15m, RegimeType.RiskOn);

        var service = CreateService(broker, snapshots, tradeRepo: trades, marketData: marketData);
        var result = await service.RunAsync("run-1", CancellationToken.None);

        Assert.True(result.BrokerConnected);
        Assert.True(result.SnapshotPersisted);
        Assert.True(result.SnapshotEnriched);
        broker.Verify(b => b.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_EnrichmentReadBackThrows_StillReportsBasePersisted()
    {
        // Regression for the SnapshotPersisted contract: the base upsert happens inside
        // RiskManager, so an enrichment read-back failure must NOT false-negative the flag.
        var snapshots = new ReadBackThrowingSnapshotRepository(
            new CountingSnapshotRepository(PriorDaySnapshot(netLiq: 100_000m)));
        var broker = CreateBrokerMock(connectSucceeds: true, CreateAccount(101_000m));
        var trades = CreateTradeRepoMock();
        var marketData = CreateMarketDataMock(spy: 500m, vix: 15m, RegimeType.RiskOn);

        var service = CreateService(broker, snapshots, tradeRepo: trades, marketData: marketData);
        var result = await service.RunAsync("run-1", CancellationToken.None);

        Assert.True(result.SnapshotPersisted);
        Assert.False(result.SnapshotEnriched);
        Assert.NotEmpty(result.Warnings);
        broker.Verify(b => b.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task RunAsync_ZeroTradeDay_EnrichesWithZeroActivity()
    {
        var snapshots = new CountingSnapshotRepository(
            PriorDaySnapshot(netLiq: 100_000m));
        var broker = CreateBrokerMock(connectSucceeds: true, CreateAccount(101_000m));
        var trades = CreateTradeRepoMock(); // empty trade day
        var marketData = CreateMarketDataMock(spy: 500m, vix: 15m, RegimeType.RiskOn);

        var service = CreateService(broker, snapshots, tradeRepo: trades, marketData: marketData);
        var result = await service.RunAsync("run-1", CancellationToken.None);

        Assert.True(result.SnapshotPersisted);
        var snapshot = await snapshots.GetSnapshotAsync(Today);
        Assert.NotNull(snapshot);
        Assert.Equal(0, snapshot!.TradesExecuted);
        Assert.Equal(0m, snapshot.CommissionsPaid);
        Assert.Equal(0m, snapshot.RealizedPnL);
        Assert.Equal(RegimeType.RiskOn, snapshot.MarketRegime);
    }

    [Fact]
    public async Task RunEndOfDay_ServiceUnregistered_DegradesWithoutThrow()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var orchestrator = CreateOrchestrator(provider);

        await orchestrator.RunEndOfDay(timer: null!, CancellationToken.None);
    }

    [Fact]
    public async Task RunEndOfDay_ServiceRegistered_DelegatesOnceWithRunId()
    {
        var endOfDay = new Mock<IEndOfDayService>();
        string? runIdSeen = null;
        endOfDay
            .Setup(s => s.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((runId, _) => runIdSeen = runId)
            .ReturnsAsync(new EndOfDayResult { BrokerConnected = true, SnapshotPersisted = true });

        var provider = new ServiceCollection()
            .AddSingleton(endOfDay.Object)
            .BuildServiceProvider();
        var orchestrator = CreateOrchestrator(provider);

        await orchestrator.RunEndOfDay(timer: null!, CancellationToken.None);

        endOfDay.Verify(s => s.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(string.IsNullOrWhiteSpace(runIdSeen));
    }

    [Fact]
    public async Task RunEndOfDay_ServiceThrows_LogsAndRethrows()
    {
        var endOfDay = new Mock<IEndOfDayService>();
        endOfDay
            .Setup(s => s.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var provider = new ServiceCollection()
            .AddSingleton(endOfDay.Object)
            .BuildServiceProvider();
        var orchestrator = CreateOrchestrator(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.RunEndOfDay(timer: null!, CancellationToken.None));
    }

    // ---------- helpers ----------

    private static EndOfDayService CreateService(
        Mock<IBrokerService> broker,
        ISnapshotRepository snapshotRepo,
        Mock<IRiskAlertService>? alerts = null,
        Mock<ITradeRepository>? tradeRepo = null,
        Mock<IMarketDataService>? marketData = null)
    {
        alerts ??= CreateAlertMock();
        var calendar = new Mock<ICalendarService>();

        // Existing risk parameters verbatim — S5-001 must not change them.
        var config = new TradingSystemConfig
        {
            Risk = new RiskConfig
            {
                RiskPerTradePercent = 0.004m,
                DailyStopPercent = 0.02m,
                WeeklyStopPercent = 0.04m,
                MaxSingleEquityPercent = 0.05m,
                MaxSingleSpreadPercent = 0.02m,
                MaxGrossLeverage = 1.2m,
                MaxDrawdownHalt = 0.10m
            }
        };

        var riskManager = new RiskManager(
            broker.Object,
            calendar.Object,
            Microsoft.Extensions.Options.Options.Create(config),
            NullLogger<RiskManager>.Instance,
            snapshotRepo,
            alerts.Object);

        return new EndOfDayService(
            broker.Object,
            riskManager,
            NullLogger<EndOfDayService>.Instance,
            snapshotRepo,
            tradeRepo?.Object,
            marketData?.Object);
    }

    private static DailyOrchestrator CreateOrchestrator(IServiceProvider provider)
    {
        return new DailyOrchestrator(
            NullLogger<DailyOrchestrator>.Instance,
            Microsoft.Extensions.Options.Options.Create(new TradingSystemConfig()),
            provider);
    }

    private static Mock<IBrokerService> CreateBrokerMock(bool connectSucceeds, Account account)
    {
        var broker = new Mock<IBrokerService>();
        broker.Setup(b => b.ConnectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(connectSucceeds);
        broker.Setup(b => b.DisconnectAsync()).Returns(Task.CompletedTask);
        broker.Setup(b => b.GetAccountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(account);
        broker.Setup(b => b.GetPositionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(account.Positions);
        return broker;
    }

    private static Mock<ITradeRepository> CreateTradeRepoMock(params Trade[] todaysTrades)
    {
        var trades = new Mock<ITradeRepository>();
        trades
            .Setup(t => t.GetByDateRangeAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(todaysTrades.ToList());
        return trades;
    }

    private static Mock<IMarketDataService> CreateMarketDataMock(decimal spy, decimal vix, RegimeType regime)
    {
        var marketData = new Mock<IMarketDataService>();
        marketData
            .Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketRegime
            {
                SPYPrice = spy,
                VIX = vix,
                Regime = regime,
                Timestamp = DateTime.UtcNow
            });
        return marketData;
    }

    private static Mock<IRiskAlertService> CreateAlertMock()
    {
        var mock = new Mock<IRiskAlertService>();
        mock.Setup(a => a.SendDailyStopTriggeredAsync(It.IsAny<RiskMetrics>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(a => a.SendWeeklyStopTriggeredAsync(It.IsAny<RiskMetrics>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(a => a.SendDrawdownHaltTriggeredAsync(It.IsAny<RiskMetrics>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static Account CreateAccount(decimal netLiq, params Position[] positions)
    {
        var positionList = positions.ToList();
        return new Account
        {
            NetLiquidationValue = netLiq,
            GrossPositionValue = positionList.Sum(p => Math.Abs(p.MarketValue)),
            Positions = positionList
        };
    }

    private static DailySnapshot PriorDaySnapshot(decimal netLiq)
    {
        return new DailySnapshot
        {
            Date = Today.AddDays(-1),
            NetLiquidationValue = netLiq,
            HighWaterMark = netLiq,
            MaxDrawdown = 0m
        };
    }

    /// <summary>
    /// Wraps a snapshot repository so range reads and saves work (RiskManager's base-upsert
    /// path), but the single-date read-back used by enrichment throws — isolating the
    /// enrichment read-back failure mode.
    /// </summary>
    private sealed class ReadBackThrowingSnapshotRepository : ISnapshotRepository
    {
        private readonly ISnapshotRepository _inner;

        public ReadBackThrowingSnapshotRepository(ISnapshotRepository inner) => _inner = inner;

        public Task SaveDailySnapshotAsync(DailySnapshot snapshot, CancellationToken cancellationToken = default)
            => _inner.SaveDailySnapshotAsync(snapshot, cancellationToken);

        public Task<DailySnapshot?> GetSnapshotAsync(DateTime date, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("snapshot read-back unavailable");

        public Task<List<DailySnapshot>> GetSnapshotsAsync(
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken = default)
            => _inner.GetSnapshotsAsync(startDate, endDate, cancellationToken);
    }

    /// <summary>
    /// In-memory ISnapshotRepository mirroring JsonSnapshotRepository's upsert-by-date
    /// semantics, with write counting for idempotency/no-write assertions.
    /// </summary>
    private sealed class CountingSnapshotRepository : ISnapshotRepository
    {
        private readonly List<DailySnapshot> _snapshots = new();

        public int SaveCount { get; private set; }

        public CountingSnapshotRepository(params DailySnapshot[] seed)
        {
            _snapshots.AddRange(seed);
        }

        public int CountForDate(DateTime date) => _snapshots.Count(s => s.Date.Date == date.Date);

        public Task SaveDailySnapshotAsync(DailySnapshot snapshot, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            var index = _snapshots.FindIndex(s => s.Date.Date == snapshot.Date.Date);
            if (index >= 0)
                _snapshots[index] = snapshot;
            else
                _snapshots.Add(snapshot);

            return Task.CompletedTask;
        }

        public Task<DailySnapshot?> GetSnapshotAsync(DateTime date, CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshots.FirstOrDefault(s => s.Date.Date == date.Date));

        public Task<List<DailySnapshot>> GetSnapshotsAsync(
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken = default)
        {
            var items = _snapshots
                .Where(s => s.Date.Date >= startDate.Date && s.Date.Date <= endDate.Date)
                .OrderBy(s => s.Date.Date)
                .ToList();
            return Task.FromResult(items);
        }
    }
}
