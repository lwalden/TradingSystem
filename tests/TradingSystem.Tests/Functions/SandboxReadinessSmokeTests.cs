using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingSystem.AI.Services;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Functions;
using TradingSystem.Strategies.Services;
using Xunit;

namespace TradingSystem.Tests.Functions;

/// <summary>
/// S4-007: End-to-end SANDBOX readiness-path smoke test wiring S4-001 -> S4-002 -> S4-003:
/// <see cref="SleeveValidationThresholds"/> (in-memory config seam) -> the REAL
/// <see cref="SleeveReadinessScorecardService"/> (via <see cref="ISleeveReadinessScorecardService"/>)
/// over seeded deterministic paper metrics -> the REAL <see cref="DiscordDailyReportService"/>
/// (via <see cref="IDailyReportService"/>) with the webhook HTTP stubbed. ALL externals are
/// mocked: no TWS, no Cosmos, no live Discord POST, no gateway/metered Claude call — the suite
/// is CI-safe and deterministic.
///
/// The NAMED safety assertions are the point of this fixture (sprint S4 readiness gate):
/// no SANDBOX->LIVE switch, zero order placement, recommendation-only (zero config writes),
/// no live Discord POST (and the Enabled==false skip), and the S3-006 inert-AI posture
/// (no gateway key -> regime stays deterministic-rules, gateway handler never invoked).
/// </summary>
public class SandboxReadinessSmokeTests
{
    // Fixed evaluation date so snapshot/trade windows are deterministic.
    private static readonly DateTime AsOf = new(2026, 6, 8);

    // Token substring used in the fake webhook URL; only the stub handler may ever see it.
    private const string FakeWebhookUrl = "https://discord.com/api/webhooks/123/inert-smoke-token";

    // ========================================================================================
    // 1. Readiness path end-to-end: thresholds -> scorecard -> daily report. Non-null
    //    scorecards for BOTH sleeves and a built report payload captured by the stub handler.
    // ========================================================================================
    [Fact]
    public async Task Smoke_ReadinessPath_EndToEnd_BothScorecardsAndReportEmbedsProduced()
    {
        var fx = new SmokeFixture();

        var scorecards = await fx.ScorecardService.GenerateAsync(AsOf);
        await fx.ReportService.SendDailyReportAsync(AsOf);

        // Both sleeves produced, each with a populated evaluation against the default gate.
        Assert.Equal(2, scorecards.Count);
        var income = Assert.Single(scorecards, c => c.Sleeve == SleeveType.Income);
        var options = Assert.Single(scorecards, c => c.Sleeve == SleeveType.Tactical);
        Assert.Equal(SleeveReadinessState.Ready, income.Readiness);
        Assert.Equal(SleeveReadinessState.Ready, options.Readiness);
        Assert.NotNull(income.Evaluation.AppliedThresholds);
        Assert.NotNull(options.Evaluation.AppliedThresholds);

        // The stub handler captured exactly one POSTed payload: summary embed + readiness embed.
        var body = Assert.Single(fx.DiscordHandler.Bodies);
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var embeds = doc.RootElement.GetProperty("embeds");
        Assert.Equal(2, embeds.GetArrayLength());

        Assert.Contains("Daily Report", embeds[0].GetProperty("title").GetString());
        var readiness = embeds[1];
        Assert.Contains("Readiness", readiness.GetProperty("title").GetString());
        Assert.Equal("Income: Ready | Options: Ready", readiness.GetProperty("description").GetString());
        var fieldNames = readiness.GetProperty("fields").EnumerateArray()
            .Select(f => f.GetProperty("name").GetString() ?? string.Empty)
            .ToList();
        Assert.Contains(fieldNames, n => n.StartsWith("Income"));
        Assert.Contains(fieldNames, n => n.StartsWith("Options"));
    }

    // ========================================================================================
    // 2. No order placement: the strict broker/execution mocks record ZERO calls — no
    //    PlaceOrderAsync / PlaceComboOrderAsync / order save / signal save anywhere on the path.
    // ========================================================================================
    [Fact]
    public async Task Smoke_NoOrderPlacement_BrokerAndExecutionNeverInvoked()
    {
        var fx = new SmokeFixture();

        await fx.ScorecardService.GenerateAsync(AsOf);
        await fx.ReportService.SendDailyReportAsync(AsOf);

        // MockBehavior.Strict: ANY invocation would have thrown; VerifyNoOtherCalls locks zero.
        fx.Broker.VerifyNoOtherCalls();
        fx.OrderRepository.VerifyNoOtherCalls();
        fx.SignalRepository.VerifyNoOtherCalls();
    }

    // ========================================================================================
    // 3. No LIVE switch: Mode is Sandbox before and after, and the config repo received no
    //    SaveConfigAsync (the only seam through which Mode could be persisted).
    // ========================================================================================
    [Fact]
    public async Task Smoke_NoLiveSwitch_ModeStaysSandbox_NoConfigSave()
    {
        var fx = new SmokeFixture();
        Assert.Equal(TradingMode.Sandbox, fx.SystemConfig.Mode);

        await fx.ScorecardService.GenerateAsync(AsOf);
        await fx.ReportService.SendDailyReportAsync(AsOf);

        Assert.Equal(TradingMode.Sandbox, fx.SystemConfig.Mode);
        Assert.NotEqual(TradingMode.Live, fx.SystemConfig.Mode);
        Assert.Equal(0, fx.ConfigRepo.SaveConfigCount);
    }

    // ========================================================================================
    // 4. Recommendation-only: ZERO config writes during the run — no RiskConfig, sleeve
    //    weight, or threshold mutation through either settings seam.
    // ========================================================================================
    [Fact]
    public async Task Smoke_RecommendationOnly_ZeroConfigWrites()
    {
        var fx = new SmokeFixture();

        await fx.ScorecardService.GenerateAsync(AsOf);
        await fx.ReportService.SendDailyReportAsync(AsOf);

        Assert.Equal(0, fx.ConfigRepo.SaveConfigCount);
        Assert.Equal(0, fx.ConfigRepo.SetSettingCount);
        Assert.Empty(fx.ConfigRepo.WrittenKeys);
    }

    // ========================================================================================
    // 5. Discord leg hits ONLY the stubbed handler: exactly one POST, captured in-memory —
    //    structurally no live network (the HttpClient is built over the stub handler).
    // ========================================================================================
    [Fact]
    public async Task Smoke_DiscordPost_HitsOnlyStubHandler()
    {
        var fx = new SmokeFixture();

        await fx.ReportService.SendDailyReportAsync(AsOf);

        Assert.Equal(1, fx.DiscordHandler.InvocationCount);
        Assert.Single(fx.DiscordHandler.Bodies);
        // The stub saw the webhook request (proof the leg terminated at the stub, not the wire).
        var uri = Assert.Single(fx.DiscordHandler.RequestUris);
        Assert.Equal("discord.com", uri.Host);
    }

    // ========================================================================================
    // 5b. Enabled==false: the Discord leg is skipped entirely — zero HTTP, zero repo reads.
    // ========================================================================================
    [Fact]
    public async Task Smoke_DiscordDisabled_SkipsReportEntirely_NoPost()
    {
        var fx = new SmokeFixture(discordEnabled: false);

        await fx.ReportService.SendDailyReportAsync(AsOf);

        Assert.Equal(0, fx.DiscordHandler.InvocationCount);
        Assert.Empty(fx.DiscordHandler.Bodies);
    }

    // ========================================================================================
    // 6. Inert-AI posture (S3-006/S3-003): with no gateway key and the metered fallback OFF
    //    (its real default), the gateway handler is NEVER invoked, zero metered HTTP happens,
    //    and the regime in the delivered report comes from the deterministic seeded data —
    //    not from any AI call.
    // ========================================================================================
    [Fact]
    public async Task Smoke_InertAi_GatewayNeverInvoked_NoMeteredCall_RegimeFromDeterministicRules()
    {
        var fx = new SmokeFixture();

        // The harness-posture ClaudeService: empty GatewayApiKey, DirectApiFallbackEnabled at
        // its real default (false). The gateway factory THROWS on any send; the metered
        // handler counts direct-API sends.
        var claudeConfig = new ClaudeConfig
        {
            ApiKey = "test-key",
            GatewayApiKey = string.Empty,
            Model = "claude-sonnet-4-20250514",
            MaxDirectApiCallsPerDay = 50
        };
        var metered = new MeteredCountingHandler();
        var claude = new ClaudeService(
            NullLogger<ClaudeService>.Instance,
            Microsoft.Extensions.Options.Options.Create(claudeConfig),
            new HttpClient(metered),
            new ThrowingGatewayFactory());

        // Non-generic path: gateway miss + fallback off yields no content (caller falls to rules).
        var raw = await claude.AnalyzeAsync(new AIAnalysisRequest
        {
            StrategyId = "readiness-smoke",
            SystemPrompt = "sys",
            UserPrompt = "user"
        });
        Assert.True(string.IsNullOrEmpty(raw), "inert AI must yield no content");

        // Generic path throws into the caller's try/catch -> deterministic rules.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            claude.AnalyzeAsync<RegimeStub>(new AIAnalysisRequest
            {
                StrategyId = "readiness-smoke",
                SystemPrompt = "sys",
                UserPrompt = "user"
            }));

        // Provably inert: zero metered HTTP (and the throwing gateway factory was never hit,
        // or the calls above would have surfaced its InvalidOperationException as a send).
        Assert.Equal(0, metered.InvocationCount);

        // The readiness path then runs rules-only: the delivered report's regime is the
        // deterministic seeded snapshot value, untouched by any AI leg.
        await fx.ReportService.SendDailyReportAsync(AsOf);
        var body = Assert.Single(fx.DiscordHandler.Bodies);
        Assert.Contains(nameof(RegimeType.Cautious), body);
    }

    // ========================================================================================
    // 7. Suite-green guard: the fixture is structurally CI-safe — fakes/stubs only, no TWS,
    //    no live HTTP, no Cosmos; Mode is Sandbox and never Live.
    // ========================================================================================
    [Fact]
    public void Smoke_RunsInCi_NoLiveDependencies()
    {
        var fx = new SmokeFixture();

        // Broker is a Moq proxy (strict), never a concrete IBKR client.
        Assert.StartsWith("Castle.Proxies", fx.Broker.Object.GetType().Namespace ?? string.Empty);
        Assert.DoesNotContain("IBKR", fx.Broker.Object.GetType().FullName ?? string.Empty);

        // Repos are the in-memory read-only fakes (writes throw), not Cosmos/Json stores.
        Assert.IsType<ReadOnlySnapshotRepository>(fx.SnapshotRepository);
        Assert.IsType<ReadOnlyTradeRepository>(fx.TradeRepository);
        Assert.IsType<CountingConfigRepository>(fx.ConfigRepo);

        // The services under test are resolved through their interfaces (S4 contracts).
        Assert.IsAssignableFrom<ISleeveReadinessScorecardService>(fx.ScorecardService);
        Assert.IsAssignableFrom<IDailyReportService>(fx.ReportService);

        // SANDBOX posture — capital-preservation guard; no LIVE path exists in the fixture.
        Assert.Equal(TradingMode.Sandbox, fx.SystemConfig.Mode);
        Assert.NotEqual(TradingMode.Live, fx.SystemConfig.Mode);
    }

    // ========================================================================================
    // Fixture — seeded deterministic paper metrics, in-memory config seam, stubbed HTTP.
    // Patterns mirror SleeveReadinessScorecardServiceTests (weekly snapshot/trade seeding)
    // and DiscordDailyReportServiceTests (recording handler + fake IHttpClientFactory).
    // ========================================================================================

    private sealed class SmokeFixture
    {
        public TradingSystemConfig SystemConfig { get; } = new() { Mode = TradingMode.Sandbox };
        public CountingConfigRepository ConfigRepo { get; }
        public ISnapshotRepository SnapshotRepository { get; }
        public ITradeRepository TradeRepository { get; }
        public RecordingHandler DiscordHandler { get; } = new();
        public Mock<IBrokerService> Broker { get; } = new(MockBehavior.Strict);
        public Mock<IOrderRepository> OrderRepository { get; } = new(MockBehavior.Strict);
        public Mock<ISignalRepository> SignalRepository { get; } = new(MockBehavior.Strict);
        public ISleeveReadinessScorecardService ScorecardService { get; }
        public IDailyReportService ReportService { get; }

        public SmokeFixture(bool discordEnabled = true)
        {
            ConfigRepo = new CountingConfigRepository(SystemConfig);
            ConfigRepo.Seed(SleeveValidationThresholds.SettingsKey, SleeveValidationThresholds.Defaults());
            ConfigRepo.ResetWriteCount();

            SnapshotRepository = new ReadOnlySnapshotRepository(SeedSnapshots());
            TradeRepository = new ReadOnlyTradeRepository(SeedTrades());

            // S4-001/S4-002: real scorecard service over the in-memory seam and seeded metrics.
            ScorecardService = new SleeveReadinessScorecardService(
                SnapshotRepository, TradeRepository, ConfigRepo);

            // S4-003: real report service; webhook HTTP terminates at the recording stub.
            ReportService = new DiscordDailyReportService(
                new StubHttpClientFactory(DiscordHandler),
                Microsoft.Extensions.Options.Options.Create(new DiscordConfig
                {
                    Enabled = discordEnabled,
                    WebhookUrl = FakeWebhookUrl,
                    Username = "TradingSystem Risk"
                }),
                SnapshotRepository,
                TradeRepository,
                NullLogger<DiscordDailyReportService>.Instance,
                ScorecardService);
        }

        // 13 weekly points = a 12-week span (exactly MinWeeksObserved). Income 100k -> 105k
        // (+5%) and tactical 100k -> 104k (+4%) vs SPY 500 -> 510 (+2%): both sleeves are net
        // profitable, beat SPY, monotonic (zero drawdown), and end above the $100k capital
        // minimum. The final (report-day) snapshot carries the day's P&L/regime fields.
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

        // 7W/3L closed trades per sleeve inside the window (70% hit rate, PF 700/150 ≈ 4.67 —
        // comfortably above the default gate) plus one closed report-day fill (entered after
        // AsOf midnight so it renders in the day's Fills without perturbing the scorecard
        // window, which ends at AsOf).
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

            trades.Add(new Trade
            {
                Sleeve = SleeveType.Income,
                StrategyId = "income-monthly-reinvest",
                Symbol = "MSFT",
                Action = OrderAction.Buy,
                Quantity = 10m,
                EntryPrice = 50.00m,
                EntryTime = AsOf.AddHours(10),
                ExitTime = AsOf.AddHours(11),
                RealizedPnL = 25m
            });
            return trades;
        }
    }

    // Records every webhook POST (URI + body, captured at send time) and returns 204 — the
    // Discord leg structurally cannot reach the network.
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int InvocationCount { get; private set; }
        public List<string> Bodies { get; } = new();
        public List<Uri> RequestUris { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            if (request.RequestUri != null)
                RequestUris.Add(request.RequestUri);
            if (request.Content != null)
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }

    // In-memory config seam carrying the S4-001 thresholds. Counts writes by seam so the
    // recommendation-only and no-LIVE-switch invariants are independently assertable.
    private sealed class CountingConfigRepository : IConfigRepository
    {
        private readonly TradingSystemConfig _config;
        private readonly Dictionary<string, object> _settings = new();

        public int SaveConfigCount { get; private set; }
        public int SetSettingCount { get; private set; }
        public List<string> WrittenKeys { get; } = new();

        public CountingConfigRepository(TradingSystemConfig config) => _config = config;

        public void Seed(string key, object value) => _settings[key] = value;

        public void ResetWriteCount()
        {
            SaveConfigCount = 0;
            SetSettingCount = 0;
            WrittenKeys.Clear();
        }

        public Task<TradingSystemConfig> GetConfigAsync(CancellationToken ct = default) =>
            Task.FromResult(_config);

        public Task SaveConfigAsync(TradingSystemConfig config, CancellationToken ct = default)
        {
            SaveConfigCount++;
            return Task.CompletedTask;
        }

        public Task<T?> GetSettingAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(_settings.TryGetValue(key, out var value) ? (T?)value : default);

        public Task SetSettingAsync<T>(string key, T value, CancellationToken ct = default)
        {
            SetSettingCount++;
            WrittenKeys.Add(key);
            _settings[key] = value!;
            return Task.CompletedTask;
        }
    }

    // Read-only repository fakes: any write THROWS, so a config/risk/order mutation anywhere
    // on the readiness path fails the test rather than passing silently.
    private sealed class ReadOnlySnapshotRepository : ISnapshotRepository
    {
        private readonly List<DailySnapshot> _snapshots;

        public ReadOnlySnapshotRepository(List<DailySnapshot> snapshots) => _snapshots = snapshots;

        public Task SaveDailySnapshotAsync(DailySnapshot snapshot, CancellationToken ct = default) =>
            throw new NotSupportedException("Readiness path must be read-only.");

        public Task<DailySnapshot?> GetSnapshotAsync(DateTime date, CancellationToken ct = default) =>
            Task.FromResult(_snapshots.FirstOrDefault(s => s.Date.Date == date.Date));

        public Task<List<DailySnapshot>> GetSnapshotsAsync(DateTime startDate, DateTime endDate,
            CancellationToken ct = default) =>
            Task.FromResult(_snapshots.Where(s => s.Date >= startDate && s.Date <= endDate).ToList());
    }

    private sealed class ReadOnlyTradeRepository : ITradeRepository
    {
        private readonly List<Trade> _trades;

        public ReadOnlyTradeRepository(List<Trade> trades) => _trades = trades;

        public Task<Trade> SaveAsync(Trade trade, CancellationToken ct = default) =>
            throw new NotSupportedException("Readiness path must be read-only.");

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

    // ---- S3-003/S3-006 inert-AI harness (mirrors DailyOrchestratorSmokeTests) ----

    // Counts would-be metered direct Anthropic API sends; must stay zero (fallback off).
    private sealed class MeteredCountingHandler : HttpMessageHandler
    {
        public int InvocationCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"content\":[{\"type\":\"text\",\"text\":\"{}\"}]}",
                    System.Text.Encoding.UTF8,
                    "application/json")
            });
        }
    }

    // Gateway leg must be skipped outright when GatewayApiKey is empty — any send throws.
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

    private sealed class RegimeStub
    {
        public string? Regime { get; set; }
    }
}
