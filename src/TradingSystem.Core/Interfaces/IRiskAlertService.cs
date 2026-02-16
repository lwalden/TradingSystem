namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Sends risk alerts when hard stops are triggered.
/// </summary>
public interface IRiskAlertService
{
    Task SendDailyStopTriggeredAsync(RiskMetrics metrics, CancellationToken cancellationToken = default);
    Task SendWeeklyStopTriggeredAsync(RiskMetrics metrics, CancellationToken cancellationToken = default);
    Task SendDrawdownHaltTriggeredAsync(RiskMetrics metrics, CancellationToken cancellationToken = default);
}
