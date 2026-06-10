namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Sends operational (non-risk) failure alerts — broker-connect failures and orchestration-run
/// failures (S5-003). Implemented by the same Discord sender as <see cref="IRiskAlertService"/>
/// (Default D5: same webhook/config/named client/S3-004 hardening), but rendered as a
/// warning-orange embed so an ops failure is visually distinct from a capital-preservation
/// risk stop. Alerting is observability, never control: implementations must degrade to logs
/// (no throw) on delivery failure, and callers must treat the send as best-effort — an alert
/// failure can never fail a run or influence any trading path.
///
/// Content rule: descriptions may carry run identifiers and exception TYPE NAMES only — never
/// exception messages/ToString(), which can echo URIs or secrets.
/// </summary>
public interface IOperationalAlertService
{
    Task SendOperationalAlertAsync(string title, string description, CancellationToken cancellationToken = default);
}
