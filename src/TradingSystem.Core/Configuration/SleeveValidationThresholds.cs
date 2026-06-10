namespace TradingSystem.Core.Configuration;

/// <summary>
/// Per-sleeve paper-validation thresholds (PDR-004 / ADR-010). Evaluation-only: persisted via
/// the IConfigRepository settings seam (NOT on TradingSystemConfig, which carries trade-affecting
/// risk params) and never read by any order/execution path.
/// </summary>
public class SleeveValidationThresholds
{
    // Key used with IConfigRepository.GetSettingAsync/SetSettingAsync.
    public const string SettingsKey = "sleeveValidationThresholds";

    public SleeveThresholds Income { get; set; } = new();
    public SleeveThresholds Options { get; set; } = new();

    // ADR-010 values (owner-confirmed 2026-06-09 as authoritative, identical for both sleeves):
    // hit rate >=45%, profit factor >=1.3, max drawdown <=15%, minimum 12 weeks observed,
    // and "profitable OR outperform S&P 500" required.
    public static SleeveValidationThresholds Defaults() => new();
}

/// <summary>
/// Validation thresholds for a single sleeve, plus the pure evaluation against observed metrics.
/// </summary>
public class SleeveThresholds
{
    public decimal MinHitRatePercent { get; set; } = 45m;
    public decimal MinProfitFactor { get; set; } = 1.3m;
    public decimal MaxDrawdownPercent { get; set; } = 15m;
    public int MinWeeksObserved { get; set; } = 12;

    // Mirrors ADR-010's "Profitable OR outperform S&P 500". Beat-SPY is STRICT greater-than
    // (sleeve return > SPY return, no margin) — owner-confirmed 2026-06-09.
    public bool RequireProfitableOrBeatSpy { get; set; } = true;

    /// <summary>
    /// Pure evaluation of observed sleeve metrics against these thresholds. Below the minimum
    /// observation window the outcome is InsufficientData (not-yet-evaluable), never a premature
    /// PASS or FAIL; per-metric flags are still reported for progress visibility.
    /// </summary>
    public ThresholdResult Evaluate(SleeveMetrics actual)
    {
        var hitRatePass = actual.HitRatePercent >= MinHitRatePercent;
        var profitFactorPass = actual.ProfitFactor >= MinProfitFactor;
        var drawdownPass = actual.MaxDrawdownPercent <= MaxDrawdownPercent;
        var profitableOrBeatSpyPass = !RequireProfitableOrBeatSpy
            || actual.TotalReturnPercent > 0m
            || actual.TotalReturnPercent > actual.SpyReturnPercent;
        var weeksObservedMet = actual.WeeksObserved >= MinWeeksObserved;

        var outcome = !weeksObservedMet
            ? ValidationOutcome.InsufficientData
            : hitRatePass && profitFactorPass && drawdownPass && profitableOrBeatSpyPass
                ? ValidationOutcome.Pass
                : ValidationOutcome.Fail;

        return new ThresholdResult
        {
            HitRatePass = hitRatePass,
            ProfitFactorPass = profitFactorPass,
            DrawdownPass = drawdownPass,
            ProfitableOrBeatSpyPass = profitableOrBeatSpyPass,
            WeeksObservedMet = weeksObservedMet,
            Outcome = outcome
        };
    }
}

/// <summary>
/// Observed paper-trading metrics for one sleeve over the validation window.
/// </summary>
public class SleeveMetrics
{
    public decimal HitRatePercent { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal MaxDrawdownPercent { get; set; }
    public int WeeksObserved { get; set; }
    public decimal TotalReturnPercent { get; set; }
    public decimal SpyReturnPercent { get; set; }
}

/// <summary>
/// Per-metric pass/fail flags plus the overall validation outcome for one sleeve.
/// </summary>
public class ThresholdResult
{
    public bool HitRatePass { get; init; }
    public bool ProfitFactorPass { get; init; }
    public bool DrawdownPass { get; init; }
    public bool ProfitableOrBeatSpyPass { get; init; }
    public bool WeeksObservedMet { get; init; }
    public ValidationOutcome Outcome { get; init; }
}

public enum ValidationOutcome
{
    // Observation window shorter than MinWeeksObserved — not yet evaluable (distinct from Fail).
    InsufficientData,
    Pass,
    Fail
}
