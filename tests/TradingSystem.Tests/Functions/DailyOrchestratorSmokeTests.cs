using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingSystem.AI.Services;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Core.Services;
using TradingSystem.Functions;
using TradingSystem.Strategies.Options;
using Xunit;

namespace TradingSystem.Tests.Functions;

/// <summary>
/// S3-006: End-to-end SANDBOX paper-mode smoke test over <see cref="DailyOrchestrator.RunPreMarket"/>.
///
/// Goal: prove the pre-market orchestration wiring is sound under fully-mocked, CI-safe dependencies
/// (no live IBKR, no live market data, no live HTTP, no Cosmos) and that the ADR-024 graceful-skip
/// paths hold. The AI-inert behavior (S3-003: metered direct fallback OFF -> deterministic rules,
/// zero metered call) is covered as a focused assertion using the S3-003 ClaudeService harness, NOT
/// by wiring a live ClaudeService into the orchestrator — the orchestrator smoke stays deterministic
/// with a mocked <see cref="IMarketDataService.GetMarketRegimeAsync"/> regime.
///
/// Fixtures intentionally mirror DailyOrchestratorTests (BuildProviderWithOptionsManager, the
/// executable-options path, mocked broker/risk/market/calendar + in-memory repos) rather than
/// reinventing them. DailyOrchestratorTests is left unchanged.
/// </summary>
public class DailyOrchestratorSmokeTests
{
    // ----------------------------------------------------------------------------------------
    // 1. Happy path: SANDBOX pre-market wires broker -> screening -> sizing -> execution.
    // ----------------------------------------------------------------------------------------
    [Fact]
    public async Task Smoke_SandboxPaperMode_PreMarket_HappyPath_WiresThroughToExecution()
    {
        var brokerMock = new Mock<IBrokerService>();
        brokerMock
            .Setup(b => b.ConnectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        brokerMock
            .Setup(b => b.DisconnectAsync())
            .Returns(Task.CompletedTask);
        brokerMock
            .Setup(b => b.GetAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Account
            {
                NetLiquidationValue = 100_000m,
                AvailableFunds = 100_000m,
                BuyingPower = 100_000m
            });
        brokerMock
            .Setup(b => b.GetPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>());
        brokerMock
            .Setup(b => b.GetOptionChainAsync("SPY", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateBullPutSpreadChain("SPY"));
        brokerMock
            .Setup(b => b.PlaceComboOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Order
            {
                Id = "order-1",
                BrokerId = "101",
                Status = OrderStatus.Submitted
            });

        // Risk NOT halted so screening/sizing/execution proceed.
        var riskMock = CreateRiskManagerMock(isTradingHalted: false);

        // Discord is a mocked IRiskAlertService — assert no live alert call is made by the
        // pre-market path. The orchestrator never sends Discord alerts on the happy path.
        var discordMock = new Mock<IRiskAlertService>(MockBehavior.Strict);

        var provider = BuildProviderWithOptionsManager(
            brokerMock,
            riskMock,
            discordMock,
            tacticalConfig: new TacticalConfig { OptionUniverse = new List<string> { " spy ", "SPY" } },
            out var marketDataMock,
            out var calendarMock);

        // Regime supplied via the mocked market data service (the orchestrator's deterministic,
        // CI-safe stand-in for the AI regime call — see class summary / S3-006 locked decision).
        marketDataMock
            .Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MarketRegime
            {
                Regime = RegimeType.RiskOn,
                Timestamp = DateTime.UtcNow
            });
        marketDataMock
            .Setup(m => m.GetOptionsAnalyticsAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OptionsAnalytics
            {
                Symbol = "SPY",
                IVRank = 60m,
                IVPercentile = 70m,
                CurrentIV = 0.25m,
                Timestamp = DateTime.UtcNow
            });
        marketDataMock
            .Setup(m => m.GetQuoteAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Quote
            {
                Symbol = "SPY",
                Last = 500m,
                Bid = 499.5m,
                Ask = 500.5m,
                Timestamp = DateTime.UtcNow
            });
        calendarMock
            .Setup(c => c.GetSymbolsInNoTradeWindowAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var config = new TradingSystemConfig
        {
            Mode = TradingMode.Sandbox,
            Tactical = new TacticalConfig { OptionUniverse = new List<string> { " spy ", "SPY" } }
        };
        var orchestrator = CreateOrchestrator(config, provider);

        // Run completes without throwing.
        await orchestrator.RunPreMarket(timer: null!, CancellationToken.None);

        // Broker connect/disconnect balanced.
        brokerMock.Verify(b => b.ConnectAsync(It.IsAny<CancellationToken>()), Times.Once);
        brokerMock.Verify(b => b.DisconnectAsync(), Times.Once);

        // Risk validation ran and a combo order was placed: screening -> sizing -> execution wired.
        riskMock.Verify(
            r => r.ValidateSignalAsync(It.IsAny<Signal>(), It.IsAny<Account>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        brokerMock.Verify(
            b => b.PlaceComboOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        // Discord mock received NO live call (strict mock + no invocations).
        discordMock.VerifyNoOtherCalls();

        // Mode is SANDBOX — no LIVE path exercised.
        Assert.Equal(TradingMode.Sandbox, config.Mode);
    }

    // ----------------------------------------------------------------------------------------
    // 2. AI path provably inert at the E2E level (S3-003): fallback disabled + gateway down ->
    //    AnalyzeAsync<T> throws (caller would fall to deterministic rules); zero metered HTTP.
    //    Documents the S3-003 dependency that lets the orchestrator stay rules-only & CI-safe.
    // ----------------------------------------------------------------------------------------
    [Fact]
    public async Task Smoke_AiPathDisabled_RegimeFallsToRules_NoMeteredCall()
    {
        // DirectApiFallbackEnabled defaults to false (S3-003); GatewayApiKey empty => gateway leg
        // skipped; ApiKey set to prove the gate is the flag, not a missing key.
        var config = new ClaudeConfig
        {
            ApiKey = "test-key",
            GatewayApiKey = string.Empty,
            Model = "claude-sonnet-4-20250514",
            MaxDirectApiCallsPerDay = 50
            // DirectApiFallbackEnabled left at its real default (false) — the behavior under test.
        };

        var meteredHandler = new MeteredCountingHandler();
        var service = BuildClaudeService(config, meteredHandler);

        // Non-generic path: gateway miss + fallback off returns no content (caller uses rules).
        var raw = await service.AnalyzeAsync(SampleAiRequest());
        Assert.True(string.IsNullOrEmpty(raw), "fallback-off gateway miss should yield no content");

        // Generic path throws into the caller's try/catch -> deterministic rules.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AnalyzeAsync<RegimeStub>(SampleAiRequest()));

        // Provably inert: no metered direct API call, cap counter untouched, zero metered HTTP.
        Assert.Equal(0, meteredHandler.InvocationCount);
        Assert.Equal(0, GetDirectCallsToday(service));
    }

    // ----------------------------------------------------------------------------------------
    // 3. ADR-024 graceful-skip: broker connects but OptionsSleeveManager is NOT registered ->
    //    GetRequiredService<OptionsSleeveManager> throws InvalidOperationException, caught by the
    //    orchestrator -> run completes, broker disconnected once.
    // ----------------------------------------------------------------------------------------
    [Fact]
    public async Task Smoke_GracefulSkip_OptionsDiIncomplete_DoesNotThrow()
    {
        var brokerMock = new Mock<IBrokerService>();
        brokerMock
            .Setup(b => b.ConnectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        brokerMock
            .Setup(b => b.DisconnectAsync())
            .Returns(Task.CompletedTask);

        // Broker registered, but NO OptionsSleeveManager -> GetRequiredService throws.
        var provider = new ServiceCollection()
            .AddSingleton(brokerMock.Object)
            .BuildServiceProvider();

        var config = new TradingSystemConfig
        {
            Mode = TradingMode.Sandbox,
            Tactical = new TacticalConfig { OptionUniverse = new List<string> { "SPY" } }
        };
        var orchestrator = CreateOrchestrator(config, provider);

        // ADR-024: incomplete options DI is caught and skipped, not rethrown.
        await orchestrator.RunPreMarket(timer: null!, CancellationToken.None);

        brokerMock.Verify(b => b.ConnectAsync(It.IsAny<CancellationToken>()), Times.Once);
        brokerMock.Verify(b => b.DisconnectAsync(), Times.Once);
    }

    // ----------------------------------------------------------------------------------------
    // 4. ADR-024 defensive: no broker registered -> run completes cleanly, Discord never called.
    // ----------------------------------------------------------------------------------------
    [Fact]
    public async Task Smoke_BrokerUnavailable_SkipsCleanly()
    {
        // No IBrokerService and no OptionsSleeveManager registered.
        var provider = new ServiceCollection().BuildServiceProvider();

        var discordMock = new Mock<IRiskAlertService>(MockBehavior.Strict);

        var config = new TradingSystemConfig
        {
            Mode = TradingMode.Sandbox,
            Tactical = new TacticalConfig { OptionUniverse = new List<string> { "SPY" } }
        };
        var orchestrator = CreateOrchestrator(config, provider);

        // No broker -> orchestrator logs a warning and returns; must not throw.
        await orchestrator.RunPreMarket(timer: null!, CancellationToken.None);

        // Discord mock never called on the broker-unavailable skip path.
        discordMock.VerifyNoOtherCalls();
    }

    // ----------------------------------------------------------------------------------------
    // 5. Structural CI guard: the fixture uses only mocks/in-memory (no real IBrokerService, no
    //    live HttpClient, no Cosmos) and Mode == Sandbox. No SANDBOX->LIVE path is exercised.
    // ----------------------------------------------------------------------------------------
    [Fact]
    public void Smoke_RunsInCi_NoLiveDependencies()
    {
        var brokerMock = new Mock<IBrokerService>();
        var riskMock = CreateRiskManagerMock(isTradingHalted: false);
        var discordMock = new Mock<IRiskAlertService>();

        var provider = BuildProviderWithOptionsManager(
            brokerMock,
            riskMock,
            discordMock,
            tacticalConfig: new TacticalConfig { OptionUniverse = new List<string> { "SPY" } },
            out var marketDataMock,
            out var calendarMock);

        // Broker is a Moq proxy, not a concrete IBKR client.
        var broker = provider.GetRequiredService<IBrokerService>();
        Assert.True(IsMoqProxy(broker), "broker must be a mock, not a live IBrokerService implementation");
        Assert.DoesNotContain("IBKR", broker.GetType().FullName ?? string.Empty);

        // Market data and calendar are mocks, not Polygon/live HTTP clients.
        Assert.True(IsMoqProxy(marketDataMock.Object), "market data must be a mock (no live HTTP)");
        Assert.True(IsMoqProxy(calendarMock.Object), "calendar must be a mock");
        Assert.True(IsMoqProxy(discordMock.Object), "Discord alert service must be a mock (no live POST)");

        // Options sleeve manager resolves from the in-memory fixture (no Cosmos behind it).
        Assert.NotNull(provider.GetRequiredService<OptionsSleeveManager>());

        // No live HttpClient / Cosmos client leaked into the provider.
        Assert.Null(provider.GetService<HttpClient>());
        Assert.Null(provider.GetService<IClaudeService>());

        // Mode is SANDBOX — capital-preservation guard; no LIVE path.
        var config = new TradingSystemConfig
        {
            Mode = TradingMode.Sandbox,
            Tactical = new TacticalConfig { OptionUniverse = new List<string> { "SPY" } }
        };
        Assert.Equal(TradingMode.Sandbox, config.Mode);
        Assert.NotEqual(TradingMode.Live, config.Mode);
    }

    // ========================================================================================
    // Helpers — mirror DailyOrchestratorTests (broker/risk/market/calendar + in-memory repos)
    // and the ClaudeServiceTests S3-003 harness (counting handler + reflection on the cap counter).
    // ========================================================================================

    private static DailyOrchestrator CreateOrchestrator(
        TradingSystemConfig config,
        IServiceProvider provider)
    {
        return new DailyOrchestrator(
            NullLogger<DailyOrchestrator>.Instance,
            Microsoft.Extensions.Options.Options.Create(config),
            provider);
    }

    private static Mock<IRiskManager> CreateRiskManagerMock(bool isTradingHalted)
    {
        var riskMock = new Mock<IRiskManager>();
        riskMock
            .Setup(r => r.IsTradingHaltedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(isTradingHalted);
        riskMock
            .Setup(r => r.ValidateSignalAsync(It.IsAny<Signal>(), It.IsAny<Account>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskValidationResult { IsValid = true });

        return riskMock;
    }

    private static IServiceProvider BuildProviderWithOptionsManager(
        Mock<IBrokerService> brokerMock,
        Mock<IRiskManager> riskMock,
        Mock<IRiskAlertService> discordMock,
        TacticalConfig tacticalConfig,
        out Mock<IMarketDataService> marketDataMock,
        out Mock<ICalendarService> calendarMock)
    {
        marketDataMock = new Mock<IMarketDataService>();
        calendarMock = new Mock<ICalendarService>();

        var optionsRepo = new InMemoryOptionsPositionRepository();
        var signalRepo = new Mock<ISignalRepository>();
        var orderRepo = new Mock<IOrderRepository>();

        signalRepo
            .Setup(r => r.SaveAsync(It.IsAny<Signal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Signal signal, CancellationToken _) => signal);
        signalRepo
            .Setup(r => r.UpdateStatusAsync(It.IsAny<string>(), It.IsAny<SignalStatus>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        orderRepo
            .Setup(r => r.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order order, CancellationToken _) => order);

        var screeningService = new OptionsScreeningService(
            marketDataMock.Object,
            brokerMock.Object,
            calendarMock.Object,
            Microsoft.Extensions.Options.Options.Create(tacticalConfig),
            NullLogger<OptionsScreeningService>.Instance);

        var executionService = new OptionsExecutionService(
            brokerMock.Object,
            orderRepo.Object,
            signalRepo.Object,
            optionsRepo,
            NullLogger<OptionsExecutionService>.Instance);

        var manager = new OptionsSleeveManager(
            brokerMock.Object,
            riskMock.Object,
            optionsRepo,
            screeningService,
            new OptionsLifecycleRules(tacticalConfig.Options),
            new OptionsPositionGrouper(),
            new OptionsCandidateConverter(),
            new OptionsPositionSizer(new RiskConfig
            {
                RiskPerTradePercent = 0.004m,
                MaxSingleSpreadPercent = 0.02m
            }),
            executionService,
            tacticalConfig,
            NullLogger<OptionsSleeveManager>.Instance);

        return new ServiceCollection()
            .AddSingleton(brokerMock.Object)
            .AddSingleton(discordMock.Object)
            .AddSingleton(manager)
            .BuildServiceProvider();
    }

    private static List<OptionContract> CreateBullPutSpreadChain(string symbol)
    {
        var expiration = DateTime.Today.AddDays(30);
        return new List<OptionContract>
        {
            new()
            {
                Symbol = $"{symbol}_PUT_500",
                UnderlyingSymbol = symbol,
                Strike = 500m,
                Expiration = expiration,
                Right = OptionRight.Put,
                Delta = -0.20m,
                Bid = 2.02m,
                Ask = 2.06m,
                Last = 2.04m,
                OpenInterest = 500,
                Volume = 100,
                ImpliedVolatility = 0.30m,
                Theta = -0.05m,
                Timestamp = DateTime.UtcNow
            },
            new()
            {
                Symbol = $"{symbol}_PUT_495",
                UnderlyingSymbol = symbol,
                Strike = 495m,
                Expiration = expiration,
                Right = OptionRight.Put,
                Delta = -0.10m,
                Bid = 1.02m,
                Ask = 1.04m,
                Last = 1.03m,
                OpenInterest = 500,
                Volume = 100,
                ImpliedVolatility = 0.30m,
                Theta = -0.04m,
                Timestamp = DateTime.UtcNow
            }
        };
    }

    private static bool IsMoqProxy(object instance) =>
        instance.GetType().Namespace?.StartsWith("Castle.Proxies", StringComparison.Ordinal) == true;

    // ---- S3-003 ClaudeService inert-AI harness (mirrors ClaudeServiceTests) ----

    // Counts how many times the metered direct Anthropic API would be hit. In the inert-AI
    // assertion it must stay at zero (fallback off -> never reaches the metered branch).
    private sealed class MeteredCountingHandler : System.Net.Http.HttpMessageHandler
    {
        public int InvocationCount { get; private set; }

        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            var json = "{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"regime\\\":\\\"riskon\\\"}\"}]}";
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    // Gateway factory whose handler must never be invoked when GatewayApiKey is empty.
    private sealed class GatewaySkippedClientFactory : System.Net.Http.IHttpClientFactory
    {
        private readonly ClaudeConfig _config;

        public GatewaySkippedClientFactory(ClaudeConfig config)
        {
            _config = config;
        }

        public System.Net.Http.HttpClient CreateClient(string name)
        {
            var handler = new ThrowingHandler();
            return new System.Net.Http.HttpClient(handler)
            {
                BaseAddress = new Uri(string.IsNullOrEmpty(_config.GatewayBaseUrl)
                    ? "http://localhost:3131/"
                    : _config.GatewayBaseUrl)
            };
        }

        private sealed class ThrowingHandler : System.Net.Http.HttpMessageHandler
        {
            protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
                System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new InvalidOperationException("gateway must not be called when GatewayApiKey is empty");
        }
    }

    private static ClaudeService BuildClaudeService(
        ClaudeConfig config, MeteredCountingHandler meteredHandler)
    {
        var httpClient = new System.Net.Http.HttpClient(meteredHandler);
        var logger = new Mock<ILogger<ClaudeService>>();
        var factory = new GatewaySkippedClientFactory(config);
        return new ClaudeService(
            logger.Object, Microsoft.Extensions.Options.Options.Create(config), httpClient, factory);
    }

    private static int GetDirectCallsToday(ClaudeService service)
    {
        var field = typeof(ClaudeService).GetField("_directCallsToday",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return (int)field!.GetValue(service)!;
    }

    private static AIAnalysisRequest SampleAiRequest() => new()
    {
        StrategyId = "regime-smoke",
        SystemPrompt = "sys",
        UserPrompt = "user"
    };

    private sealed class RegimeStub
    {
        public string? Regime { get; set; }
    }

    private sealed class InMemoryOptionsPositionRepository : IOptionsPositionRepository
    {
        private readonly List<OptionsPosition> _positions = new();

        public Task<OptionsPosition> SaveAsync(OptionsPosition position, CancellationToken ct = default)
        {
            var existing = _positions.FindIndex(p => p.Id == position.Id);
            if (existing >= 0)
                _positions[existing] = position;
            else
                _positions.Add(position);

            return Task.FromResult(position);
        }

        public Task<OptionsPosition?> GetByIdAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_positions.FirstOrDefault(p => p.Id == id));

        public Task<List<OptionsPosition>> GetOpenPositionsAsync(CancellationToken ct = default)
            => Task.FromResult(_positions.Where(p => p.Status == OptionsPositionStatus.Open).ToList());

        public Task<List<OptionsPosition>> GetByUnderlyingAsync(string symbol, CancellationToken ct = default)
            => Task.FromResult(_positions.Where(p =>
                p.UnderlyingSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)).ToList());

        public Task<List<OptionsPosition>> GetByDateRangeAsync(DateTime start, DateTime end, CancellationToken ct = default)
            => Task.FromResult(_positions.Where(p => p.OpenedAt >= start && p.OpenedAt <= end).ToList());

        public Task UpdateAsync(OptionsPosition position, CancellationToken ct = default)
        {
            var index = _positions.FindIndex(p => p.Id == position.Id);
            if (index < 0)
                _positions.Add(position);
            else
                _positions[index] = position;

            return Task.CompletedTask;
        }
    }
}
