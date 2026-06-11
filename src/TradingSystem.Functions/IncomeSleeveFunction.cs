using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Core.Configuration;

namespace TradingSystem.Functions;

/// <summary>
/// Monthly income sleeve reinvestment function
/// </summary>
public class IncomeSleeveFunction
{
    private readonly ILogger<IncomeSleeveFunction> _logger;
    private readonly TradingSystemConfig _config;
    private readonly Func<DateTime> _utcNow;

    public IncomeSleeveFunction(
        ILogger<IncomeSleeveFunction> logger,
        IOptions<TradingSystemConfig> config)
        : this(logger, config, () => DateTime.UtcNow)
    {
    }

    /// <summary>
    /// Testable-clock seam (S6-007, Default D1): tests pin the day-of-month guard;
    /// S6-001 reuses this seam for the first-trading-weekday gate.
    /// </summary>
    internal IncomeSleeveFunction(
        ILogger<IncomeSleeveFunction> logger,
        IOptions<TradingSystemConfig> config,
        Func<DateTime> utcNow)
    {
        _logger = logger;
        _config = config.Value;
        _utcNow = utcNow;
    }

    /// <summary>
    /// Monthly reinvest - First trading day of month at 6:30 AM PT
    /// </summary>
    [Function("IncomeSleeve_MonthlyReinvest")]
    public async Task RunMonthlyReinvest(
        [TimerTrigger("0 30 13 1-7 * 1-5")] TimerInfo timer, // First Mon-Fri, days 1-7
        CancellationToken cancellationToken)
    {
        // Only run on the first weekday of the month
        if (_utcNow().Day > 7) return;
        
        var runId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("Starting monthly income reinvest. RunId: {RunId}", runId);

        try
        {
            // TODO: Implement
            // 1. Pull sleeve positions and cash
            // 2. Get dividends/interest received this month
            // 3. Compute current weights vs targets
            // 4. Screen income universe with quality gates
            // 5. Build buy list to reduce drift
            // 6. Execute with limit orders
            // 7. Verify caps respected
            // 8. Log rebalance summary

            _logger.LogInformation("Monthly income reinvest complete. RunId: {RunId}", runId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Monthly income reinvest failed. RunId: {RunId}", runId);
            throw;
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
