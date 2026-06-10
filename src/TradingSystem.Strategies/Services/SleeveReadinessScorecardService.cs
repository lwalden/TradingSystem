using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Strategies.Services;

/// <summary>
/// Weekly sleeve readiness scorecard vs the PDR-004 paper-validation gate (S4-002).
///
/// Recommendation-only and strictly read-only: reads <see cref="SleeveValidationThresholds"/>
/// from the <see cref="IConfigRepository"/> settings seam (falling back to
/// <see cref="SleeveValidationThresholds.Defaults"/> when unset), derives per-sleeve actuals
/// from snapshot history and closed trades, and returns <see cref="SleeveReadinessScorecard"/>
/// objects. It never writes config, never changes mode/risk/sleeve weights, and never places
/// orders. Confidence is a deterministic function of sample size — no AI calls.
///
/// Sleeve trade attribution uses <see cref="Trade.Sleeve"/> (the authoritative dimension)
/// over trades from <see cref="ITradeRepository.GetByDateRangeAsync"/>, with hit rate and
/// profit factor computed via the same <see cref="TradeStatistics"/> definitions the
/// repository's <c>GetStatisticsAsync</c> uses. GetStatisticsAsync itself filters only by
/// exact strategy id, and options strategy ids are open-ended ("options-csp",
/// "options-iron-condor", "-close" suffixes, ...), so strategy-id slicing cannot reliably
/// reconstruct a sleeve — Trade.Sleeve can.
/// </summary>
public class SleeveReadinessScorecardService : ISleeveReadinessScorecardService
{
    // Observation window queried for snapshots/trades. The effective weeks-observed gate is
    // MinWeeksObserved (default 12); a year of lookback comfortably covers it.
    internal const int LookbackDays = 365;

    // Closed-trade count at which the trade-count half of Confidence saturates at 1.0.
    internal const int FullConfidenceTradeCount = 30;

    // Profit factor reported for a no-loss window with at least one winning trade. The raw
    // gross-profit/gross-loss ratio is undefined (division by zero); TradeStatistics reports 0
    // in that case, which would absurdly fail a flawless sleeve, so the scorecard substitutes
    // a sentinel that passes any sane threshold while staying serializable/comparable. The
    // structural condition is surfaced via SleeveReadinessScorecard.IsProfitFactorUndefined so
    // renderers show "∞ / no losses" rather than the raw sentinel.
    internal const decimal NoLossProfitFactor = 999m;

    private readonly ISnapshotRepository _snapshotRepository;
    private readonly ITradeRepository _tradeRepository;
    private readonly IConfigRepository _configRepository;

    public SleeveReadinessScorecardService(
        ISnapshotRepository snapshotRepository,
        ITradeRepository tradeRepository,
        IConfigRepository configRepository)
    {
        _snapshotRepository = snapshotRepository;
        _tradeRepository = tradeRepository;
        _configRepository = configRepository;
    }

    /// <summary>
    /// Produces one scorecard per sleeve (Income, Options/Tactical) for the window ending at
    /// <paramref name="asOf"/>. Read-only; safe to call from reporting paths.
    /// </summary>
    public async Task<IReadOnlyList<SleeveReadinessScorecard>> GenerateAsync(
        DateTime asOf, CancellationToken cancellationToken = default)
    {
        var thresholds = await _configRepository.GetSettingAsync<SleeveValidationThresholds>(
            SleeveValidationThresholds.SettingsKey, cancellationToken)
            ?? SleeveValidationThresholds.Defaults();

        var windowStart = asOf.AddDays(-LookbackDays);
        var snapshots = (await _snapshotRepository.GetSnapshotsAsync(windowStart, asOf, cancellationToken))
            .OrderBy(s => s.Date)
            .ToList();
        var trades = await _tradeRepository.GetByDateRangeAsync(windowStart, asOf, cancellationToken);

        return new[]
        {
            BuildScorecard("Income", SleeveType.Income, thresholds.Income,
                thresholds.MinimumLiveCapitalPerSleeve, snapshots, s => s.IncomeSleeveValue, trades, asOf),
            BuildScorecard("Options", SleeveType.Tactical, thresholds.Options,
                thresholds.MinimumLiveCapitalPerSleeve, snapshots, s => s.TacticalSleeveValue, trades, asOf)
        };
    }

    private static SleeveReadinessScorecard BuildScorecard(
        string sleeveName,
        SleeveType sleeve,
        SleeveThresholds sleeveThresholds,
        decimal minimumLiveCapital,
        List<DailySnapshot> snapshots,
        Func<DailySnapshot, decimal> sleeveValue,
        List<Trade> trades,
        DateTime asOf)
    {
        // Only snapshots where the sleeve actually held value count as observation time:
        // a sleeve that has not been funded yet has not been observed.
        var sleeveSnapshots = snapshots.Where(s => sleeveValue(s) > 0m).ToList();
        var weeksObserved = sleeveSnapshots.Count >= 2
            ? (int)((sleeveSnapshots[^1].Date - sleeveSnapshots[0].Date).TotalDays / 7)
            : 0;

        var closedTrades = trades
            .Where(t => t.Sleeve == sleeve && t.ExitTime.HasValue)
            .ToList();
        var stats = BuildStatistics(closedTrades);

        // No losing trades with at least one winner: the gross-profit/gross-loss ratio is
        // structurally undefined. The metrics carry a gate-passing sentinel (the gate must not
        // fail a flawless sleeve), and the scorecard flags the condition for renderers.
        var profitFactorUndefined = stats.LosingTrades == 0 && stats.WinningTrades > 0;

        var metrics = new SleeveMetrics
        {
            HitRatePercent = stats.WinRate,
            ProfitFactor = profitFactorUndefined
                ? NoLossProfitFactor
                : stats.LosingTrades == 0 ? 0m : stats.ProfitFactor,
            MaxDrawdownPercent = MaxDrawdownPercent(sleeveSnapshots, sleeveValue),
            WeeksObserved = weeksObserved,
            TotalReturnPercent = ReturnPercent(sleeveSnapshots, sleeveValue),
            SpyReturnPercent = ReturnPercent(sleeveSnapshots, s => s.SPYClose)
        };

        var result = sleeveThresholds.Evaluate(metrics);

        // Zero closed trades means hit rate / profit factor have no sample at all — that is
        // not-yet-evaluable (InsufficientData), never a premature NotReady verdict.
        var readiness = closedTrades.Count == 0
            ? SleeveReadinessState.InsufficientData
            : result.Outcome switch
            {
                ValidationOutcome.Pass => SleeveReadinessState.Ready,
                ValidationOutcome.Fail => SleeveReadinessState.NotReady,
                _ => SleeveReadinessState.InsufficientData
            };

        var currentValue = sleeveSnapshots.Count > 0 ? sleeveValue(sleeveSnapshots[^1]) : 0m;
        var meetsMinimumCapital = currentValue >= minimumLiveCapital;

        return new SleeveReadinessScorecard
        {
            SleeveName = sleeveName,
            Sleeve = sleeve,
            AsOf = asOf,
            Evaluation = result,
            IsProfitFactorUndefined = profitFactorUndefined,
            Readiness = readiness,
            ClosedTradeCount = closedTrades.Count,
            CurrentSleeveValue = currentValue,
            MinimumLiveCapital = minimumLiveCapital,
            MeetsMinimumCapital = meetsMinimumCapital,
            Confidence = ComputeConfidence(weeksObserved, closedTrades.Count, sleeveThresholds.MinWeeksObserved),
            Rationale = BuildRationale(
                readiness, result, closedTrades.Count, currentValue, minimumLiveCapital, meetsMinimumCapital)
        };
    }

    /// <summary>
    /// Same definitions as JsonTradeRepository.GetStatisticsAsync, applied to the
    /// sleeve-attributed closed-trade subset: WinRate and ProfitFactor then come from the
    /// TradeStatistics computed properties themselves.
    /// </summary>
    private static TradeStatistics BuildStatistics(List<Trade> closedTrades)
    {
        if (closedTrades.Count == 0)
            return new TradeStatistics();

        var winners = closedTrades.Where(t => (t.RealizedPnL ?? 0m) > 0m).ToList();
        var losers = closedTrades.Where(t => (t.RealizedPnL ?? 0m) <= 0m).ToList();

        return new TradeStatistics
        {
            TotalTrades = closedTrades.Count,
            WinningTrades = winners.Count,
            LosingTrades = losers.Count,
            TotalPnL = closedTrades.Sum(t => t.RealizedPnL ?? 0m),
            AverageWin = winners.Count > 0 ? winners.Average(t => t.RealizedPnL ?? 0m) : 0m,
            AverageLoss = losers.Count > 0 ? losers.Average(t => t.RealizedPnL ?? 0m) : 0m
        };
    }

    /// <summary>Simple total return over the observed series, in percent (0 when unobservable).</summary>
    private static decimal ReturnPercent(List<DailySnapshot> series, Func<DailySnapshot, decimal> value)
    {
        if (series.Count < 2)
            return 0m;

        var first = value(series[0]);
        if (first == 0m)
            return 0m;

        return (value(series[^1]) - first) / first * 100m;
    }

    /// <summary>Peak-to-trough max drawdown of the sleeve value series, in percent.</summary>
    private static decimal MaxDrawdownPercent(List<DailySnapshot> series, Func<DailySnapshot, decimal> value)
    {
        decimal peak = 0m, maxDrawdown = 0m;
        foreach (var snapshot in series)
        {
            var v = value(snapshot);
            peak = Math.Max(peak, v);
            if (peak > 0m)
                maxDrawdown = Math.Max(maxDrawdown, (peak - v) / peak * 100m);
        }
        return maxDrawdown;
    }

    /// <summary>
    /// Deterministic sample-size confidence in [0,1]: half from observation time (saturates at
    /// the configured MinWeeksObserved) and half from closed-trade count (saturates at
    /// <see cref="FullConfidenceTradeCount"/>). Monotonically non-decreasing in both inputs.
    /// </summary>
    internal static decimal ComputeConfidence(int weeksObserved, int closedTradeCount, int minWeeksObserved)
    {
        var weeksDenominator = Math.Max(1, minWeeksObserved);
        var weeksComponent = Math.Min(1m, weeksObserved / (decimal)weeksDenominator);
        var tradesComponent = Math.Min(1m, closedTradeCount / (decimal)FullConfidenceTradeCount);
        return Math.Round(0.5m * weeksComponent + 0.5m * tradesComponent, 4);
    }

    private static string BuildRationale(
        SleeveReadinessState readiness,
        ThresholdResult result,
        int closedTradeCount,
        decimal currentValue,
        decimal minimumLiveCapital,
        bool meetsMinimumCapital)
    {
        var actual = result.ActualMetrics;
        var applied = result.AppliedThresholds;
        var parts = new List<string>();

        switch (readiness)
        {
            case SleeveReadinessState.InsufficientData:
                // Self-contained: each arm names both the observation progress (weeks vs the
                // MinWeeksObserved target) and the closed-trade sample, so the rationale is
                // readable without the rest of the scorecard.
                parts.Add(closedTradeCount == 0
                    ? $"Insufficient data: no closed trades in the window ({actual.WeeksObserved} of " +
                      $"{applied.MinWeeksObserved} minimum week(s) observed)."
                    : $"Insufficient data: {actual.WeeksObserved} week(s) observed, below the " +
                      $"{applied.MinWeeksObserved}-week minimum ({closedTradeCount} closed trade(s)).");
                break;

            case SleeveReadinessState.NotReady:
                var failures = new List<string>();
                if (!result.HitRatePass)
                    failures.Add($"hit rate {actual.HitRatePercent:0.#}% below minimum {applied.MinHitRatePercent:0.#}%");
                if (!result.ProfitFactorPass)
                    failures.Add($"profit factor {actual.ProfitFactor:0.##} below minimum {applied.MinProfitFactor:0.##}");
                if (!result.DrawdownPass)
                    failures.Add($"max drawdown {actual.MaxDrawdownPercent:0.#}% above maximum {applied.MaxDrawdownPercent:0.#}%");
                if (!result.ProfitableOrBeatSpyPass)
                    failures.Add($"not net profitable ({actual.TotalReturnPercent:0.#}%) and did not beat SPY ({actual.SpyReturnPercent:0.#}%)");
                parts.Add($"Not ready: {string.Join("; ", failures)}.");
                break;

            default:
                parts.Add($"Ready: all metric gates passed over {actual.WeeksObserved} week(s) and {closedTradeCount} closed trade(s).");
                // Say WHICH profitability sub-condition fired (ADR-010 "profitable OR beat SPY").
                if (result.ProfitableOrBeatSpyPass)
                {
                    parts.Add(result.IsNetProfitable
                        ? $"Profitability gate passed via net profit ({actual.TotalReturnPercent:0.#}%)."
                        : result.BeatsSpy
                            ? $"Profitability gate passed via beat-SPY: sleeve {actual.TotalReturnPercent:0.#}% vs SPY {actual.SpyReturnPercent:0.#}% (not net profitable)."
                            : "Profitability gate not required by configuration.");
                }
                break;
        }

        if (!meetsMinimumCapital)
        {
            // Invariant formatting: the rationale is a stable, machine-loggable string and must
            // not change shape with the host culture.
            var current = currentValue.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            var minimum = minimumLiveCapital.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            parts.Add(
                $"Capital-gated: current sleeve value ${current} is below the ${minimum} " +
                "minimum live capital — no activation recommended regardless of metrics.");
        }

        return string.Join(" ", parts);
    }
}
