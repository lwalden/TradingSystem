using Microsoft.Extensions.Logging;
using TradingSystem.Core.Interfaces;

namespace TradingSystem.Functions;

/// <summary>
/// S5-001 end-of-day pipeline. Connect → sync/stop-check/base-snapshot (delegated to the
/// existing <see cref="IRiskManager.GetRiskMetricsAsync"/> — locked decision 4: alert-only,
/// no halt behavior, no risk-parameter changes) → best-effort enrichment second upsert
/// (activity from trades, market context from one cached regime call) → disconnect.
///
/// Failure contract (Defaults D3/D4):
/// - Broker connect failure: NO snapshot written, no throw — returns a structured failure
///   result (S5-003 alerts on it).
/// - Enrichment failure: warning only; the RiskManager-persisted base snapshot is kept.
/// - Unexpected exceptions propagate (the timer wrapper logs + rethrows for App Insights).
/// </summary>
public class EndOfDayService : IEndOfDayService
{
    private readonly IBrokerService _broker;
    private readonly IRiskManager _riskManager;
    private readonly ILogger<EndOfDayService> _logger;
    private readonly ISnapshotRepository? _snapshotRepository;
    private readonly ITradeRepository? _tradeRepository;
    private readonly IMarketDataService? _marketDataService;

    public EndOfDayService(
        IBrokerService broker,
        IRiskManager riskManager,
        ILogger<EndOfDayService> logger,
        ISnapshotRepository? snapshotRepository = null,
        ITradeRepository? tradeRepository = null,
        IMarketDataService? marketDataService = null)
    {
        _broker = broker;
        _riskManager = riskManager;
        _logger = logger;
        _snapshotRepository = snapshotRepository;
        _tradeRepository = tradeRepository;
        _marketDataService = marketDataService;
    }

    public async Task<EndOfDayResult> RunAsync(string runId, CancellationToken cancellationToken = default)
    {
        var result = new EndOfDayResult();

        var connected = await _broker.ConnectAsync(cancellationToken);
        if (!connected)
        {
            // Default D3: never persist stale data — no snapshot write, no throw.
            _logger.LogWarning(
                "Could not connect to broker at end-of-day. No snapshot will be written. RunId: {RunId}",
                runId);
            return result;
        }

        result.BrokerConnected = true;
        try
        {
            // Sync + stop check + base snapshot — existing RiskManager semantics as-is
            // (broker account/position sync, transition-gated stop alerts, base upsert).
            var metrics = await _riskManager.GetRiskMetricsAsync(cancellationToken);
            result.StopTriggered =
                metrics.DailyStopTriggered || metrics.WeeklyStopTriggered || metrics.DrawdownHaltTriggered;

            await EnrichSnapshotAsync(runId, result, cancellationToken);

            _logger.LogInformation(
                "End-of-day pipeline finished. RunId: {RunId}, SnapshotPersisted: {SnapshotPersisted}, StopTriggered: {StopTriggered}, Warnings: {WarningCount}",
                runId,
                result.SnapshotPersisted,
                result.StopTriggered,
                result.Warnings.Count);

            return result;
        }
        finally
        {
            await _broker.DisconnectAsync();
        }
    }

    /// <summary>
    /// Best-effort enrichment (Default D4): populate the fields RiskManager leaves empty
    /// (TradesExecuted, CommissionsPaid, RealizedPnL, SPYClose, VIXClose, MarketRegime) and
    /// upsert. Any failure logs a warning and keeps the base snapshot — never throws.
    /// </summary>
    private async Task EnrichSnapshotAsync(string runId, EndOfDayResult result, CancellationToken cancellationToken)
    {
        if (_snapshotRepository == null)
        {
            _logger.LogWarning(
                "ISnapshotRepository not registered; end-of-day snapshot not persisted. RunId: {RunId}",
                runId);
            result.Warnings.Add("Snapshot repository not registered; snapshot not persisted.");
            return;
        }

        var today = DateTime.UtcNow.Date;
        try
        {
            var snapshot = await _snapshotRepository.GetSnapshotAsync(today, cancellationToken);
            if (snapshot == null)
            {
                _logger.LogWarning(
                    "Base snapshot for {Date} not found after risk sync; enrichment skipped. RunId: {RunId}",
                    today,
                    runId);
                result.Warnings.Add($"Base snapshot for {today:yyyy-MM-dd} not found; enrichment skipped.");
                return;
            }

            // The base snapshot is persisted regardless of how enrichment fares below.
            result.SnapshotPersisted = true;

            if (_tradeRepository != null)
            {
                var todaysTrades = await _tradeRepository.GetByDateRangeAsync(today, today, cancellationToken);
                snapshot.TradesExecuted = todaysTrades.Count;
                snapshot.CommissionsPaid = todaysTrades.Sum(t => t.Commission ?? 0m);
                snapshot.RealizedPnL = todaysTrades
                    .Where(t => t.ExitTime?.Date == today)
                    .Sum(t => t.RealizedPnL ?? 0m);
            }

            if (_marketDataService != null)
            {
                // One cached call covers all three context fields (gateway-or-rules per
                // ADR-029/030 — zero metered spend with the direct-API fallback off).
                var regime = await _marketDataService.GetMarketRegimeAsync(cancellationToken);
                snapshot.SPYClose = regime.SPYPrice;
                snapshot.VIXClose = regime.VIX;
                snapshot.MarketRegime = regime.Regime;
            }

            await _snapshotRepository.SaveDailySnapshotAsync(snapshot, cancellationToken);
        }
        catch (Exception ex)
        {
            // Default D4: enrichment is best-effort — keep the base snapshot, never rethrow.
            _logger.LogWarning(
                ex,
                "End-of-day snapshot enrichment failed; base snapshot retained. RunId: {RunId}",
                runId);
            result.Warnings.Add($"Snapshot enrichment failed: {ex.GetType().Name}");
        }
    }
}
