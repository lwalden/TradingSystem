using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Strategies.Income;

namespace TradingSystem.Functions;

/// <summary>
/// S6-001 monthly reinvest pipeline (Default D2 — the S5-001 thin-timer shape):
/// first-trading-weekday gate (Default D3, in code — NCRONTAB day-of-month/day-of-week
/// semantics are never trusted) → broker connect (failure = S5-003 operational alert +
/// structured degrade, Default D7) → recommendation plan through the EXISTING
/// <see cref="IncomeSleeveManager"/> (10% issuer / 40% category caps enforced at plan time —
/// reused, never duplicated) → flag-gated paper order placement
/// (<see cref="IncomeSleeveConfig.OrderPlacementEnabled"/>, default FALSE — locked decision 1)
/// → best-effort Discord plan report (Default D6; report failure can never fail the run) →
/// disconnect in finally.
///
/// Recommendation-cash input (Default D4): availableCash = Account.TotalCashValue ×
/// TradingSystemConfig.IncomeTargetPercent (the existing MonthlyReinvestStrategy heuristic).
/// There is no dividend-activity data source yet, so ReinvestmentPlan.DividendsReceived /
/// InterestReceived stay 0 — fields are left default-valued rather than guessed (S5-001
/// fidelity rule). An empty sleeve legitimately yields an empty plan and the report still
/// goes out saying no buys are proposed (Default D8) — no bootstrap/seeding logic here
/// (that is a human capital-allocation action, ADR-021).
/// </summary>
public class IncomeReinvestService : IIncomeReinvestService
{
    private readonly IBrokerService _broker;
    private readonly IncomeSleeveManager _sleeveManager;
    private readonly TradingSystemConfig _config;
    private readonly IncomeSleeveConfig _sleeveConfig;
    private readonly ILogger<IncomeReinvestService> _logger;
    private readonly IIncomeReportService? _reportService;
    private readonly IOperationalAlertService? _operationalAlertService;

    // S5-003 alert-spam guard (Default D7): once per run per failure category, keyed
    // runId:category — same pattern as EndOfDayService/DailyOrchestrator. The timer fires
    // once a month with a fresh runId, so this stays tiny over a long-lived singleton.
    private readonly object _alertGateLock = new();
    private readonly HashSet<string> _alertedRunCategories = new(StringComparer.Ordinal);

    public IncomeReinvestService(
        IBrokerService broker,
        IncomeSleeveManager sleeveManager,
        IOptions<TradingSystemConfig> config,
        IOptions<IncomeSleeveConfig> sleeveConfig,
        ILogger<IncomeReinvestService> logger,
        IIncomeReportService? reportService = null,
        IOperationalAlertService? operationalAlertService = null)
    {
        _broker = broker;
        _sleeveManager = sleeveManager;
        _config = config.Value;
        _sleeveConfig = sleeveConfig.Value;
        _logger = logger;
        _reportService = reportService;
        _operationalAlertService = operationalAlertService;
    }

    public async Task<IncomeReinvestResult> RunAsync(
        string runId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var result = new IncomeReinvestResult();

        // Default D3: the AUTHORITATIVE schedule filter. The cron restricts day-of-month AND
        // day-of-week, which (depending on NCRONTAB union/intersection semantics) can fire on
        // every weekday of the first week — only the computed first trading weekday proceeds.
        // (Holiday edge accepted: a market-holiday gate day degrades like any closed-market
        // day — the recommendation-only output is benign. Documented in the runbook, S6-003.)
        var gateDay = FirstTradingWeekday(utcNow);
        if (utcNow.Date != gateDay)
        {
            result.Skipped = true;
            result.SkipReason =
                $"Not the first trading weekday of the month (gate day {gateDay:yyyy-MM-dd}, fired {utcNow.Date:yyyy-MM-dd}).";
            _logger.LogInformation(
                "Monthly reinvest skipped — {SkipReason} RunId: {RunId}",
                result.SkipReason,
                runId);
            return result;
        }

        var connected = await _broker.ConnectAsync(cancellationToken);
        if (!connected)
        {
            // Default D7 (S5-003 verbatim): no plan, no report, no throw — best-effort
            // operational alert, once per run per category.
            _logger.LogWarning(
                "Could not connect to broker for monthly reinvest. No plan will be generated. RunId: {RunId}. " +
                "Verify TWS/IB Gateway is running and the API port/client-id match config.",
                runId);

            await TrySendOperationalAlertAsync(
                runId,
                "connect-failure",
                "Broker Connect Failure — Monthly Reinvest",
                $"Could not connect to the broker for the monthly reinvest run. No plan was generated and no report was sent. RunId: {runId}. " +
                "Verify TWS/IB Gateway is running and the API port/client-id match config.",
                cancellationToken);

            return result;
        }

        result.BrokerConnected = true;
        try
        {
            // Default D4: recommendation-cash input — the existing MonthlyReinvestStrategy
            // heuristic, recorded in the plan (and rendered in the report) so the owner sees it.
            var account = await _broker.GetAccountAsync(cancellationToken);
            var availableCash = account.TotalCashValue * _config.IncomeTargetPercent;
            _logger.LogInformation(
                "Monthly reinvest cash input (estimate): {AvailableCash:C} = TotalCashValue {TotalCash:C} × IncomeTargetPercent {Target:P0}. RunId: {RunId}",
                availableCash,
                account.TotalCashValue,
                _config.IncomeTargetPercent,
                runId);

            // Recommendation path — ALWAYS. Caps (10% issuer / 40% category, post-buy
            // semantics) are enforced inside the existing plan generator; never duplicated here.
            var state = await _sleeveManager.GetSleeveStateAsync(cancellationToken);
            var plan = _sleeveManager.GenerateReinvestmentPlan(state, availableCash, cancellationToken);
            result.PlanGenerated = true;
            result.ProposedBuyCount = plan.ProposedBuys.Count;
            result.TotalProposedAmount = plan.TotalProposedAmount;

            if (_sleeveConfig.OrderPlacementEnabled)
            {
                // Owner-gated paper order path: the EXISTING IncomeSleeveManager execution
                // pipeline (live quotes → limit orders via IExecutionService). No new
                // execution logic, no mode logic.
                var executions = await _sleeveManager.ExecuteReinvestmentPlanAsync(plan, cancellationToken);
                result.OrdersPlaced = executions.Count(e => e.Success);
                _logger.LogInformation(
                    "Monthly reinvest posture: order placement ENABLED (IncomeSleeve:OrderPlacementEnabled=true). " +
                    "OrdersPlaced: {OrdersPlaced}/{Proposed}. RunId: {RunId}",
                    result.OrdersPlaced,
                    result.ProposedBuyCount,
                    runId);
            }
            else
            {
                // LOCKED DECISION 1: the default posture places NO orders — state it in the
                // run record so the (expected) absence of TWS orders is auditable.
                _logger.LogInformation(
                    "Monthly reinvest posture: recommendation-only; IncomeSleeve:OrderPlacementEnabled=false. " +
                    "No orders will be placed. RunId: {RunId}",
                    runId);
            }

            // Default D6/D8: best-effort report on every non-skipped, connected run —
            // including empty plans ("no buys proposed" is proof the timer ran). Report
            // failure degrades to a warning and can never fail the run.
            await TrySendReportAsync(runId, plan, state, result, cancellationToken);

            return result;
        }
        finally
        {
            await _broker.DisconnectAsync();
        }
    }

    /// <summary>
    /// First Mon–Fri day of <paramref name="utcNow"/>'s month — the gate day (Default D3).
    /// </summary>
    internal static DateTime FirstTradingWeekday(DateTime utcNow)
    {
        var day = new DateTime(utcNow.Year, utcNow.Month, 1);
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            day = day.AddDays(1);
        }
        return day;
    }

    private async Task TrySendReportAsync(
        string runId,
        ReinvestmentPlan plan,
        IncomeSleeveState state,
        IncomeReinvestResult result,
        CancellationToken cancellationToken)
    {
        if (_reportService == null)
        {
            _logger.LogDebug(
                "IIncomeReportService not registered; reinvest plan report skipped. RunId: {RunId}",
                runId);
            return;
        }

        try
        {
            await _reportService.SendReinvestmentPlanReportAsync(plan, state, result.OrdersPlaced, cancellationToken);
            result.ReportSent = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Observability is never control: exception TYPE NAME only (messages can echo
            // the token-bearing webhook URI), warning + structured Warnings entry, no throw.
            _logger.LogWarning(
                "Income reinvest plan report failed ({ErrorType}); run outcome unaffected. RunId: {RunId}",
                ex.GetType().Name,
                runId);
            result.Warnings.Add($"Income reinvest report failed ({ex.GetType().Name}).");
        }
    }

    /// <summary>
    /// S5-003 best-effort operational alert: null-tolerant, never throws (alert failure must
    /// never fail or replace the run's outcome), once per run per failure category.
    /// </summary>
    private async Task TrySendOperationalAlertAsync(
        string runId,
        string category,
        string title,
        string description,
        CancellationToken cancellationToken)
    {
        if (_operationalAlertService == null)
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
            await _operationalAlertService.SendOperationalAlertAsync(title, description, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Operational alert send failed ({ErrorType}); reinvest outcome unaffected. RunId: {RunId}, Category: {Category}",
                ex.GetType().Name,
                runId,
                category);
        }
    }
}
