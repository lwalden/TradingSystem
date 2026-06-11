using TradingSystem.Core.Models;

namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Sends the monthly income reinvest plan report (S6-001, Default D6): a digest-class
/// (neutral color) Discord message — NOT an orange operational alert. Sent on every
/// non-skipped, connected reinvest run, including empty plans ("no buys proposed" is proof
/// the timer ran — Default D8). Observability, never control: implementations must degrade
/// to logs (no throw beyond caller cancellation) on delivery failure, and callers treat the
/// send as best-effort — a report failure can never fail the run or influence any trading
/// path. No secrets and no exception messages ever render in the report content.
/// </summary>
public interface IIncomeReportService
{
    /// <summary>
    /// Renders and sends the reinvestment plan report.
    /// <paramref name="ordersPlaced"/> is the number of paper orders actually placed
    /// (always 0 under the default recommendation-only posture — locked decision 1).
    /// </summary>
    Task SendReinvestmentPlanReportAsync(
        ReinvestmentPlan plan,
        IncomeSleeveState state,
        int ordersPlaced,
        CancellationToken cancellationToken = default);
}
