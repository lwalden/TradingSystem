using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Core.Services;
using Xunit;

namespace TradingSystem.Tests.Risk;

public class RiskManagerTests
{
    [Fact]
    public async Task ValidateSignalAsync_RiskExceedsBudget_ReturnsInvalid()
    {
        var account = CreateAccount(100_000m);
        var manager = CreateRiskManager(account, noTradeWindow: false);
        var signal = CreateSignal(
            strategyId: "options-bull-put-spread",
            symbol: "SPY",
            securityType: "BAG",
            positionSize: 1,
            suggestedRiskAmount: 900m);

        var result = await manager.ValidateSignalAsync(signal, account);

        Assert.False(result.IsValid);
        Assert.Contains("per-trade-risk", result.FailedChecks);
    }

    [Fact]
    public async Task ValidateSignalAsync_NoTradeWindow_ReturnsInvalid()
    {
        var account = CreateAccount(100_000m);
        var manager = CreateRiskManager(account, noTradeWindow: true);
        var signal = CreateSignal(
            strategyId: "options-bear-call-spread",
            symbol: "AAPL",
            securityType: "BAG",
            positionSize: 1,
            suggestedRiskAmount: 150m);

        var result = await manager.ValidateSignalAsync(signal, account);

        Assert.False(result.IsValid);
        Assert.Contains("no-trade-window", result.FailedChecks);
    }

    [Fact]
    public async Task ValidateSignalAsync_PositionCapExceeded_ReturnsInvalid()
    {
        var account = CreateAccount(
            1_000_000m,
            new Position
            {
                Symbol = "SPY_500P",
                UnderlyingSymbol = "SPY",
                Sleeve = SleeveType.Tactical,
                Quantity = -1950,
                AverageCost = 10m,
                MarketPrice = 10m
            });
        var manager = CreateRiskManager(account, noTradeWindow: false);
        var signal = CreateSignal(
            strategyId: "options-csp",
            symbol: "SPY",
            securityType: "OPT",
            positionSize: 1,
            suggestedRiskAmount: 1_000m);

        var result = await manager.ValidateSignalAsync(signal, account);

        Assert.False(result.IsValid);
        Assert.Contains("position-limits", result.FailedChecks);
    }

    [Fact]
    public async Task ValidateSignalAsync_ValidSignal_ReturnsValid()
    {
        var account = CreateAccount(100_000m);
        var manager = CreateRiskManager(account, noTradeWindow: false);
        var signal = CreateSignal(
            strategyId: "options-iron-condor",
            symbol: "QQQ",
            securityType: "BAG",
            positionSize: 1,
            suggestedRiskAmount: 150m);

        var result = await manager.ValidateSignalAsync(signal, account);

        Assert.True(result.IsValid);
        Assert.Empty(result.FailedChecks);
        Assert.Contains("per-trade-risk", result.PassedChecks);
        Assert.Contains("position-limits", result.PassedChecks);
    }

    [Fact]
    public async Task CheckIncomeCapsAsync_IssuerCapViolation_ReturnsInvalid()
    {
        var account = CreateAccount(
            100_000m,
            new Position
            {
                Symbol = "ARCC",
                Sleeve = SleeveType.Income,
                Category = "BDC",
                Quantity = 100,
                AverageCost = 120m,
                MarketPrice = 120m
            });
        var manager = CreateRiskManager(account, noTradeWindow: false);

        var result = await manager.CheckIncomeCapsAsync("ARCC", 0m, account);

        Assert.False(result.WithinCaps);
        Assert.NotNull(result.IssuerCapViolation);
    }

    [Fact]
    public void CalculatePositionSize_UsesStopDistance()
    {
        var account = CreateAccount(100_000m);
        var manager = CreateRiskManager(account, noTradeWindow: false);

        var sizing = manager.CalculatePositionSize("SPY", 50m, 48m, 100_000m, 0.004m);

        Assert.Equal(200, sizing.Shares);
        Assert.Equal(400m, sizing.RiskAmount);
        Assert.Equal(10_000m, sizing.PositionValue);
    }

    [Fact]
    public async Task IsTradingHaltedAsync_DailyStopHit_ReturnsTrue()
    {
        var account = CreateAccount(
            100_000m,
            new Position
            {
                Symbol = "SPY",
                Sleeve = SleeveType.Tactical,
                Quantity = 100,
                AverageCost = 100m,
                MarketPrice = 70m
            });
        var manager = CreateRiskManager(account, noTradeWindow: false);

        var halted = await manager.IsTradingHaltedAsync();

        Assert.True(halted);
    }

    [Fact]
    public async Task GetRiskMetricsAsync_ReturnsExpectedExposureAndCount()
    {
        var account = CreateAccount(
            100_000m,
            new Position
            {
                Symbol = "VIG",
                Sleeve = SleeveType.Income,
                Quantity = 100,
                AverageCost = 100m,
                MarketPrice = 102m
            },
            new Position
            {
                Symbol = "SPY_500P",
                UnderlyingSymbol = "SPY",
                Sleeve = SleeveType.Tactical,
                Quantity = -1,
                AverageCost = 12m,
                MarketPrice = 11m
            });
        var manager = CreateRiskManager(account, noTradeWindow: false);

        var metrics = await manager.GetRiskMetricsAsync();

        Assert.Equal(2, metrics.OpenPositionCount);
        Assert.True(metrics.GrossExposure > 0m);
    }

    [Fact]
    public async Task GetRiskMetricsAsync_UsesPersistedBaselineAndDrawdown()
    {
        var snapshots = new InMemorySnapshotRepository(
            new DailySnapshot
            {
                Date = DateTime.UtcNow.Date.AddDays(-1),
                NetLiquidationValue = 100_000m,
                HighWaterMark = 110_000m,
                MaxDrawdown = 0.05m
            });

        var account = CreateAccount(
            95_000m,
            new Position
            {
                Symbol = "SPY",
                Sleeve = SleeveType.Tactical,
                Quantity = 10,
                AverageCost = 100m,
                MarketPrice = 100m
            });

        var manager = CreateRiskManager(account, noTradeWindow: false, snapshotRepository: snapshots);
        var metrics = await manager.GetRiskMetricsAsync();

        Assert.Equal(-5_000m, metrics.DailyPnL);
        Assert.Equal(-0.05m, metrics.DailyPnLPercent);
        Assert.Equal(110_000m, metrics.HighWaterMark);
        Assert.InRange(metrics.CurrentDrawdown, 0.136m, 0.137m);
        Assert.Equal(metrics.CurrentDrawdown, metrics.MaxDrawdown);
    }

    [Fact]
    public async Task GetRiskMetricsAsync_DailyStopAlertSentOnlyOnTransition()
    {
        var snapshots = new InMemorySnapshotRepository();
        var alerts = CreateNoOpAlertMock();
        var account = CreateAccount(
            100_000m,
            new Position
            {
                Symbol = "SPY",
                Sleeve = SleeveType.Tactical,
                Quantity = 100,
                AverageCost = 100m,
                MarketPrice = 70m
            });

        var manager = CreateRiskManager(
            account,
            noTradeWindow: false,
            snapshotRepository: snapshots,
            alertService: alerts);

        await manager.GetRiskMetricsAsync();
        await manager.GetRiskMetricsAsync();

        alerts.Verify(
            a => a.SendDailyStopTriggeredAsync(It.IsAny<RiskMetrics>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IsTradingHaltedAsync_DrawdownHaltTriggered_ReturnsTrue()
    {
        var snapshots = new InMemorySnapshotRepository(
            new DailySnapshot
            {
                Date = DateTime.UtcNow.Date.AddDays(-1),
                NetLiquidationValue = 95_000m,
                HighWaterMark = 110_000m,
                MaxDrawdown = 0.08m
            });

        var account = CreateAccount(95_000m);
        var manager = CreateRiskManager(account, noTradeWindow: false, snapshotRepository: snapshots);

        var halted = await manager.IsTradingHaltedAsync();

        Assert.True(halted);
    }

    private static RiskManager CreateRiskManager(
        Account account,
        bool noTradeWindow,
        InMemorySnapshotRepository? snapshotRepository = null,
        Mock<IRiskAlertService>? alertService = null)
    {
        var brokerMock = new Mock<IBrokerService>();
        brokerMock.Setup(b => b.GetAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        brokerMock.Setup(b => b.GetPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(account.Positions);

        var calendarMock = new Mock<ICalendarService>();
        calendarMock.Setup(c => c.IsInNoTradeWindowAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(noTradeWindow);

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
            },
            Income = new IncomeConfig
            {
                MaxIssuerPercent = 0.10m,
                MaxCategoryPercent = 0.40m
            }
        };

        snapshotRepository ??= new InMemorySnapshotRepository();
        alertService ??= CreateNoOpAlertMock();

        return new RiskManager(
            brokerMock.Object,
            calendarMock.Object,
            Microsoft.Extensions.Options.Options.Create(config),
            NullLogger<RiskManager>.Instance,
            snapshotRepository,
            alertService.Object);
    }

    private static Mock<IRiskAlertService> CreateNoOpAlertMock()
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
        var gross = positionList.Sum(p => Math.Abs(p.MarketValue));
        return new Account
        {
            NetLiquidationValue = netLiq,
            GrossPositionValue = gross,
            Positions = positionList
        };
    }

    private static Signal CreateSignal(
        string strategyId,
        string symbol,
        string securityType,
        decimal positionSize,
        decimal suggestedRiskAmount)
    {
        return new Signal
        {
            StrategyId = strategyId,
            StrategyName = strategyId,
            SetupType = strategyId,
            Symbol = symbol,
            SecurityType = securityType,
            Direction = SignalDirection.Short,
            Strength = SignalStrength.Moderate,
            SuggestedPositionSize = positionSize,
            SuggestedRiskAmount = suggestedRiskAmount,
            GeneratedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Status = SignalStatus.Active
        };
    }

    private sealed class InMemorySnapshotRepository : ISnapshotRepository
    {
        private readonly List<DailySnapshot> _snapshots = new();

        public InMemorySnapshotRepository(params DailySnapshot[] snapshots)
        {
            _snapshots.AddRange(snapshots);
        }

        public Task SaveDailySnapshotAsync(DailySnapshot snapshot, CancellationToken cancellationToken = default)
        {
            var index = _snapshots.FindIndex(s => s.Date.Date == snapshot.Date.Date);
            if (index >= 0)
                _snapshots[index] = snapshot;
            else
                _snapshots.Add(snapshot);

            return Task.CompletedTask;
        }

        public Task<DailySnapshot?> GetSnapshotAsync(DateTime date, CancellationToken cancellationToken = default)
        {
            var snapshot = _snapshots.FirstOrDefault(s => s.Date.Date == date.Date);
            return Task.FromResult(snapshot);
        }

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
