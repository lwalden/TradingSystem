using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Strategies.Options;

namespace TradingSystem.Functions;

/// <summary>
/// Main daily orchestrator - runs pre-market to evaluate strategies and execute trades
/// </summary>
public class DailyOrchestrator
{
    private readonly ILogger<DailyOrchestrator> _logger;
    private readonly TradingSystemConfig _config;
    private readonly IServiceProvider _serviceProvider;

    // S5-003 alert-spam guard (Default D6): once per run per failure category, keyed
    // runId:category. Each timer fires once/day with a fresh runId, so this stays tiny over a
    // long-lived instance; the lock is belt-and-braces (the two timers never overlap).
    private readonly object _alertGateLock = new();
    private readonly HashSet<string> _alertedRunCategories = new(StringComparer.Ordinal);

    public DailyOrchestrator(
        ILogger<DailyOrchestrator> logger,
        IOptions<TradingSystemConfig> config,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _config = config.Value;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Pre-market run - 6:00 AM PT (13:00 UTC) on trading days
    /// </summary>
    [Function("DailyOrchestrator_PreMarket")]
    public async Task RunPreMarket(
        [TimerTrigger("0 0 13 * * 1-5")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("Starting pre-market orchestration. RunId: {RunId}, Mode: {Mode}", 
            runId, _config.Mode);

        try
        {
            await RunOptionsSleeveAsync(runId, cancellationToken);

            _logger.LogInformation("Pre-market orchestration complete. RunId: {RunId}", runId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pre-market orchestration failed. RunId: {RunId}", runId);

            // S5-003: operational orchestration-failure alert BEFORE the rethrow (App Insights
            // failure signal preserved). Exception TYPE NAME only — messages can echo
            // URIs/secrets. CancellationToken.None: a cancelled run must still be able to alert.
            await TrySendOperationalAlertAsync(
                runId,
                "orchestration-failure",
                "Orchestration Run Failure — Pre-Market",
                $"Unhandled {ex.GetType().Name} during the pre-market run. RunId: {runId}. See Application Insights for details.",
                CancellationToken.None);

            throw;
        }
    }

    /// <summary>
    /// End-of-day run - 1:30 PM PT (20:30 UTC) on trading days
    /// </summary>
    [Function("DailyOrchestrator_EndOfDay")]
    public async Task RunEndOfDay(
        [TimerTrigger("0 30 20 * * 1-5")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("Starting end-of-day processing. RunId: {RunId}", runId);

        try
        {
            // Thin timer wrapper (S5-001 / Default D2): the EOD pipeline lives in
            // IEndOfDayService. Same null-tolerant resolve style as pre-market.
            var endOfDayService = _serviceProvider.GetService<IEndOfDayService>();
            if (endOfDayService == null)
            {
                _logger.LogWarning(
                    "IEndOfDayService not registered. Skipping end-of-day processing. RunId: {RunId}",
                    runId);
                return;
            }

            var result = await endOfDayService.RunAsync(runId, cancellationToken);

            if (result.Warnings.Count > 0)
            {
                _logger.LogWarning(
                    "End-of-day warnings. RunId: {RunId}. {Warnings}",
                    runId,
                    string.Join(" | ", result.Warnings));
            }

            _logger.LogInformation(
                "End-of-day processing complete. RunId: {RunId}, BrokerConnected: {BrokerConnected}, SnapshotPersisted: {SnapshotPersisted}, SnapshotEnriched: {SnapshotEnriched}, StopTriggered: {StopTriggered}",
                runId,
                result.BrokerConnected,
                result.SnapshotPersisted,
                result.SnapshotEnriched,
                result.StopTriggered);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "End-of-day processing failed. RunId: {RunId}", runId);

            // S5-003: operational orchestration-failure alert BEFORE the rethrow (App Insights
            // failure signal preserved). Exception TYPE NAME only — messages can echo
            // URIs/secrets. CancellationToken.None: a cancelled run must still be able to alert.
            await TrySendOperationalAlertAsync(
                runId,
                "orchestration-failure",
                "Orchestration Run Failure — End of Day",
                $"Unhandled {ex.GetType().Name} during the end-of-day run. RunId: {runId}. See Application Insights for details.",
                CancellationToken.None);

            throw;
        }
    }

    private async Task RunOptionsSleeveAsync(string runId, CancellationToken cancellationToken)
    {
        var broker = _serviceProvider.GetService<IBrokerService>();
        if (broker == null)
        {
            _logger.LogWarning("IBrokerService not registered. Skipping options sleeve. RunId: {RunId}", runId);
            return;
        }

        var connected = await broker.ConnectAsync(cancellationToken);
        if (!connected)
        {
            _logger.LogWarning("Could not connect to broker. Skipping options sleeve. RunId: {RunId}", runId);

            // S5-003: operational connect-failure alert (best-effort, once per run — Default D6).
            // The degrade itself is unchanged: options sleeve skipped, no throw.
            await TrySendOperationalAlertAsync(
                runId,
                "connect-failure",
                "Broker Connect Failure — Pre-Market",
                $"Could not connect to the broker for the pre-market run. Options sleeve skipped. RunId: {runId}.",
                cancellationToken);

            return;
        }

        try
        {
            var optionsManager = _serviceProvider.GetRequiredService<OptionsSleeveManager>();
            var symbols = GetOptionSymbols();
            if (symbols.Count == 0)
            {
                _logger.LogInformation("No options symbols configured. RunId: {RunId}", runId);
                return;
            }

            var result = await optionsManager.RunDailyAsync(symbols, cancellationToken);
            _logger.LogInformation(
                "Options sleeve run complete. RunId: {RunId}, Symbols: {SymbolCount}, Candidates: {Candidates}, LifecycleActions: {LifecycleActions}, NewEntries: {NewEntries}, Success: {Success}, Failures: {Failures}, Halted: {Halted}",
                runId,
                symbols.Count,
                result.CandidatesScanned,
                result.LifecycleActionsTriggered,
                result.NewEntriesOpened,
                result.SuccessfulExecutions,
                result.FailedExecutions,
                result.TradingHalted);

            if (result.Warnings.Count > 0)
            {
                _logger.LogWarning(
                    "Options sleeve warnings. RunId: {RunId}. {Warnings}",
                    runId,
                    string.Join(" | ", result.Warnings));
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "Options sleeve dependencies are not fully wired. Skipping options sleeve run. RunId: {RunId}",
                runId);
        }
        finally
        {
            await broker.DisconnectAsync();
        }
    }

    /// <summary>
    /// S5-003 best-effort operational alert: null-tolerant resolve (same style as the other
    /// optional services), never throws (alert failure must never fail or replace the run's
    /// outcome), gated to once per run per failure category (Default D6). Exception TYPE NAMES
    /// only ever reach logs here — never messages, which can echo URIs/secrets.
    /// </summary>
    private async Task TrySendOperationalAlertAsync(
        string runId,
        string category,
        string title,
        string description,
        CancellationToken cancellationToken)
    {
        var operationalAlerts = _serviceProvider.GetService<IOperationalAlertService>();
        if (operationalAlerts == null)
            return;

        bool claimed;
        lock (_alertGateLock)
        {
            claimed = _alertedRunCategories.Add($"{runId}:{category}");
        }

        if (!claimed)
        {
            _logger.LogDebug(
                "Operational alert already sent for this run/category; skipping. RunId: {RunId}, Category: {Category}",
                runId,
                category);
            return;
        }

        try
        {
            await operationalAlerts.SendOperationalAlertAsync(title, description, cancellationToken);
        }
        catch (Exception ex)
        {
            // Observability must never become control: swallow and log (type name only).
            _logger.LogWarning(
                "Operational alert send failed ({ErrorType}); orchestration outcome unaffected. RunId: {RunId}, Category: {Category}",
                ex.GetType().Name,
                runId,
                category);
        }
    }

    private List<string> GetOptionSymbols()
    {
        return _config.Tactical.OptionUniverse
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
