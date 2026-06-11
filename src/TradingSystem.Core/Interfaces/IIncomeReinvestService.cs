namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Monthly income-sleeve reinvest pipeline (S6-001, Default D2 — the S5-001 thin-timer shape):
/// first-trading-weekday gate (in code, never trusted to NCRONTAB — Default D3) → broker
/// connect → recommendation plan via the existing IncomeSleeveManager (caps enforced at plan
/// time; never duplicated) → flag-gated paper order placement
/// (<see cref="TradingSystem.Core.Configuration.IncomeSleeveConfig.OrderPlacementEnabled"/>,
/// default FALSE — locked decision 1: the default posture places NO orders) → best-effort
/// Discord plan report. The timer wrapper resolves this null-tolerantly (ADR-024) and owns
/// the catch → operational-alert → rethrow contract (Default D7).
/// </summary>
public interface IIncomeReinvestService
{
    /// <summary>
    /// Runs the monthly reinvest pipeline. <paramref name="utcNow"/> comes from the timer's
    /// injected clock seam (S6-007, Default D1) so the first-weekday gate is testable.
    /// </summary>
    Task<IncomeReinvestResult> RunAsync(string runId, DateTime utcNow, CancellationToken cancellationToken = default);
}

/// <summary>
/// Structured outcome of a monthly reinvest run (EndOfDayResult precedent). Consumed by the
/// timer wrapper's result log and by tests — notably the sprint's primary invariant:
/// flag off ⇒ <see cref="OrdersPlaced"/> is 0 and IExecutionService is never called.
/// </summary>
public class IncomeReinvestResult
{
    /// <summary>True when the first-trading-weekday gate (Default D3) skipped the run.</summary>
    public bool Skipped { get; set; }

    /// <summary>Why the run was skipped (gate day mismatch). Null when not skipped.</summary>
    public string? SkipReason { get; set; }

    /// <summary>Whether the broker connection succeeded. False → no plan, no report (Default D7).</summary>
    public bool BrokerConnected { get; set; }

    /// <summary>Whether a ReinvestmentPlan was generated (an EMPTY plan still counts — Default D8).</summary>
    public bool PlanGenerated { get; set; }

    /// <summary>Number of proposed buys in the plan (0 is a legitimate outcome — Default D8).</summary>
    public int ProposedBuyCount { get; set; }

    /// <summary>Total dollar amount across proposed buys.</summary>
    public decimal TotalProposedAmount { get; set; }

    /// <summary>
    /// Paper orders actually placed. ALWAYS 0 when
    /// IncomeSleeve:OrderPlacementEnabled is false (locked decision 1).
    /// </summary>
    public int OrdersPlaced { get; set; }

    /// <summary>Whether the plan report send path completed without error (best-effort, Default D6).</summary>
    public bool ReportSent { get; set; }

    /// <summary>Non-fatal degradations (e.g. report delivery failure). The run still succeeds with these present.</summary>
    public List<string> Warnings { get; set; } = new();
}
