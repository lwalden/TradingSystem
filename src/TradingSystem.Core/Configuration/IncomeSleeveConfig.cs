namespace TradingSystem.Core.Configuration;

/// <summary>
/// Owner-gated operational config for the income sleeve timer (S6-001, Default D5). Bound
/// from the "IncomeSleeve" config section (ReportingConfig precedent). This is an operational
/// gate, NOT risk config — it must never live in TradingSystemConfig/RiskConfig/IncomeConfig,
/// and changing it never alters any deterministic trading rule, cap, or sleeve allocation.
/// </summary>
public class IncomeSleeveConfig
{
    /// <summary>
    /// LOCKED DECISION (S6 #1): the default posture places NO orders. The monthly reinvest
    /// timer always computes the ReinvestmentPlan and sends the Discord report; only when the
    /// OWNER explicitly flips this to true does the existing paper limit-order path
    /// (IncomeSleeveManager.ExecuteReinvestmentPlanAsync) engage. The income sleeve's PDR-004
    /// ≥12-week clock starts at its first actual trade — not at plan generation.
    /// </summary>
    public bool OrderPlacementEnabled { get; set; } = false;
}
