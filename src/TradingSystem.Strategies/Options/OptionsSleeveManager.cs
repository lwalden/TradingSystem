using Microsoft.Extensions.Logging;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Core.Services;

namespace TradingSystem.Strategies.Options;

/// <summary>
/// Orchestrates daily options sleeve workflow: reconcile, lifecycle actions, screening, and entry execution.
/// </summary>
public class OptionsSleeveManager
{
    private readonly IBrokerService _broker;
    private readonly IRiskManager _riskManager;
    private readonly IOptionsPositionRepository _optionsPositionRepository;
    private readonly OptionsScreeningService _screeningService;
    private readonly OptionsLifecycleRules _lifecycleRules;
    private readonly OptionsPositionGrouper _positionGrouper;
    private readonly OptionsCandidateConverter _candidateConverter;
    private readonly OptionsPositionSizer _positionSizer;
    private readonly OptionsExecutionService _optionsExecutionService;
    private readonly OptionsConfig _optionsConfig;
    private readonly ILogger<OptionsSleeveManager> _logger;

    public OptionsSleeveManager(
        IBrokerService broker,
        IRiskManager riskManager,
        IOptionsPositionRepository optionsPositionRepository,
        OptionsScreeningService screeningService,
        OptionsLifecycleRules lifecycleRules,
        OptionsPositionGrouper positionGrouper,
        OptionsCandidateConverter candidateConverter,
        OptionsPositionSizer positionSizer,
        OptionsExecutionService optionsExecutionService,
        TacticalConfig tacticalConfig,
        ILogger<OptionsSleeveManager> logger)
    {
        _broker = broker;
        _riskManager = riskManager;
        _optionsPositionRepository = optionsPositionRepository;
        _screeningService = screeningService;
        _lifecycleRules = lifecycleRules;
        _positionGrouper = positionGrouper;
        _candidateConverter = candidateConverter;
        _positionSizer = positionSizer;
        _optionsExecutionService = optionsExecutionService;
        _optionsConfig = tacticalConfig.Options;
        _logger = logger;
    }

    public async Task<OptionsSleeveState> GetSleeveStateAsync(CancellationToken cancellationToken = default)
    {
        var openPositions = await _optionsPositionRepository.GetOpenPositionsAsync(cancellationToken);
        return OptionsSleeveState.Build(openPositions, _optionsConfig.MaxOpenPositions);
    }

    public async Task<OptionsSleeveRunResult> RunDailyAsync(
        IEnumerable<string> symbols,
        CancellationToken cancellationToken = default)
    {
        var result = new OptionsSleeveRunResult();
        var symbolList = symbols
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (await _riskManager.IsTradingHaltedAsync(cancellationToken))
        {
            result.TradingHalted = true;
            result.Warnings.Add("Trading is halted by risk manager.");
            return result;
        }

        var account = await _broker.GetAccountAsync(cancellationToken);
        var trackedOpenPositions = await _optionsPositionRepository.GetOpenPositionsAsync(cancellationToken);
        var brokerPositions = await _broker.GetPositionsAsync(cancellationToken);

        var reconciliation = _positionGrouper.Reconcile(trackedOpenPositions, brokerPositions);
        result.UntrackedPositionGroups = reconciliation.UntrackedBrokerPositions.Count;
        result.Warnings.AddRange(reconciliation.Warnings);

        foreach (var tracked in reconciliation.ReconciledTrackedPositions)
        {
            await _optionsPositionRepository.UpdateAsync(tracked, cancellationToken);
        }

        await ExecuteLifecycleActionsAsync(reconciliation.ReconciledTrackedPositions, result, cancellationToken);

        var currentState = await GetSleeveStateAsync(cancellationToken);
        if (!currentState.HasCapacity)
        {
            result.Warnings.Add("No remaining options sleeve capacity for new entries.");
            return result;
        }

        if (symbolList.Count == 0)
        {
            result.Warnings.Add("No symbols supplied for screening.");
            return result;
        }

        var screeningResult = await _screeningService.ScanAsync(symbolList, cancellationToken);
        var candidates = FlattenCandidates(screeningResult);
        result.CandidatesScanned = screeningResult.TotalCandidates;

        var positionsByUnderlying = new Dictionary<string, int>(
            currentState.PositionsByUnderlying,
            StringComparer.OrdinalIgnoreCase);
        var openCount = currentState.OpenPositionCount;

        foreach (var candidate in candidates)
        {
            if (openCount >= _optionsConfig.MaxOpenPositions)
                break;

            var currentUnderlyingCount = positionsByUnderlying.GetValueOrDefault(candidate.UnderlyingSymbol, 0);
            if (currentUnderlyingCount >= _optionsConfig.MaxPositionsPerUnderlying)
                continue;

            var size = _positionSizer.CalculateContracts(candidate, account.NetLiquidationValue, account.AvailableFunds);
            if (size.Contracts <= 0)
                continue;

            var signal = _candidateConverter.ConvertToEntrySignal(candidate, size.Contracts);
            var validation = await _riskManager.ValidateSignalAsync(signal, account, cancellationToken);
            if (!validation.IsValid)
            {
                result.RiskRejectedEntries++;
                continue;
            }

            if (validation.AdjustedPositionSize.HasValue && validation.AdjustedPositionSize.Value > 0)
                signal.SuggestedPositionSize = validation.AdjustedPositionSize.Value;

            var execution = await _optionsExecutionService.ExecuteSignalAsync(signal, cancellationToken);
            result.Executions.Add(execution);

            if (!execution.Success)
            {
                result.FailedExecutions++;
                continue;
            }

            result.SuccessfulExecutions++;
            result.NewEntriesOpened++;
            openCount++;
            positionsByUnderlying[candidate.UnderlyingSymbol] = currentUnderlyingCount + 1;
        }

        return result;
    }

    private async Task ExecuteLifecycleActionsAsync(
        IEnumerable<OptionsPosition> positions,
        OptionsSleeveRunResult result,
        CancellationToken cancellationToken)
    {
        foreach (var position in positions.Where(p => p.Status == OptionsPositionStatus.Open))
        {
            var decision = _lifecycleRules.Evaluate(position);
            if (decision.Action == OptionsLifecycleAction.Hold)
                continue;

            result.LifecycleActionsTriggered++;
            var closeSignal = _candidateConverter.ConvertToCloseSignal(position, decision);
            var execution = await _optionsExecutionService.ExecuteSignalAsync(closeSignal, cancellationToken);
            result.Executions.Add(execution);

            if (!execution.Success)
            {
                result.FailedExecutions++;
                continue;
            }

            result.SuccessfulExecutions++;
            position.Status = decision.Action switch
            {
                OptionsLifecycleAction.TakeProfit => OptionsPositionStatus.ProfitTargetReached,
                OptionsLifecycleAction.MandatoryTakeProfit => OptionsPositionStatus.ProfitTargetReached,
                OptionsLifecycleAction.StopOut => OptionsPositionStatus.StopTriggered,
                OptionsLifecycleAction.Roll => OptionsPositionStatus.RollPending,
                OptionsLifecycleAction.CloseNearExpiration => OptionsPositionStatus.Closing,
                _ => position.Status
            };
            position.ExitReason = decision.Reason;
            position.LastUpdated = DateTime.UtcNow;
            await _optionsPositionRepository.UpdateAsync(position, cancellationToken);
        }
    }

    private static List<OptionCandidate> FlattenCandidates(OptionsScreenResult screeningResult)
    {
        return screeningResult.CSPCandidates
            .Concat(screeningResult.BullPutSpreadCandidates)
            .Concat(screeningResult.BearCallSpreadCandidates)
            .Concat(screeningResult.IronCondorCandidates)
            .Concat(screeningResult.CalendarSpreadCandidates)
            .OrderByDescending(c => c.Score)
            .ToList();
    }
}

public class OptionsSleeveRunResult
{
    public bool TradingHalted { get; set; }
    public int UntrackedPositionGroups { get; set; }
    public int CandidatesScanned { get; set; }
    public int LifecycleActionsTriggered { get; set; }
    public int RiskRejectedEntries { get; set; }
    public int NewEntriesOpened { get; set; }
    public int SuccessfulExecutions { get; set; }
    public int FailedExecutions { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<ExecutionResult> Executions { get; set; } = new();
}
