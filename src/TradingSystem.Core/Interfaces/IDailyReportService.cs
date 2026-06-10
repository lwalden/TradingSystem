namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Sends the end-of-day rich digest for one trading day (S4-003 / B-004): executed trades,
/// realized/unrealized P&amp;L, open positions, market regime, and the ADR-023 cost breakout
/// (platform vs brokerage — never conflated). Read-only over repositories: implementations
/// never place orders, never change mode/risk parameters, and degrade to logs on delivery
/// failure (a missed report is never an operational stop).
/// </summary>
public interface IDailyReportService
{
    /// <summary>
    /// Builds and delivers the daily report for the trading day <paramref name="date"/>
    /// (time component ignored). Returns without throwing on delivery failure.
    /// </summary>
    Task SendDailyReportAsync(DateTime date, CancellationToken cancellationToken = default);
}
