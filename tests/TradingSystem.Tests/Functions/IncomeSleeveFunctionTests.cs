using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TradingSystem.Core.Configuration;
using TradingSystem.Functions;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace TradingSystem.Tests.Functions;

/// <summary>
/// S6-007: quarterly-audit stub honesty. During the live paper-validation run, logs are
/// evidence — a TODO stub that logs "Quarterly quality audit complete" while doing nothing
/// would falsify the run record in the Jul 1–7 audit window. The stub must instead log an
/// explicit Warning that it is not implemented and was skipped. Also covers the internal
/// Func&lt;DateTime&gt; clock seam (Default D1) that S6-001 rebases on: both timer methods
/// read the injected clock instead of DateTime.UtcNow, so the day-of-month guard is testable.
/// No Discord send, no cron change, no behavior added.
/// </summary>
public class IncomeSleeveFunctionTests
{
    // ---------- harness ----------

    private static (IncomeSleeveFunction Function, Mock<ILogger<IncomeSleeveFunction>> Logger) Build(DateTime pinnedUtcNow)
    {
        var logger = new Mock<ILogger<IncomeSleeveFunction>>();
        var config = MsOptions.Create(new TradingSystemConfig());
        var function = new IncomeSleeveFunction(logger.Object, config, () => pinnedUtcNow);
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

    // ---------- monthly reinvest: untouched in this item (S6-007 test plan 4) ----------

    [Fact]
    public async Task RunMonthlyReinvest_WithinFirstWeek_BehaviorUnchanged_StillLogsComplete()
    {
        // S6-001 owns the reinvest wiring; this item must not change its behavior.
        var (function, logger) = Build(new DateTime(2026, 7, 1, 13, 30, 0, DateTimeKind.Utc));

        await function.RunMonthlyReinvest(new TimerInfo(), CancellationToken.None);

        var informationMessages = CapturedLogs(logger)
            .Where(e => e.Level == LogLevel.Information)
            .Select(e => e.Message)
            .ToList();
        Assert.Contains(informationMessages, m => m.Contains("Monthly income reinvest complete"));
    }

    [Fact]
    public async Task RunMonthlyReinvest_AfterFirstWeek_GuardUsesInjectedClock()
    {
        // Proves the monthly method reads the seam (S6-001 rebases on this), guard intact.
        var (function, logger) = Build(new DateTime(2026, 7, 8, 13, 30, 0, DateTimeKind.Utc));

        await function.RunMonthlyReinvest(new TimerInfo(), CancellationToken.None);

        Assert.Empty(CapturedLogs(logger));
    }
}
