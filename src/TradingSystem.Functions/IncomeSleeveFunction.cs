using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;

namespace TradingSystem.Functions;

/// <summary>
/// Monthly income sleeve reinvestment function
/// </summary>
public class IncomeSleeveFunction
{
    private readonly ILogger<IncomeSleeveFunction> _logger;
    private readonly TradingSystemConfig _config;
    private readonly IServiceProvider _serviceProvider;
    private readonly Func<DateTime> _utcNow;

    // S5-003 alert-spam guard (Default D7): once per run per failure category, keyed
    // runId:category — same pattern as DailyOrchestrator. The reinvest timer fires once a
    // month at most, so this stays tiny over a long-lived instance.
    private readonly object _alertGateLock = new();
    private readonly HashSet<string> _alertedRunCategories = new(StringComparer.Ordinal);

    public IncomeSleeveFunction(
        ILogger<IncomeSleeveFunction> logger,
        IOptions<TradingSystemConfig> config,
        IServiceProvider serviceProvider)
        : this(logger, config, serviceProvider, () => DateTime.UtcNow)
    {
    }

    /// <summary>
    /// Testable-clock seam (S6-007, Default D1): tests pin the day-of-month guard;
    /// S6-001 reuses this seam for the first-trading-weekday gate.
    /// </summary>
    internal IncomeSleeveFunction(
        ILogger<IncomeSleeveFunction> logger,
        IOptions<TradingSystemConfig> config,
        IServiceProvider serviceProvider,
        Func<DateTime> utcNow)
    {
        _logger = logger;
        _config = config.Value;
        _serviceProvider = serviceProvider;
        _utcNow = utcNow;
    }

    /// <summary>
    /// Monthly reinvest - First trading day of month at 6:30 AM PT.
    /// S6-001 thin timer wrapper (S5-001 / DailyOrchestrator EOD pattern): the pipeline lives
    /// in <see cref="IIncomeReinvestService"/>. Default posture (locked decision 1):
    /// recommendation-only — plan + Discord report, NO orders, unless the owner has flipped
    /// IncomeSleeve:OrderPlacementEnabled to true.
    /// </summary>
    [Function("IncomeSleeve_MonthlyReinvest")]
    public async Task RunMonthlyReinvest(
        [TimerTrigger("0 30 13 1-7 * 1-5")] TimerInfo timer, // First Mon-Fri, days 1-7
        CancellationToken cancellationToken)
    {
        // Cheap pre-filter, belt-and-braces only: the AUTHORITATIVE first-trading-weekday
        // gate lives in IncomeReinvestService (Default D3) — NCRONTAB day-of-month/day-of-week
        // union-vs-intersection semantics are never trusted to do this filtering.
        if (_utcNow().Day > 7) return;

        var runId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("Starting monthly income reinvest. RunId: {RunId}", runId);

        try
        {
            // Null-tolerant resolve (ADR-024): a missing registration degrades, never crashes.
            var reinvestService = _serviceProvider.GetService<IIncomeReinvestService>();
            if (reinvestService == null)
            {
                _logger.LogWarning(
                    "IIncomeReinvestService not registered. Skipping monthly reinvest. RunId: {RunId}",
                    runId);
                return;
            }

            var result = await reinvestService.RunAsync(runId, _utcNow(), cancellationToken);

            if (result.Warnings.Count > 0)
            {
                _logger.LogWarning(
                    "Monthly reinvest warnings. RunId: {RunId}. {Warnings}",
                    runId,
                    string.Join(" | ", result.Warnings));
            }

            _logger.LogInformation(
                "Monthly income reinvest finished. RunId: {RunId}, Skipped: {Skipped}, BrokerConnected: {BrokerConnected}, PlanGenerated: {PlanGenerated}, ProposedBuyCount: {ProposedBuyCount}, TotalProposedAmount: {TotalProposedAmount:C}, OrdersPlaced: {OrdersPlaced}, ReportSent: {ReportSent}, SkipReason: {SkipReason}",
                runId,
                result.Skipped,
                result.BrokerConnected,
                result.PlanGenerated,
                result.ProposedBuyCount,
                result.TotalProposedAmount,
                result.OrdersPlaced,
                result.ReportSent,
                result.SkipReason ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Monthly income reinvest failed. RunId: {RunId}", runId);

            // Default D7 (S5-003): operational orchestration-failure alert BEFORE the rethrow.
            // Exception TYPE NAME only — messages can echo URIs/secrets. CancellationToken.None:
            // a cancelled run must still be able to alert.
            await TrySendOperationalAlertAsync(
                runId,
                "orchestration-failure",
                "Orchestration Run Failure — Monthly Reinvest",
                $"Unhandled {ex.GetType().Name} during the monthly reinvest run. RunId: {runId}. " +
                "The reinvest plan/report may not have been produced. See the worker logs for details.",
                CancellationToken.None);

            throw;
        }
    }

    /// <summary>
    /// S5-003 best-effort operational alert (DailyOrchestrator pattern): null-tolerant
    /// resolve, never throws (alert failure must never mask or replace the run's outcome),
    /// gated to once per run per failure category. Exception TYPE NAMES only ever reach
    /// alerts/logs here — never messages, which can echo URIs/secrets.
    /// </summary>
    private async Task TrySendOperationalAlertAsync(
        string runId,
        string category,
        string title,
        string description,
        CancellationToken cancellationToken)
    {
        var operationalAlerts = _serviceProvider.GetService<IOperationalAlertService>();
        if (operationalAlerts == null)
            return;

        bool claimed;
        lock (_alertGateLock)
        {
            claimed = _alertedRunCategories.Add($"{runId}:{category}");
        }

        if (!claimed)
        {
            _logger.LogDebug(
                "Operational alert already sent for this run/category; skipping. RunId: {RunId}, Category: {Category}",
                runId,
                category);
            return;
        }

        try
        {
            await operationalAlerts.SendOperationalAlertAsync(title, description, cancellationToken);
        }
        catch (Exception ex)
        {
            // Observability must never become control: swallow and log (type name only).
            _logger.LogWarning(
                "Operational alert send failed ({ErrorType}); reinvest outcome unaffected. RunId: {RunId}, Category: {Category}",
                ex.GetType().Name,
                runId,
                category);
        }
    }

    /// <summary>
    /// Quarterly quality audit - First week of Jan, Apr, Jul, Oct
    /// </summary>
    [Function("IncomeSleeve_QuarterlyAudit")]
    public async Task RunQuarterlyAudit(
        [TimerTrigger("0 0 14 1-7 1,4,7,10 1-5")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        if (_utcNow().Day > 7) return;

        var runId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("Starting quarterly quality audit. RunId: {RunId}", runId);

        try
        {
            // TODO: Implement with Claude AI
            // 1. Pull current holdings
            // 2. Fetch quality metrics (NII, FFO, ROC, etc.)
            // 3. Call Claude for analysis
            // 4. Flag securities needing attention
            // 5. Generate reduction signals if needed
            // 6. Send report to owner

            // S6-007 stub honesty: during the paper-validation run, logs are evidence.
            // Do NOT log "complete" for work that never happened.
            _logger.LogWarning(
                "IncomeSleeve_QuarterlyAudit is not implemented — skipped (deferred per backlog, S7+). RunId: {RunId}",
                runId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Quarterly quality audit failed. RunId: {RunId}", runId);
            throw;
        }
    }
}
