using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.AI.Services;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Functions;
using TradingSystem.Strategies.Services;

namespace TradingSystem.SmokeTest;

/// <summary>
/// S4-007: inert readiness-path section for the manual SANDBOX smoke harness, wiring
/// S4-001 (SleeveValidationThresholds via an in-memory config seam) -> S4-002 (the real
/// SleeveReadinessScorecardService over seeded deterministic paper metrics) -> S4-003
/// (the real DiscordDailyReportService with the webhook HTTP stubbed in-memory).
///
/// Everything external is stubbed: no TWS, no Cosmos, no live Discord POST, and an
/// inert-AI ClaudeService (no gateway key, metered fallback at its default OFF). The
/// CI-enforced, assertion-bearing twin of this section lives in
/// tests/TradingSystem.Tests/Functions/SandboxReadinessSmokeTests.cs — this section
/// exists so manual SANDBOX runs exercise the same wiring and print the scorecards.
/// </summary>
internal static class ReadinessSmokeSection
{
    private static readonly DateTime AsOf = DateTime.Today;

    /// <summary>Runs the three readiness smoke checks, numbered from <paramref name="firstIndex"/>.</summary>
    public static async Task<(int Passed, int Failed)> RunAsync(ILoggerFactory loggerFactory, int firstIndex, int totalTests)
    {
        var passed = 0;
        var failed = 0;

        var configRepo = new InertConfigRepository();
        configRepo.Seed(SleeveValidationThresholds.SettingsKey, SleeveValidationThresholds.Defaults());
        var snapshotRepo = new InertSnapshotRepository(SeedSnapshots());
        var tradeRepo = new InertTradeRepository(SeedTrades());

        // Single scorecard service shared by both checks below (mirrors the xUnit fixture).
        var scorecardService = new SleeveReadinessScorecardService(snapshotRepo, tradeRepo, configRepo);

        // [first] Sleeve readiness scorecards (S4-001 thresholds -> S4-002 scorecards).
        Console.WriteLine($"[{firstIndex}/{totalTests}] Sleeve readiness scorecards (stubbed repos, deterministic)...");
        IReadOnlyList<SleeveReadinessScorecard>? scorecards = null;
        try
        {
            scorecards = await scorecardService.GenerateAsync(AsOf);
            if (scorecards.Count != 2)
                throw new InvalidOperationException($"expected 2 sleeve scorecards, got {scorecards.Count}");
            foreach (var card in scorecards)
            {
                Console.WriteLine($"  OK: {card.SleeveName}: {card.Readiness} (confidence {card.Confidence:P0}, {card.ClosedTradeCount} closed trades)");
                Console.WriteLine($"      {card.Rationale}");
            }
            if (configRepo.WriteCount != 0)
                throw new InvalidOperationException("recommendation-only violated: scorecard wrote config");
            Console.WriteLine("  OK: zero config writes (recommendation-only)");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL [{ex.GetType().Name}]: {ex.Message}" +
                (ex.InnerException is { } inner ? $" (inner: {inner.Message})" : string.Empty));
            failed++;
        }

        Console.WriteLine();

        // [first+1] Daily report embeds via a stubbed webhook handler — no live Discord POST.
        Console.WriteLine($"[{firstIndex + 1}/{totalTests}] Daily report embed build (Discord webhook stubbed, no live POST)...");
        try
        {
            var handler = new RecordingDiscordHandler();
            var reportService = new DiscordDailyReportService(
                new RecordingHttpClientFactory(handler),
                Options.Create(new DiscordConfig
                {
                    Enabled = true,
                    WebhookUrl = "https://discord.com/api/webhooks/0/inert-smoke-token",
                    Username = "TradingSystem Smoke"
                }),
                snapshotRepo,
                tradeRepo,
                loggerFactory.CreateLogger<DiscordDailyReportService>(),
                scorecardService);

            await reportService.SendDailyReportAsync(AsOf);
            if (handler.InvocationCount != 1)
                throw new InvalidOperationException($"expected exactly 1 stubbed POST, got {handler.InvocationCount}");
            var body = handler.Bodies.Single();
            if (!body.Contains("Daily Report") || !body.Contains("Readiness"))
                throw new InvalidOperationException(
                    $"report payload missing summary or readiness embed; body[..200]: {body[..Math.Min(200, body.Length)]}");
            Console.WriteLine($"  OK: payload captured by stub handler ({body.Length} chars), summary + readiness embeds present");
            Console.WriteLine("  OK: no live Discord POST (HTTP terminated at in-memory stub)");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL [{ex.GetType().Name}]: {ex.Message}" +
                (ex.InnerException is { } inner ? $" (inner: {inner.Message})" : string.Empty));
            failed++;
        }

        Console.WriteLine();

        // [first+2] Inert-AI posture: no gateway key + metered fallback OFF -> zero AI HTTP.
        Console.WriteLine($"[{firstIndex + 2}/{totalTests}] Inert AI (no gateway key, metered fallback off)...");
        try
        {
            var metered = new MeteredCountingHandler();
            var claude = new ClaudeService(
                loggerFactory.CreateLogger<ClaudeService>(),
                Options.Create(new ClaudeConfig
                {
                    ApiKey = "smoke-key-never-used",
                    GatewayApiKey = string.Empty,
                    Model = "claude-sonnet-4-20250514",
                    MaxDirectApiCallsPerDay = 50
                    // DirectApiFallbackEnabled left at its real default (false).
                }),
                new HttpClient(metered),
                new ThrowingGatewayFactory());

            var raw = await claude.AnalyzeAsync(new AIAnalysisRequest
            {
                StrategyId = "readiness-smoke",
                SystemPrompt = "sys",
                UserPrompt = "user"
            });
            if (!string.IsNullOrEmpty(raw))
                throw new InvalidOperationException("inert AI unexpectedly produced content");
            Console.WriteLine("  OK: non-generic AnalyzeAsync yielded no content (caller falls to rules)");

            // Generic path throws into the caller's try/catch -> deterministic rules
            // (mirrors the xUnit twin's Assert.ThrowsAsync<InvalidOperationException>).
            var genericThrew = false;
            try
            {
                await claude.AnalyzeAsync<RegimeStub>(new AIAnalysisRequest
                {
                    StrategyId = "readiness-smoke",
                    SystemPrompt = "sys",
                    UserPrompt = "user"
                });
            }
            catch (InvalidOperationException)
            {
                genericThrew = true;
            }
            if (!genericThrew)
                throw new InvalidOperationException(
                    "generic AnalyzeAsync<T> unexpectedly succeeded on inert AI (expected InvalidOperationException)");
            Console.WriteLine("  OK: generic AnalyzeAsync<T> threw InvalidOperationException — caller falls back to deterministic rules");

            if (metered.InvocationCount != 0)
                throw new InvalidOperationException($"metered API was hit {metered.InvocationCount} time(s)");
            Console.WriteLine("  OK: gateway never invoked, zero metered calls — regime stays deterministic rules");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL [{ex.GetType().Name}]: {ex.Message}" +
                (ex.InnerException is { } inner ? $" (inner: {inner.Message})" : string.Empty));
            failed++;
        }

        return (passed, failed);
    }

    // 13 weekly points = 12-week span (exactly the default MinWeeksObserved). Both sleeves
    // profitable and beating SPY; final snapshot carries the report-day fields.
    private static List<DailySnapshot> SeedSnapshots()
    {
        const int count = 13;
        var snapshots = new List<DailySnapshot>();
        for (var i = 0; i < count; i++)
        {
            var t = (decimal)i / (count - 1);
            snapshots.Add(new DailySnapshot
            {
                Date = AsOf.AddDays(-7 * (count - 1 - i)),
                IncomeSleeveValue = 100_000m + 5_000m * t,
                TacticalSleeveValue = 100_000m + 4_000m * t,
                SPYClose = 500m + 10m * t
            });
        }

        var today = snapshots[^1];
        today.NetLiquidationValue = 250_000m;
        today.DailyPnL = 1_234.56m;
        today.DailyPnLPercent = 0.0049m;
        today.RealizedPnL = 800.25m;
        today.UnrealizedPnL = 434.31m;
        today.TradesExecuted = 1;
        today.CommissionsPaid = 3.30m;
        today.OpenPositions = 5;
        today.MarketRegime = RegimeType.Cautious;
        return snapshots;
    }

    // 7W/3L closed trades per sleeve (70% hit rate, profit factor ≈ 4.67) inside the window.
    private static List<Trade> SeedTrades()
    {
        var trades = new List<Trade>();
        foreach (var sleeve in new[] { SleeveType.Income, SleeveType.Tactical })
        {
            for (var i = 0; i < 10; i++)
            {
                trades.Add(new Trade
                {
                    Sleeve = sleeve,
                    StrategyId = sleeve == SleeveType.Income ? "income-monthly-reinvest" : "options-csp",
                    Symbol = sleeve == SleeveType.Income ? "VIG" : "SPY",
                    EntryTime = AsOf.AddDays(-30 - i),
                    ExitTime = AsOf.AddDays(-25 - i),
                    RealizedPnL = i < 7 ? 100m : -50m
                });
            }
        }
        return trades;
    }

    // ---- inert stubs (mirror the CI fixture in SandboxReadinessSmokeTests) ----

    private sealed class RecordingDiscordHandler : HttpMessageHandler
    {
        public int InvocationCount { get; private set; }
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            if (request.Content != null)
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public RecordingHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }

    private sealed class MeteredCountingHandler : HttpMessageHandler
    {
        public int InvocationCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Count first (preserves the zero-invocation assertions), then refuse the call:
            // the metered direct Anthropic API must never be reachable from test/smoke code.
            InvocationCount++;
            throw new InvalidOperationException(
                "metered Anthropic API must not be called in test/smoke context");
        }
    }

    // Deserialization target for the generic AnalyzeAsync<T> inert check (mirrors the
    // xUnit twin's RegimeStub — the console project has no reference to that fixture).
    private sealed class RegimeStub
    {
        public string? Regime { get; set; }
    }

    private sealed class ThrowingGatewayFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new ThrowingHandler()) { BaseAddress = new Uri("http://localhost:3131/") };

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("gateway must not be called when GatewayApiKey is empty");
        }
    }

    private sealed class InertConfigRepository : IConfigRepository
    {
        private readonly Dictionary<string, object> _settings = new();

        public int WriteCount { get; private set; }

        public void Seed(string key, object value) => _settings[key] = value;

        public Task<TradingSystemConfig> GetConfigAsync(CancellationToken ct = default) =>
            Task.FromResult(new TradingSystemConfig { Mode = TradingMode.Sandbox });

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

    private sealed class InertSnapshotRepository : ISnapshotRepository
    {
        private readonly List<DailySnapshot> _snapshots;

        public InertSnapshotRepository(List<DailySnapshot> snapshots) => _snapshots = snapshots;

        public Task SaveDailySnapshotAsync(DailySnapshot snapshot, CancellationToken ct = default) =>
            throw new NotSupportedException("Readiness smoke path must be read-only.");

        public Task<DailySnapshot?> GetSnapshotAsync(DateTime date, CancellationToken ct = default) =>
            Task.FromResult(_snapshots.FirstOrDefault(s => s.Date.Date == date.Date));

        public Task<List<DailySnapshot>> GetSnapshotsAsync(DateTime startDate, DateTime endDate,
            CancellationToken ct = default) =>
            Task.FromResult(_snapshots.Where(s => s.Date >= startDate && s.Date <= endDate).ToList());
    }

    private sealed class InertTradeRepository : ITradeRepository
    {
        private readonly List<Trade> _trades;

        public InertTradeRepository(List<Trade> trades) => _trades = trades;

        public Task<Trade> SaveAsync(Trade trade, CancellationToken ct = default) =>
            throw new NotSupportedException("Readiness smoke path must be read-only.");

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
}
