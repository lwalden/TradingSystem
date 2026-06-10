using TradingSystem.Core.Configuration;

namespace TradingSystem.Core.Models;

/// <summary>
/// Overall readiness verdict for one sleeve. Maps 1:1 from <see cref="ValidationOutcome"/>:
/// InsufficientData (window/sample too small to judge), NotReady (judged and failing),
/// Ready (judged and passing all metric gates).
/// </summary>
public enum SleeveReadinessState
{
    InsufficientData,
    NotReady,
    Ready
}

/// <summary>
/// Weekly paper-validation readiness scorecard for one sleeve (S4-002 / PDR-004).
/// Recommendation-only: this object describes readiness against the
/// <see cref="SleeveValidationThresholds"/> gate — it never changes mode, risk
/// parameters, sleeve weights, or orders, and producing it writes nothing anywhere.
/// </summary>
public class SleeveReadinessScorecard
{
    /// <summary>Display name matching the thresholds object ("Income" / "Options").</summary>
    public string SleeveName { get; init; } = string.Empty;

    /// <summary>Portfolio sleeve the metrics were attributed to (Options maps to Tactical).</summary>
    public SleeveType Sleeve { get; init; }

    /// <summary>Evaluation date the observation window ends at.</summary>
    public DateTime AsOf { get; init; }

    /// <summary>
    /// Per-metric pass/fail detail from <see cref="SleeveThresholds.Evaluate"/> — carries the
    /// observed <see cref="SleeveMetrics"/> and a snapshot of the applied thresholds.
    /// </summary>
    public ThresholdResult Evaluation { get; init; } = new();

    /// <summary>
    /// True when the profit factor is structurally undefined for the window: zero losing
    /// trades with at least one winner, so the gross-profit/gross-loss ratio has no value.
    /// <see cref="ThresholdResult.ActualMetrics"/>.ProfitFactor then carries a gate-passing
    /// sentinel; renderers must branch on this flag and show "∞ / no losses" instead of the
    /// raw sentinel value.
    /// </summary>
    public bool IsProfitFactorUndefined { get; init; }

    /// <summary>Overall readiness verdict (metrics only — capital gate reported separately).</summary>
    public SleeveReadinessState Readiness { get; init; }

    /// <summary>Closed trades attributed to this sleeve inside the observation window.</summary>
    public int ClosedTradeCount { get; init; }

    /// <summary>Latest observed sleeve value (0 when the sleeve has no snapshot history).</summary>
    public decimal CurrentSleeveValue { get; init; }

    /// <summary>The configured minimum live capital this sleeve was checked against.</summary>
    public decimal MinimumLiveCapital { get; init; }

    /// <summary>
    /// Capital gate: current sleeve value at-or-above <see cref="MinimumLiveCapital"/>.
    /// A metrics-Ready sleeve below the minimum stays Ready on metrics but the
    /// recommendation is capital-gated — never an activation trigger.
    /// </summary>
    public bool MeetsMinimumCapital { get; init; }

    /// <summary>
    /// Deterministic sample-size confidence in [0,1]: monotonically non-decreasing in both
    /// weeks observed and closed-trade count. Not an AI output.
    /// </summary>
    public decimal Confidence { get; init; }

    /// <summary>
    /// Human-readable explanation: names failing metrics with actual-vs-threshold values,
    /// says which profitability sub-condition fired, and notes the capital gate when it binds.
    /// </summary>
    public string Rationale { get; init; } = string.Empty;
}
