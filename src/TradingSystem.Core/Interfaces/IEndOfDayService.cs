namespace TradingSystem.Core.Interfaces;

/// <summary>
/// End-of-day pipeline (S5-001): broker position/P&amp;L sync, stop-trigger check, and
/// DailySnapshot persistence + enrichment. The spine delegates to
/// <see cref="IRiskManager.GetRiskMetricsAsync"/> (existing semantics — alert-only stop
/// evaluation, transition-gated alerts, base-snapshot upsert) and adds a best-effort
/// enrichment layer (activity + market context) as a second upsert.
/// </summary>
public interface IEndOfDayService
{
    Task<EndOfDayResult> RunAsync(string runId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Structured outcome of an end-of-day run. Consumed by tests and by operational
/// alerting (S5-003 alerts on the failure shapes — e.g. <see cref="BrokerConnected"/> false).
/// </summary>
public class EndOfDayResult
{
    /// <summary>Whether the broker connection succeeded. False → no snapshot was written (never persist stale data).</summary>
    public bool BrokerConnected { get; set; }

    /// <summary>Whether today's DailySnapshot was persisted (base upsert via RiskManager).</summary>
    public bool SnapshotPersisted { get; set; }

    /// <summary>Whether any stop (daily/weekly/drawdown) is triggered per existing RiskManager evaluation. Alert-only — no halt action.</summary>
    public bool StopTriggered { get; set; }

    /// <summary>Non-fatal degradations (e.g. enrichment failure). The run still succeeds with these present.</summary>
    public List<string> Warnings { get; set; } = new();
}
