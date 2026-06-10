namespace TradingSystem.Core.Configuration;

/// <summary>
/// Operational reporting cadence (S5-002, Default D7). Bound from the "Reporting" config
/// section. This is observability cadence config, NOT risk config — it must never carry
/// risk parameters or sleeve allocations (those live in <see cref="TradingSystemConfig"/>/
/// <see cref="RiskConfig"/> and require explicit human approval to change).
/// </summary>
public class ReportingConfig
{
    /// <summary>
    /// Day of week on which the daily Discord report appends the weekly sleeve-readiness
    /// scorecard embed. Default Friday (S5 locked decision 5). On all other days the daily
    /// report carries only the core digest.
    /// </summary>
    public DayOfWeek WeeklyScorecardDay { get; set; } = DayOfWeek.Friday;
}
