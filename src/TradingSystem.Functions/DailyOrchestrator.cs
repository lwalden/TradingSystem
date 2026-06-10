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

    private List<string> GetOptionSymbols()
    {
        return _config.Tactical.OptionUniverse
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
