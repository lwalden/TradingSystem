using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Core.Services;

/// <summary>
/// Core risk manager enforcing per-trade risk, position caps, and stop-halt checks.
/// </summary>
public class RiskManager : IRiskManager
{
    private readonly IBrokerService _broker;
    private readonly ICalendarService _calendar;
    private readonly TradingSystemConfig _config;
    private readonly ILogger<RiskManager> _logger;
    private readonly ISnapshotRepository? _snapshotRepository;
    private readonly IRiskAlertService _riskAlertService;
    private static readonly IRiskAlertService NoOpAlertService = new NoOpRiskAlertService();

    public RiskManager(
        IBrokerService broker,
        ICalendarService calendar,
        IOptions<TradingSystemConfig> config,
        ILogger<RiskManager> logger,
        ISnapshotRepository? snapshotRepository = null,
        IRiskAlertService? riskAlertService = null)
    {
        _broker = broker;
        _calendar = calendar;
        _config = config.Value;
        _logger = logger;
        _snapshotRepository = snapshotRepository;
        _riskAlertService = riskAlertService ?? NoOpAlertService;
    }

    public async Task<RiskValidationResult> ValidateSignalAsync(
        Signal signal,
        Account account,
        CancellationToken cancellationToken = default)
    {
        var result = new RiskValidationResult { IsValid = true };

        if (await IsTradingHaltedAsync(cancellationToken))
        {
            Fail(result, "trading-halted", "Trading is halted by daily/weekly stop.");
            return result;
        }

        if (!IsSignalExecutable(signal))
        {
            Fail(result, "invalid-signal", $"Signal {signal.Id} is not executable.");
            return result;
        }

        var size = signal.SuggestedPositionSize ?? 0m;
        if (size <= 0)
        {
            Fail(result, "invalid-size", "SuggestedPositionSize must be greater than zero.");
            return result;
        }

        var riskAmount = CalculateRiskAmount(signal);
        if (riskAmount <= 0m)
        {
            Fail(result, "invalid-risk", $"Unable to determine risk amount for signal {signal.Id}.");
            return result;
        }

        var riskBudget = account.NetLiquidationValue * _config.Risk.RiskPerTradePercent;
        if (riskAmount > riskBudget)
        {
            Fail(
                result,
                "per-trade-risk",
                $"Risk {riskAmount:C} exceeds max per-trade budget {riskBudget:C}.");
            return result;
        }
        result.PassedChecks.Add("per-trade-risk");

        var sleeve = InferSleeve(signal);
        var proposedValue = CalculateProposedValue(signal);
        var limits = await CheckPositionLimitsAsync(
            signal.Symbol,
            proposedValue,
            sleeve,
            account,
            cancellationToken);
        if (!limits.WithinLimits)
        {
            Fail(
                result,
                "position-limits",
                $"{limits.ViolationType}: proposed exposure {limits.ProposedExposure:C} > max {limits.MaxAllowed:C}.");
            return result;
        }
        result.PassedChecks.Add("position-limits");

        if (sleeve == SleeveType.Income)
        {
            var caps = await CheckIncomeCapsAsync(
                signal.Symbol,
                proposedValue,
                account,
                cancellationToken);
            if (!caps.WithinCaps)
            {
                var reason = caps.IssuerCapViolation ?? caps.CategoryCapViolation ?? "Income cap violation.";
                Fail(result, "income-caps", reason);
                return result;
            }
            result.PassedChecks.Add("income-caps");
        }

        if (IsEntrySignal(signal))
        {
            var inNoTradeWindow = await _calendar.IsInNoTradeWindowAsync(
                signal.Symbol,
                DateTime.UtcNow.Date,
                cancellationToken);
            if (inNoTradeWindow)
            {
                Fail(result, "no-trade-window", $"{signal.Symbol} is currently in a no-trade window.");
                return result;
            }
            result.PassedChecks.Add("no-trade-window");
        }

        return result;
    }

    public PositionSizeResult CalculatePositionSize(
        string symbol,
        decimal entryPrice,
        decimal stopPrice,
        decimal accountEquity,
        decimal riskPercent)
    {
        var riskAmount = Math.Max(0m, accountEquity * riskPercent);
        var riskPerUnit = Math.Abs(entryPrice - stopPrice);

        if (riskAmount <= 0m || riskPerUnit <= 0m)
        {
            return new PositionSizeResult
            {
                Shares = 0,
                RiskAmount = riskAmount,
                PositionValue = 0m,
                RiskPercent = riskPercent,
                Rationale = $"Cannot size {symbol}: non-positive risk budget or stop distance."
            };
        }

        var shares = (int)Math.Floor(riskAmount / riskPerUnit);
        if (shares < 0)
            shares = 0;

        return new PositionSizeResult
        {
            Shares = shares,
            RiskAmount = shares * riskPerUnit,
            PositionValue = shares * entryPrice,
            RiskPercent = riskPercent,
            Rationale = $"Sized {symbol} using risk budget {riskAmount:C} and unit risk {riskPerUnit:C}."
        };
    }

    public async Task<bool> IsTradingHaltedAsync(CancellationToken cancellationToken = default)
    {
        var metrics = await GetRiskMetricsAsync(cancellationToken);
        return metrics.DailyStopTriggered || metrics.WeeklyStopTriggered || metrics.DrawdownHaltTriggered;
    }

    public Task<PositionLimitResult> CheckPositionLimitsAsync(
        string symbol,
        decimal proposedValue,
        SleeveType sleeve,
        Account account,
        CancellationToken cancellationToken = default)
    {
        var nav = account.NetLiquidationValue;
        var currentExposure = account.Positions
            .Where(p => IsMatchingSymbol(p, symbol))
            .Sum(p => Math.Abs(p.MarketValue));

        var maxPercent = sleeve == SleeveType.Tactical
            ? _config.Risk.MaxSingleSpreadPercent
            : _config.Risk.MaxSingleEquityPercent;

        var maxAllowed = nav * maxPercent;
        var proposedExposure = currentExposure + Math.Abs(proposedValue);

        var result = new PositionLimitResult
        {
            WithinLimits = proposedExposure <= maxAllowed,
            CurrentExposure = currentExposure,
            ProposedExposure = proposedExposure,
            MaxAllowed = maxAllowed,
            ViolationType = proposedExposure <= maxAllowed
                ? null
                : (sleeve == SleeveType.Tactical ? "single-spread-cap" : "single-equity-cap")
        };

        return Task.FromResult(result);
    }

    public Task<CapCheckResult> CheckIncomeCapsAsync(
        string symbol,
        decimal proposedValue,
        Account account,
        CancellationToken cancellationToken = default)
    {
        var incomePositions = account.Positions
            .Where(p => p.Sleeve == SleeveType.Income)
            .ToList();
        var nav = account.NetLiquidationValue;
        if (nav <= 0m)
        {
            return Task.FromResult(new CapCheckResult
            {
                WithinCaps = false,
                IssuerCapViolation = "Net liquidation value must be positive."
            });
        }

        var issuerCurrent = incomePositions
            .Where(p => p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .Sum(p => Math.Abs(p.MarketValue));
        var issuerExposure = (issuerCurrent + Math.Abs(proposedValue)) / nav;

        var category = incomePositions
            .FirstOrDefault(p => p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            ?.Category;

        decimal? categoryExposure = null;
        string? categoryViolation = null;
        if (!string.IsNullOrWhiteSpace(category))
        {
            var categoryCurrent = incomePositions
                .Where(p => p.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                .Sum(p => Math.Abs(p.MarketValue));
            categoryExposure = (categoryCurrent + Math.Abs(proposedValue)) / nav;

            if (categoryExposure > _config.Income.MaxCategoryPercent)
            {
                categoryViolation =
                    $"Category cap exceeded for {category}: {categoryExposure:P2} > {_config.Income.MaxCategoryPercent:P2}.";
            }
        }

        var issuerViolation = issuerExposure > _config.Income.MaxIssuerPercent
            ? $"Issuer cap exceeded for {symbol}: {issuerExposure:P2} > {_config.Income.MaxIssuerPercent:P2}."
            : null;

        return Task.FromResult(new CapCheckResult
        {
            WithinCaps = issuerViolation == null && categoryViolation == null,
            IssuerExposure = issuerExposure,
            CategoryExposure = categoryExposure,
            IssuerCapViolation = issuerViolation,
            CategoryCapViolation = categoryViolation
        });
    }

    public async Task<RiskMetrics> GetRiskMetricsAsync(CancellationToken cancellationToken = default)
    {
        var account = await _broker.GetAccountAsync(cancellationToken);
        var positions = account.Positions.Count > 0
            ? account.Positions
            : await _broker.GetPositionsAsync(cancellationToken);

        var unrealizedPnL = positions.Sum(p => p.UnrealizedPnL);
        var grossExposure = account.GrossPositionValue > 0m
            ? account.GrossPositionValue
            : positions.Sum(p => Math.Abs(p.MarketValue));
        var netExposure = positions.Sum(p => p.MarketValue);
        var today = DateTime.UtcNow.Date;
        var snapshots = await LoadSnapshotsAsync(today, cancellationToken);
        var todaySnapshot = snapshots.LastOrDefault(s => s.Date.Date == today);
        var priorSnapshots = snapshots
            .Where(s => s.Date.Date < today)
            .OrderBy(s => s.Date.Date)
            .ToList();

        var dailyBaseline = priorSnapshots.LastOrDefault()?.NetLiquidationValue;
        var dailyPnL = dailyBaseline.HasValue
            ? account.NetLiquidationValue - dailyBaseline.Value
            : unrealizedPnL;
        var dailyPnLPercent = CalculatePercent(
            dailyPnL,
            dailyBaseline ?? account.NetLiquidationValue);

        var weeklyBaseline = GetWeeklyBaseline(today, priorSnapshots) ?? dailyBaseline ?? account.NetLiquidationValue;
        var weeklyPnL = account.NetLiquidationValue - weeklyBaseline;
        var weeklyPnLPercent = CalculatePercent(weeklyPnL, weeklyBaseline);

        var historicalHighWater = priorSnapshots
            .Select(s => Math.Max(s.HighWaterMark, s.NetLiquidationValue))
            .DefaultIfEmpty(account.NetLiquidationValue)
            .Max();
        var highWaterMark = Math.Max(historicalHighWater, account.NetLiquidationValue);
        var currentDrawdown = CalculateDrawdown(account.NetLiquidationValue, highWaterMark);
        var historicalMaxDrawdown = priorSnapshots
            .Select(s => s.MaxDrawdown)
            .DefaultIfEmpty(0m)
            .Max();
        var maxDrawdown = Math.Max(historicalMaxDrawdown, currentDrawdown);

        var metrics = new RiskMetrics
        {
            DailyPnL = dailyPnL,
            DailyPnLPercent = dailyPnLPercent,
            WeeklyPnL = weeklyPnL,
            WeeklyPnLPercent = weeklyPnLPercent,
            HighWaterMark = highWaterMark,
            MaxDrawdown = maxDrawdown,
            CurrentDrawdown = currentDrawdown,
            DailyStopTriggered = dailyPnLPercent <= -_config.Risk.DailyStopPercent,
            WeeklyStopTriggered = weeklyPnLPercent <= -_config.Risk.WeeklyStopPercent,
            DrawdownHaltTriggered = currentDrawdown >= _config.Risk.MaxDrawdownHalt,
            OpenPositionCount = positions.Count,
            GrossExposure = grossExposure,
            NetExposure = netExposure,
            LastUpdated = DateTime.UtcNow
        };

        await SendStopAlertsIfNeededAsync(metrics, todaySnapshot, cancellationToken);
        await PersistSnapshotAsync(account, positions, metrics, cancellationToken);

        if (metrics.DailyStopTriggered || metrics.WeeklyStopTriggered || metrics.DrawdownHaltTriggered)
        {
            _logger.LogWarning(
                "Risk stop triggered: daily={DailyTriggered} ({DailyPnLPercent:P2}), weekly={WeeklyTriggered} ({WeeklyPnLPercent:P2}), drawdown={DrawdownTriggered} ({CurrentDrawdown:P2})",
                metrics.DailyStopTriggered,
                metrics.DailyPnLPercent,
                metrics.WeeklyStopTriggered,
                metrics.WeeklyPnLPercent,
                metrics.DrawdownHaltTriggered,
                metrics.CurrentDrawdown);
        }

        return metrics;
    }

    private async Task<List<DailySnapshot>> LoadSnapshotsAsync(
        DateTime today,
        CancellationToken cancellationToken)
    {
        if (_snapshotRepository == null)
            return new List<DailySnapshot>();

        var startDate = today.AddDays(-30);
        var endDate = today;
        return await _snapshotRepository.GetSnapshotsAsync(startDate, endDate, cancellationToken);
    }

    private static decimal? GetWeeklyBaseline(DateTime today, IReadOnlyCollection<DailySnapshot> snapshots)
    {
        var weekStart = StartOfWeek(today);
        var weekSnapshot = snapshots
            .Where(s => s.Date.Date >= weekStart)
            .OrderBy(s => s.Date.Date)
            .FirstOrDefault();

        return weekSnapshot?.NetLiquidationValue;
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var delta = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-delta).Date;
    }

    private static decimal CalculatePercent(decimal numerator, decimal denominator)
    {
        if (denominator == 0m)
            return 0m;

        return numerator / denominator;
    }

    private static decimal CalculateDrawdown(decimal currentValue, decimal highWaterMark)
    {
        if (highWaterMark <= 0m)
            return 0m;

        var drawdown = (highWaterMark - currentValue) / highWaterMark;
        return Math.Max(0m, drawdown);
    }

    private async Task SendStopAlertsIfNeededAsync(
        RiskMetrics metrics,
        DailySnapshot? todaySnapshot,
        CancellationToken cancellationToken)
    {
        if (_snapshotRepository == null)
            return;

        var wasDailyTriggered = todaySnapshot?.DailyStopTriggered == true;
        var wasWeeklyTriggered = todaySnapshot?.WeeklyStopTriggered == true;
        var wasDrawdownTriggered = todaySnapshot?.DrawdownHaltTriggered == true;

        if (metrics.DailyStopTriggered && !wasDailyTriggered)
        {
            await _riskAlertService.SendDailyStopTriggeredAsync(metrics, cancellationToken);
        }

        if (metrics.WeeklyStopTriggered && !wasWeeklyTriggered)
        {
            await _riskAlertService.SendWeeklyStopTriggeredAsync(metrics, cancellationToken);
        }

        if (metrics.DrawdownHaltTriggered && !wasDrawdownTriggered)
        {
            await _riskAlertService.SendDrawdownHaltTriggeredAsync(metrics, cancellationToken);
        }
    }

    private async Task PersistSnapshotAsync(
        Account account,
        IReadOnlyCollection<Position> positions,
        RiskMetrics metrics,
        CancellationToken cancellationToken)
    {
        if (_snapshotRepository == null)
            return;

        var incomeSleeveValue = account.IncomeSleeveValue > 0m
            ? account.IncomeSleeveValue
            : positions
                .Where(p => p.Sleeve == SleeveType.Income)
                .Sum(p => Math.Abs(p.MarketValue));
        var tacticalSleeveValue = account.TacticalSleeveValue > 0m
            ? account.TacticalSleeveValue
            : positions
                .Where(p => p.Sleeve == SleeveType.Tactical)
                .Sum(p => Math.Abs(p.MarketValue));
        var cashValue = account.TotalCashValue > 0m
            ? account.TotalCashValue
            : account.AvailableFunds;

        var snapshot = new DailySnapshot
        {
            Date = metrics.LastUpdated.Date,
            NetLiquidationValue = account.NetLiquidationValue,
            CashValue = cashValue,
            IncomeSleeveValue = incomeSleeveValue,
            TacticalSleeveValue = tacticalSleeveValue,
            DailyPnL = metrics.DailyPnL,
            DailyPnLPercent = metrics.DailyPnLPercent,
            RealizedPnL = 0m,
            UnrealizedPnL = positions.Sum(p => p.UnrealizedPnL),
            MaxDrawdown = metrics.MaxDrawdown,
            HighWaterMark = metrics.HighWaterMark,
            DailyStopTriggered = metrics.DailyStopTriggered,
            WeeklyStopTriggered = metrics.WeeklyStopTriggered,
            DrawdownHaltTriggered = metrics.DrawdownHaltTriggered,
            OpenPositions = metrics.OpenPositionCount,
            GrossExposure = metrics.GrossExposure
        };

        await _snapshotRepository.SaveDailySnapshotAsync(snapshot, cancellationToken);
    }

    private sealed class NoOpRiskAlertService : IRiskAlertService
    {
        public Task SendDailyStopTriggeredAsync(RiskMetrics metrics, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SendWeeklyStopTriggeredAsync(RiskMetrics metrics, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SendDrawdownHaltTriggeredAsync(RiskMetrics metrics, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static bool IsSignalExecutable(Signal signal)
    {
        if (signal.Status != SignalStatus.Active)
            return false;
        if (signal.ExpiresAt <= DateTime.UtcNow)
            return false;
        return signal.Direction != SignalDirection.Hold;
    }

    private static bool IsEntrySignal(Signal signal)
    {
        return signal.Direction is SignalDirection.Long or SignalDirection.Short;
    }

    private static SleeveType InferSleeve(Signal signal)
    {
        var strategyId = signal.StrategyId.ToLowerInvariant();
        if (strategyId.Contains("income", StringComparison.Ordinal))
            return SleeveType.Income;

        if (signal.SecurityType.Equals("OPT", StringComparison.OrdinalIgnoreCase) ||
            signal.SecurityType.Equals("BAG", StringComparison.OrdinalIgnoreCase))
        {
            return SleeveType.Tactical;
        }

        return strategyId.Contains("options", StringComparison.Ordinal)
            ? SleeveType.Tactical
            : SleeveType.Income;
    }

    private static bool IsMatchingSymbol(Position position, string symbol)
    {
        if (position.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            return true;

        return position.UnderlyingSymbol?.Equals(symbol, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static decimal CalculateRiskAmount(Signal signal)
    {
        if (signal.SuggestedRiskAmount.HasValue && signal.SuggestedRiskAmount.Value > 0m)
            return signal.SuggestedRiskAmount.Value;

        var size = signal.SuggestedPositionSize ?? 0m;
        if (size <= 0m)
            return 0m;

        if (signal.MaxLoss.HasValue && signal.MaxLoss.Value > 0m)
            return signal.MaxLoss.Value * size;

        if (signal.SuggestedEntryPrice.HasValue &&
            signal.SuggestedStopPrice.HasValue &&
            signal.SuggestedEntryPrice.Value > 0m &&
            signal.SuggestedStopPrice.Value > 0m)
        {
            return Math.Abs(signal.SuggestedEntryPrice.Value - signal.SuggestedStopPrice.Value) * size;
        }

        return 0m;
    }

    private static decimal CalculateProposedValue(Signal signal)
    {
        if (signal.SuggestedRiskAmount.HasValue && signal.SuggestedRiskAmount.Value > 0m)
            return signal.SuggestedRiskAmount.Value;

        var size = signal.SuggestedPositionSize ?? 0m;
        if (size <= 0m)
            return 0m;

        if (signal.MaxLoss.HasValue && signal.MaxLoss.Value > 0m)
            return signal.MaxLoss.Value * size;

        if (!signal.SuggestedEntryPrice.HasValue || signal.SuggestedEntryPrice.Value <= 0m)
            return 0m;

        var multiplier = signal.SecurityType.Equals("OPT", StringComparison.OrdinalIgnoreCase) ||
                         signal.SecurityType.Equals("BAG", StringComparison.OrdinalIgnoreCase)
            ? 100m
            : 1m;

        return signal.SuggestedEntryPrice.Value * size * multiplier;
    }

    private static void Fail(RiskValidationResult result, string failedCheck, string reason)
    {
        result.IsValid = false;
        result.FailedChecks.Add(failedCheck);
        result.RejectionReason = reason;
    }
}
