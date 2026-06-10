using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Strategies.Services;
using Xunit;

namespace TradingSystem.Tests.Strategies;

public class SleeveReadinessScorecardServiceTests
{
    // Fixed evaluation date so snapshot/trade windows are deterministic.
    private static readonly DateTime AsOf = new(2026, 6, 8);

    // --- helpers -----------------------------------------------------------

    /// <summary>
    /// Builds <paramref name="count"/> weekly snapshots ending at AsOf. Sleeve values are
    /// linearly interpolated from start to end so the series is monotonic (zero drawdown
    /// when rising). A (count)-point weekly series spans (count-1) weeks.
    /// </summary>
    private static List<DailySnapshot> WeeklySnapshots(
        int count,
        decimal incomeStart, decimal incomeEnd,
        decimal tacticalStart = 0m, decimal tacticalEnd = 0m,
        decimal spyStart = 500m, decimal spyEnd = 510m)
    {
        var snapshots = new List<DailySnapshot>();
        for (var i = 0; i < count; i++)
        {
            var t = count > 1 ? (decimal)i / (count - 1) : 0m;
            snapshots.Add(new DailySnapshot
            {
                Date = AsOf.AddDays(-7 * (count - 1 - i)),
                IncomeSleeveValue = incomeStart + (incomeEnd - incomeStart) * t,
                TacticalSleeveValue = tacticalStart + (tacticalEnd - tacticalStart) * t,
                SPYClose = spyStart + (spyEnd - spyStart) * t
            });
        }
        return snapshots;
    }

    /// <summary>Closed trades for one sleeve: <paramref name="winners"/> at +$100 each and
    /// <paramref name="losers"/> at -$50 each, all inside the observation window.</summary>
    private static List<Trade> ClosedTrades(SleeveType sleeve, int winners, int losers)
    {
        var trades = new List<Trade>();
        for (var i = 0; i < winners + losers; i++)
        {
            trades.Add(new Trade
            {
                Sleeve = sleeve,
                StrategyId = sleeve == SleeveType.Income ? "income-monthly-reinvest" : "options-csp",
                EntryTime = AsOf.AddDays(-30 - i),
                ExitTime = AsOf.AddDays(-25 - i),
                RealizedPnL = i < winners ? 100m : -50m
            });
        }
        return trades;
    }

    private static SleeveReadinessScorecardService BuildService(
        List<DailySnapshot> snapshots, List<Trade> trades, FakeConfigRepository? configRepo = null)
    {
        return new SleeveReadinessScorecardService(
            new FakeSnapshotRepository(snapshots),
            new FakeTradeRepository(trades),
            configRepo ?? new FakeConfigRepository());
    }

    private static SleeveReadinessScorecard Income(IReadOnlyList<SleeveReadinessScorecard> cards) =>
        cards.Single(c => c.Sleeve == SleeveType.Income);

    private static SleeveReadinessScorecard Options(IReadOnlyList<SleeveReadinessScorecard> cards) =>
        cards.Single(c => c.Sleeve == SleeveType.Tactical);

    // --- Test 1: all metrics above threshold and >= min weeks => Ready ---

    [Fact]
    public async Task AllMetricsPassing_YieldsReady_WithAllFlagsTrue()
    {
        // 13 weekly points = 12-week span (exactly MinWeeksObserved); +5% income return
        // vs +2% SPY; 7W/3L trades => 70% hit rate, profit factor 700/150 ≈ 4.67.
        var snapshots = WeeklySnapshots(13, incomeStart: 100_000m, incomeEnd: 105_000m);
        var trades = ClosedTrades(SleeveType.Income, winners: 7, losers: 3);
        var service = BuildService(snapshots, trades);

        var card = Income(await service.GenerateAsync(AsOf));

        Assert.Equal(SleeveReadinessState.Ready, card.Readiness);
        Assert.True(card.Thresholds.HitRatePass);
        Assert.True(card.Thresholds.ProfitFactorPass);
        Assert.True(card.Thresholds.DrawdownPass);
        Assert.True(card.Thresholds.ProfitableOrBeatSpyPass);
        Assert.True(card.Thresholds.WeeksObservedMet);
        Assert.True(card.MeetsMinimumCapital); // 105k >= 100k default
    }

    // --- Test 2: failing hit rate => NotReady, rationale names metric + values ---

    [Fact]
    public async Task HitRateBelowThreshold_YieldsNotReady_RationaleNamesMetricWithValues()
    {
        var snapshots = WeeklySnapshots(13, incomeStart: 100_000m, incomeEnd: 105_000m);
        // 4W/6L => 40% hit rate (below 45% default); PF = 400/300 ≈ 1.33 still passes.
        var trades = ClosedTrades(SleeveType.Income, winners: 4, losers: 6);
        var service = BuildService(snapshots, trades);

        var card = Income(await service.GenerateAsync(AsOf));

        Assert.Equal(SleeveReadinessState.NotReady, card.Readiness);
        Assert.False(card.Thresholds.HitRatePass);
        Assert.Contains("hit rate", card.Rationale, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("40", card.Rationale); // actual
        Assert.Contains("45", card.Rationale); // threshold
    }

    // --- Test 3: fewer than MinWeeksObserved weeks => InsufficientData ---

    [Fact]
    public async Task FewerThanMinWeeks_YieldsInsufficientData_NotNotReady()
    {
        // 7 weekly points = 6-week span, well short of the 12-week minimum, and
        // deliberately bad metrics: too-early data must never read as NotReady.
        var snapshots = WeeklySnapshots(7, incomeStart: 100_000m, incomeEnd: 95_000m);
        var trades = ClosedTrades(SleeveType.Income, winners: 1, losers: 9);
        var service = BuildService(snapshots, trades);

        var card = Income(await service.GenerateAsync(AsOf));

        Assert.Equal(SleeveReadinessState.InsufficientData, card.Readiness);
        Assert.NotEqual(SleeveReadinessState.NotReady, card.Readiness);
    }

    // --- Test 4: metrics-Ready but under minimum capital => capital-gated, no writes ---

    [Fact]
    public async Task MetricsReadyButUnderMinimumCapital_FlagsCapitalGate_WritesNothing()
    {
        // Same passing metrics as Test 1, but sleeve value 80k -> 84k stays below the
        // $100k owner-confirmed minimum live capital.
        var snapshots = WeeklySnapshots(13, incomeStart: 80_000m, incomeEnd: 84_000m);
        var trades = ClosedTrades(SleeveType.Income, winners: 7, losers: 3);
        var configRepo = new FakeConfigRepository();
        var service = BuildService(snapshots, trades, configRepo);

        var card = Income(await service.GenerateAsync(AsOf));

        Assert.Equal(SleeveReadinessState.Ready, card.Readiness); // metrics are Ready
        Assert.False(card.MeetsMinimumCapital);
        Assert.Contains("capital", card.Rationale, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("100,000", card.Rationale); // names the minimum it is gated on

        // Recommendation-only invariant: the service must never write config/mode/risk.
        Assert.Equal(0, configRepo.WriteCount);
    }

    [Fact]
    public async Task DefaultThresholds_CarryOwnerConfirmedMinimumLiveCapital()
    {
        Assert.Equal(100_000m, SleeveValidationThresholds.Defaults().MinimumLiveCapitalPerSleeve);
        await Task.CompletedTask;
    }

    // --- Test 5: unprofitable sleeve that beats SPY passes the profitability gate ---

    [Fact]
    public async Task UnprofitableButBeatsSpy_PassesProfitabilityGate_RationaleSaysBeatSpy()
    {
        // Income -2% vs SPY -5%: net loss, but outperforms SPY (strict >). The
        // profitability gate passes via the beat-SPY sub-condition, and the rationale
        // must say WHICH sub-condition fired.
        var snapshots = WeeklySnapshots(13,
            incomeStart: 100_000m, incomeEnd: 98_000m,
            spyStart: 500m, spyEnd: 475m);
        var trades = ClosedTrades(SleeveType.Income, winners: 7, losers: 3);
        var service = BuildService(snapshots, trades);

        var card = Income(await service.GenerateAsync(AsOf));

        Assert.True(card.Thresholds.ProfitableOrBeatSpyPass);
        Assert.False(card.Thresholds.IsNetProfitable);
        Assert.True(card.Thresholds.BeatsSpy);
        Assert.Equal(SleeveReadinessState.Ready, card.Readiness);
        Assert.Contains("SPY", card.Rationale);
    }

    // --- Test 6: confidence increases monotonically with sample size ---

    [Fact]
    public async Task Confidence_IncreasesMonotonically_WithWeeksObserved()
    {
        var trades = ClosedTrades(SleeveType.Income, winners: 7, losers: 3);
        var confidences = new List<decimal>();
        foreach (var points in new[] { 5, 9, 13 }) // 4, 8, 12-week spans
        {
            var service = BuildService(
                WeeklySnapshots(points, incomeStart: 100_000m, incomeEnd: 105_000m), trades);
            confidences.Add(Income(await service.GenerateAsync(AsOf)).Confidence);
        }

        Assert.True(confidences[0] < confidences[1],
            $"confidence must rise with weeks: {confidences[0]} !< {confidences[1]}");
        Assert.True(confidences[1] < confidences[2],
            $"confidence must rise with weeks: {confidences[1]} !< {confidences[2]}");
    }

    [Fact]
    public async Task Confidence_IncreasesMonotonically_WithTradeCount()
    {
        var snapshots = WeeklySnapshots(13, incomeStart: 100_000m, incomeEnd: 105_000m);
        var confidences = new List<decimal>();
        foreach (var trades in new[] { 5, 15, 25 }) // below the saturation count
        {
            var winners = (int)Math.Ceiling(trades * 0.6);
            var service = BuildService(
                snapshots, ClosedTrades(SleeveType.Income, winners, trades - winners));
            confidences.Add(Income(await service.GenerateAsync(AsOf)).Confidence);
        }

        Assert.True(confidences[0] < confidences[1],
            $"confidence must rise with trades: {confidences[0]} !< {confidences[1]}");
        Assert.True(confidences[1] < confidences[2],
            $"confidence must rise with trades: {confidences[1]} !< {confidences[2]}");
    }

    // --- Test 7: both sleeves in one call; empty sleeve => InsufficientData, no crash ---

    [Fact]
    public async Task BothSleevesProduced_EmptySleeveYieldsInsufficientData_NoDivideByZero()
    {
        // Income sleeve is active; tactical sleeve has zero value and zero trades.
        var snapshots = WeeklySnapshots(13, incomeStart: 100_000m, incomeEnd: 105_000m);
        var trades = ClosedTrades(SleeveType.Income, winners: 7, losers: 3);
        var service = BuildService(snapshots, trades);

        var cards = await service.GenerateAsync(AsOf);

        Assert.Equal(2, cards.Count);
        Assert.Equal(SleeveReadinessState.Ready, Income(cards).Readiness);

        var options = Options(cards);
        Assert.Equal(SleeveReadinessState.InsufficientData, options.Readiness);
        Assert.False(options.MeetsMinimumCapital);
    }

    [Fact]
    public async Task SufficientWeeksButZeroClosedTrades_YieldsInsufficientData()
    {
        // A sleeve with snapshot history but no closed trades has no hit-rate/profit-factor
        // sample at all — that is InsufficientData, never a premature NotReady.
        var snapshots = WeeklySnapshots(13, incomeStart: 100_000m, incomeEnd: 105_000m);
        var service = BuildService(snapshots, new List<Trade>());

        var card = Income(await service.GenerateAsync(AsOf));

        Assert.Equal(SleeveReadinessState.InsufficientData, card.Readiness);
    }

    // --- thresholds come from the config seam, falling back to Defaults() ---

    [Fact]
    public async Task ConfiguredThresholds_AreUsedInsteadOfDefaults()
    {
        // Raise the income hit-rate floor to 75% via the settings seam: the same 70%
        // sleeve that is Ready under defaults must become NotReady.
        var configRepo = new FakeConfigRepository();
        var custom = SleeveValidationThresholds.Defaults();
        custom.Income.MinHitRatePercent = 75m;
        configRepo.Seed(SleeveValidationThresholds.SettingsKey, custom);
        configRepo.ResetWriteCount();

        var snapshots = WeeklySnapshots(13, incomeStart: 100_000m, incomeEnd: 105_000m);
        var trades = ClosedTrades(SleeveType.Income, winners: 7, losers: 3);
        var service = BuildService(snapshots, trades, configRepo);

        var card = Income(await service.GenerateAsync(AsOf));

        Assert.Equal(SleeveReadinessState.NotReady, card.Readiness);
        Assert.False(card.Thresholds.HitRatePass);
        Assert.Equal(0, configRepo.WriteCount); // read-only even when configured
    }

    // --- fakes -------------------------------------------------------------

    private sealed class FakeSnapshotRepository : ISnapshotRepository
    {
        private readonly List<DailySnapshot> _snapshots;

        public FakeSnapshotRepository(List<DailySnapshot> snapshots) => _snapshots = snapshots;

        public Task SaveDailySnapshotAsync(DailySnapshot snapshot, CancellationToken ct = default) =>
            throw new NotSupportedException("Scorecard service must be read-only.");

        public Task<DailySnapshot?> GetSnapshotAsync(DateTime date, CancellationToken ct = default) =>
            Task.FromResult(_snapshots.FirstOrDefault(s => s.Date.Date == date.Date));

        public Task<List<DailySnapshot>> GetSnapshotsAsync(DateTime startDate, DateTime endDate,
            CancellationToken ct = default) =>
            Task.FromResult(_snapshots.Where(s => s.Date >= startDate && s.Date <= endDate).ToList());
    }

    private sealed class FakeTradeRepository : ITradeRepository
    {
        private readonly List<Trade> _trades;

        public FakeTradeRepository(List<Trade> trades) => _trades = trades;

        public Task<Trade> SaveAsync(Trade trade, CancellationToken ct = default) =>
            throw new NotSupportedException("Scorecard service must be read-only.");

        public Task<Trade?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(_trades.FirstOrDefault(t => t.Id == id));

        public Task<List<Trade>> GetByDateRangeAsync(DateTime startDate, DateTime endDate,
            CancellationToken ct = default) =>
            Task.FromResult(_trades.Where(t => t.EntryTime >= startDate && t.EntryTime <= endDate).ToList());

        public Task<List<Trade>> GetByStrategyAsync(string strategyId, DateTime? since = null,
            CancellationToken ct = default) =>
            Task.FromResult(_trades.Where(t => t.StrategyId == strategyId).ToList());

        public Task<List<Trade>> GetOpenTradesAsync(CancellationToken ct = default) =>
            Task.FromResult(_trades.Where(t => !t.ExitTime.HasValue).ToList());

        public Task<TradeStatistics> GetStatisticsAsync(DateTime? since = null, string? strategyId = null,
            CancellationToken ct = default) =>
            Task.FromResult(new TradeStatistics());
    }

    private sealed class FakeConfigRepository : IConfigRepository
    {
        private readonly Dictionary<string, object> _settings = new();

        public int WriteCount { get; private set; }

        public void Seed(string key, object value) => _settings[key] = value;

        public void ResetWriteCount() => WriteCount = 0;

        public Task<TradingSystemConfig> GetConfigAsync(CancellationToken ct = default) =>
            Task.FromResult(new TradingSystemConfig());

        public Task SaveConfigAsync(TradingSystemConfig config, CancellationToken ct = default)
        {
            WriteCount++;
            return Task.CompletedTask;
        }

        public Task<T?> GetSettingAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(_settings.TryGetValue(key, out var value) ? (T?)value : default);

        public Task SetSettingAsync<T>(string key, T value, CancellationToken ct = default)
        {
            WriteCount++;
            _settings[key] = value!;
            return Task.CompletedTask;
        }
    }
}
