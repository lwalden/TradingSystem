using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Functions;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace TradingSystem.Tests.Functions;

/// <summary>
/// S6-007: quarterly-audit stub honesty. During the live paper-validation run, logs are
/// evidence — a TODO stub that logs "Quarterly quality audit complete" while doing nothing
/// would falsify the run record in the Jul 1–7 audit window. The stub must instead log an
/// explicit Warning that it is not implemented and was skipped. Also covers the internal
/// Func&lt;DateTime&gt; clock seam (Default D1) that S6-001 builds on.
///
/// S6-001: RunMonthlyReinvest is now a THIN timer wrapper (S5-001 / DailyOrchestrator EOD
/// pattern): cheap Day&gt;7 pre-filter, null-tolerant IIncomeReinvestService resolve
/// (ADR-024 — warn + return when unregistered), delegate with (runId, utcNow, ct) via the
/// clock seam, log the structured result, and on an unhandled exception send the
/// "Orchestration Run Failure — Monthly Reinvest" operational alert (exception TYPE NAME
/// only) before rethrowing (Default D7).
/// </summary>
public class IncomeSleeveFunctionTests
{
    // ---------- harness ----------

    private static (IncomeSleeveFunction Function, Mock<ILogger<IncomeSleeveFunction>> Logger) Build(
        DateTime pinnedUtcNow,
        Action<IServiceCollection>? configureServices = null)
    {
        var logger = new Mock<ILogger<IncomeSleeveFunction>>();
        var config = MsOptions.Create(new TradingSystemConfig());
        var services = new ServiceCollection();
        configureServices?.Invoke(services);
        var function = new IncomeSleeveFunction(
            logger.Object, config, services.BuildServiceProvider(), () => pinnedUtcNow);
        return (function, logger);
    }

    private static List<(LogLevel Level, string Message)> CapturedLogs(Mock<ILogger<IncomeSleeveFunction>> logger)
    {
        var entries = new List<(LogLevel, string)>();
        foreach (var inv in logger.Invocations)
        {
            if (inv.Method.Name != nameof(ILogger.Log))
                continue;
            var level = (LogLevel)inv.Arguments[0];
            var message = inv.Arguments[2]?.ToString() ?? string.Empty;
            entries.Add((level, message));
        }
        return entries;
    }

    // 2026-07-01 (Wednesday) is the first real reinvest firing the S6-001 wiring protects.
    private static readonly DateTime FirstWeekdayJul2026 = new(2026, 7, 1, 13, 30, 0, DateTimeKind.Utc);

    // ---------- quarterly audit: honesty (S6-007 test plan 1) ----------

    [Fact]
    public async Task RunQuarterlyAudit_WithinFirstWeek_LogsExactlyOneNotImplementedWarning()
    {
        // 2026-07-01 is a Wednesday with Day = 1 (≤ 7): inside the audit window.
        var (function, logger) = Build(new DateTime(2026, 7, 1, 14, 0, 0, DateTimeKind.Utc));

        await function.RunQuarterlyAudit(new TimerInfo(), CancellationToken.None);

        var warnings = CapturedLogs(logger)
            .Where(e => e.Level == LogLevel.Warning)
            .Select(e => e.Message)
            .ToList();

        var honest = warnings.Where(m => m.Contains("not implemented — skipped")).ToList();
        Assert.Single(honest);

        // The warning carries the same runId as the start-of-run Information line.
        var startMessage = CapturedLogs(logger)
            .Where(e => e.Level == LogLevel.Information)
            .Select(e => e.Message)
            .Single(m => m.Contains("Starting quarterly quality audit"));
        var runId = Regex.Match(startMessage, @"RunId: (\w{8})").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(runId));
        Assert.Contains($"RunId: {runId}", honest[0]);
    }

    [Fact]
    public async Task RunQuarterlyAudit_WithinFirstWeek_NoLongerLogsComplete()
    {
        var (function, logger) = Build(new DateTime(2026, 7, 1, 14, 0, 0, DateTimeKind.Utc));

        await function.RunQuarterlyAudit(new TimerInfo(), CancellationToken.None);

        var informationMessages = CapturedLogs(logger)
            .Where(e => e.Level == LogLevel.Information)
            .Select(e => e.Message);
        Assert.DoesNotContain(informationMessages,
            m => m.Contains("complete", StringComparison.OrdinalIgnoreCase));
    }

    // ---------- quarterly audit: guard intact via clock seam (S6-007 test plan 2) ----------

    [Fact]
    public async Task RunQuarterlyAudit_AfterFirstWeek_ReturnsWithoutLoggingWarning()
    {
        // Day 8 (> 7): the existing guard must return before any logging.
        var (function, logger) = Build(new DateTime(2026, 7, 8, 14, 0, 0, DateTimeKind.Utc));

        await function.RunQuarterlyAudit(new TimerInfo(), CancellationToken.None);

        Assert.Empty(CapturedLogs(logger));
    }

    // ---------- monthly reinvest: thin wrapper (S6-001 test plan 11) ----------

    [Fact]
    public async Task RunMonthlyReinvest_ServiceUnregistered_WarnsAndReturnsWithoutThrowing()
    {
        // ADR-024 null-tolerant resolve: a missing registration degrades, never crashes.
        var (function, logger) = Build(FirstWeekdayJul2026);

        await function.RunMonthlyReinvest(new TimerInfo(), CancellationToken.None);

        Assert.Contains(CapturedLogs(logger),
            e => e.Level == LogLevel.Warning && e.Message.Contains("IIncomeReinvestService not registered"));
    }

    [Fact]
    public async Task RunMonthlyReinvest_DelegatesWithRunIdAndPinnedClock_AndLogsResult()
    {
        var reinvest = new Mock<IIncomeReinvestService>();
        reinvest.Setup(s => s.RunAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IncomeReinvestResult
            {
                BrokerConnected = true,
                PlanGenerated = true,
                ProposedBuyCount = 3,
                TotalProposedAmount = 15_000m,
                OrdersPlaced = 0,
                ReportSent = true
            });

        var (function, logger) = Build(FirstWeekdayJul2026,
            s => s.AddSingleton(reinvest.Object));

        await function.RunMonthlyReinvest(new TimerInfo(), CancellationToken.None);

        // Delegated exactly once, with the pinned clock value and an 8-char runId.
        reinvest.Verify(s => s.RunAsync(
                It.Is<string>(id => id.Length == 8),
                FirstWeekdayJul2026,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // The structured result is logged (run-record evidence).
        Assert.Contains(CapturedLogs(logger),
            e => e.Level == LogLevel.Information &&
                 e.Message.Contains("OrdersPlaced: 0") &&
                 e.Message.Contains("ProposedBuyCount: 3"));
    }

    [Fact]
    public async Task RunMonthlyReinvest_ResultWarnings_AreSurfacedAsWarningLog()
    {
        var reinvest = new Mock<IIncomeReinvestService>();
        reinvest.Setup(s => s.RunAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IncomeReinvestResult
            {
                BrokerConnected = true,
                PlanGenerated = true,
                Warnings = { "Income reinvest report failed (HttpRequestException)." }
            });

        var (function, logger) = Build(FirstWeekdayJul2026, s => s.AddSingleton(reinvest.Object));

        await function.RunMonthlyReinvest(new TimerInfo(), CancellationToken.None);

        Assert.Contains(CapturedLogs(logger),
            e => e.Level == LogLevel.Warning && e.Message.Contains("Income reinvest report failed"));
    }

    [Fact]
    public async Task RunMonthlyReinvest_AfterFirstWeek_GuardSkipsBeforeResolvingService()
    {
        // Day 8 (> 7): the cheap pre-filter returns before any resolve/log — the
        // authoritative first-weekday gate lives in IncomeReinvestService (Default D3).
        var reinvest = new Mock<IIncomeReinvestService>(MockBehavior.Strict);
        var (function, logger) = Build(
            new DateTime(2026, 7, 8, 13, 30, 0, DateTimeKind.Utc),
            s => s.AddSingleton(reinvest.Object));

        await function.RunMonthlyReinvest(new TimerInfo(), CancellationToken.None);

        Assert.Empty(CapturedLogs(logger));
        reinvest.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RunMonthlyReinvest_ServiceThrows_SendsOrchestrationFailureAlert_ThenRethrows()
    {
        var reinvest = new Mock<IIncomeReinvestService>();
        reinvest.Setup(s => s.RunAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("secret-bearing detail that must not leak"));

        var alerts = new Mock<IOperationalAlertService>();

        var (function, _) = Build(FirstWeekdayJul2026, s =>
        {
            s.AddSingleton(reinvest.Object);
            s.AddSingleton(alerts.Object);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            function.RunMonthlyReinvest(new TimerInfo(), CancellationToken.None));

        // Exactly one operational alert, exception TYPE NAME only — never the message.
        alerts.Verify(a => a.SendOperationalAlertAsync(
                "Orchestration Run Failure — Monthly Reinvest",
                It.Is<string>(d => d.Contains(nameof(InvalidOperationException)) &&
                                   !d.Contains("secret-bearing")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunMonthlyReinvest_ServiceThrows_NoAlertServiceRegistered_StillRethrows()
    {
        var reinvest = new Mock<IIncomeReinvestService>();
        reinvest.Setup(s => s.RunAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var (function, _) = Build(FirstWeekdayJul2026, s => s.AddSingleton(reinvest.Object));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            function.RunMonthlyReinvest(new TimerInfo(), CancellationToken.None));
    }

    [Fact]
    public async Task RunMonthlyReinvest_AlertSendFailure_DoesNotMaskTheOriginalException()
    {
        var reinvest = new Mock<IIncomeReinvestService>();
        reinvest.Setup(s => s.RunAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("original failure"));

        var alerts = new Mock<IOperationalAlertService>();
        alerts.Setup(a => a.SendOperationalAlertAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("alert transport down"));

        var (function, _) = Build(FirstWeekdayJul2026, s =>
        {
            s.AddSingleton(reinvest.Object);
            s.AddSingleton(alerts.Object);
        });

        // The ORIGINAL exception type propagates — the alert failure is swallowed.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            function.RunMonthlyReinvest(new TimerInfo(), CancellationToken.None));
    }
}
