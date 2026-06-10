using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Functions;
using Xunit;

namespace TradingSystem.Tests.Functions;

/// <summary>
/// S5-003: operational failure alerting. The existing DiscordRiskAlertService implements the
/// new IOperationalAlertService (Default D5 — same webhook/config/named client/S3-004
/// hardening; operational embeds orange 15105570 vs risk-stop red 15158332) and three call
/// sites are wired best-effort (pre-market connect failure, EOD connect failure, both timer
/// catch blocks — exception TYPE NAME only, never ex.Message). Alert-spam guard is once per
/// run per failure category (Default D6). ALL HTTP is stubbed — no live Discord POST is ever
/// made. Alert failure must never change an orchestration outcome.
/// </summary>
public class OperationalAlertTests
{
    private const string SecretToken = "secrettoken";
    private const int OperationalOrange = 15105570;
    private const int RiskStopRed = 15158332;

    // ---------- HTTP stubbing (no live POSTs; bodies captured at send time) ----------

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>? _responder;
        private readonly Exception? _throw;

        public int InvocationCount { get; private set; }
        public List<string> Bodies { get; } = new();

        private StubHandler(Func<HttpRequestMessage, HttpResponseMessage>? responder, Exception? toThrow)
        {
            _responder = responder;
            _throw = toThrow;
        }

        public static StubHandler ReturnsNoContent()
            => new(_ => new HttpResponseMessage(HttpStatusCode.NoContent), null);

        public static StubHandler Throws(Exception ex) => new(null, ex);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            // Capture the body synchronously at send time — the request/content may be
            // disposed by HttpClient after the call completes.
            var body = request.Content == null
                ? string.Empty
                : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            Bodies.Add(body);

            if (_throw != null)
                throw _throw;

            return Task.FromResult(_responder!(request));
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }

    private static DiscordConfig Config(bool enabled = true) => new()
    {
        Enabled = enabled,
        WebhookUrl = "https://discord.com/api/webhooks/123/" + SecretToken,
        Username = "TradingSystem Risk"
    };

    // Real DiscordRiskAlertService over the stub handler — the SAME instance serves both
    // IRiskAlertService and IOperationalAlertService, mirroring the Program.cs registration.
    private static (DiscordRiskAlertService Service, StubHandler Handler, Mock<ILogger<DiscordRiskAlertService>> Logger)
        BuildAlertService(StubHandler handler, bool enabled = true)
    {
        var logger = new Mock<ILogger<DiscordRiskAlertService>>();
        Func<TimeSpan, CancellationToken, Task> delay = (_, _) => Task.CompletedTask;
        var service = new DiscordRiskAlertService(
            new FakeHttpClientFactory(handler),
            Microsoft.Extensions.Options.Options.Create(Config(enabled)),
            logger.Object,
            delay);
        return (service, handler, logger);
    }

    private static IEnumerable<string> AllLogArgStrings(Mock<ILogger<DiscordRiskAlertService>> logger)
    {
        foreach (var inv in logger.Invocations)
        {
            if (inv.Method.Name != nameof(ILogger.Log))
                continue;
            foreach (var arg in inv.Arguments)
            {
                if (arg == null) continue;
                yield return arg.ToString() ?? string.Empty;
            }
        }
    }

    private static int LogCount(Mock<ILogger<DiscordRiskAlertService>> logger, LogLevel level) =>
        logger.Invocations.Count(i =>
            i.Method.Name == nameof(ILogger.Log) &&
            i.Arguments.Count > 0 &&
            i.Arguments[0] is LogLevel l && l == level);

    // ---------- orchestration fixtures ----------

    private static DailyOrchestrator CreateOrchestrator(IServiceProvider provider)
    {
        return new DailyOrchestrator(
            NullLogger<DailyOrchestrator>.Instance,
            Microsoft.Extensions.Options.Options.Create(new TradingSystemConfig()),
            provider);
    }

    private static Mock<IBrokerService> CreateBrokerMock(bool connectSucceeds)
    {
        var broker = new Mock<IBrokerService>();
        broker.Setup(b => b.ConnectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(connectSucceeds);
        broker.Setup(b => b.DisconnectAsync()).Returns(Task.CompletedTask);
        return broker;
    }

    private sealed class CountingSnapshotRepository : ISnapshotRepository
    {
        public int SaveCount { get; private set; }

        public Task SaveDailySnapshotAsync(DailySnapshot snapshot, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task<DailySnapshot?> GetSnapshotAsync(DateTime date, CancellationToken cancellationToken = default)
            => Task.FromResult<DailySnapshot?>(null);

        public Task<List<DailySnapshot>> GetSnapshotsAsync(
            DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<DailySnapshot>());
    }

    // EOD service with a failing broker connect: RiskManager is never reached on that path, so a
    // strict mock locks the "no sync, no snapshot work" degrade contract.
    private static EndOfDayService CreateEndOfDayService(
        Mock<IBrokerService> broker,
        IOperationalAlertService operationalAlerts,
        CountingSnapshotRepository snapshots)
    {
        var riskManager = new Mock<IRiskManager>(MockBehavior.Strict);
        return new EndOfDayService(
            broker.Object,
            riskManager.Object,
            NullLogger<EndOfDayService>.Instance,
            snapshots,
            tradeRepository: null,
            marketDataService: null,
            operationalAlertService: operationalAlerts);
    }

    // ---------- 1. pre-market connect failure ----------

    [Fact]
    public async Task RunPreMarket_BrokerConnectFails_SendsOneOrangeConnectFailureAlert()
    {
        var (alerts, handler, _) = BuildAlertService(StubHandler.ReturnsNoContent());
        var broker = CreateBrokerMock(connectSucceeds: false);
        var provider = new ServiceCollection()
            .AddSingleton(broker.Object)
            .AddSingleton<IOperationalAlertService>(alerts)
            .BuildServiceProvider();
        var orchestrator = CreateOrchestrator(provider);

        // Run still degrades exactly as before: options sleeve skipped, no throw.
        await orchestrator.RunPreMarket(timer: null!, CancellationToken.None);

        broker.Verify(b => b.ConnectAsync(It.IsAny<CancellationToken>()), Times.Once);
        broker.Verify(b => b.DisconnectAsync(), Times.Never);

        Assert.Equal(1, handler.InvocationCount);
        var body = Assert.Single(handler.Bodies);
        Assert.Contains("Broker Connect Failure", body);
        Assert.Contains("re-market", body); // pre-market context, case-tolerant
        Assert.Contains(OperationalOrange.ToString(), body);
        // The embed carries the runId (8-hex run identifier generated by the timer).
        Assert.Matches(new Regex("RunId: [0-9a-f]{8}"), body);
    }

    // ---------- 2. EOD connect failure ----------

    [Fact]
    public async Task EndOfDay_BrokerConnectFails_SendsOneAlert_NoSnapshotNoThrow()
    {
        var (alerts, handler, _) = BuildAlertService(StubHandler.ReturnsNoContent());
        var broker = CreateBrokerMock(connectSucceeds: false);
        var snapshots = new CountingSnapshotRepository();
        var service = CreateEndOfDayService(broker, alerts, snapshots);

        var result = await service.RunAsync("run-1", CancellationToken.None);

        // S5-001 regression intact: no snapshot write, no throw, structured failure result.
        Assert.False(result.BrokerConnected);
        Assert.False(result.SnapshotPersisted);
        Assert.Equal(0, snapshots.SaveCount);

        Assert.Equal(1, handler.InvocationCount);
        var body = Assert.Single(handler.Bodies);
        Assert.Contains("Broker Connect Failure", body);
        Assert.Contains("nd-of-day", body); // end-of-day context, case-tolerant
        Assert.Contains("run-1", body);
        Assert.Contains(OperationalOrange.ToString(), body);
    }

    // ---------- 3. orchestration-failure alert before rethrow ----------

    [Fact]
    public async Task RunEndOfDay_BodyThrows_SendsOrchestrationFailureAlert_AndRethrows()
    {
        var (alerts, handler, _) = BuildAlertService(StubHandler.ReturnsNoContent());
        var endOfDay = new Mock<IEndOfDayService>();
        endOfDay
            .Setup(s => s.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("uri-like-secret-detail"));

        var provider = new ServiceCollection()
            .AddSingleton(endOfDay.Object)
            .AddSingleton<IOperationalAlertService>(alerts)
            .BuildServiceProvider();
        var orchestrator = CreateOrchestrator(provider);

        // Rethrow preserved (App Insights failure signal) AND exactly one alert POST.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.RunEndOfDay(timer: null!, CancellationToken.None));

        Assert.Equal(1, handler.InvocationCount);
        var body = Assert.Single(handler.Bodies);
        Assert.Contains("Orchestration", body);
        // Exception TYPE NAME only — messages can echo URIs/secrets.
        Assert.Contains(nameof(InvalidOperationException), body);
        Assert.DoesNotContain("uri-like-secret-detail", body);
        Assert.Contains(OperationalOrange.ToString(), body);
    }

    [Fact]
    public async Task RunPreMarket_BodyThrows_SendsOrchestrationFailureAlert_AndRethrows()
    {
        var (alerts, handler, _) = BuildAlertService(StubHandler.ReturnsNoContent());
        var broker = new Mock<IBrokerService>();
        broker
            .Setup(b => b.ConnectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("tws://127.0.0.1:7497 secret detail"));

        var provider = new ServiceCollection()
            .AddSingleton(broker.Object)
            .AddSingleton<IOperationalAlertService>(alerts)
            .BuildServiceProvider();
        var orchestrator = CreateOrchestrator(provider);

        await Assert.ThrowsAsync<TimeoutException>(
            () => orchestrator.RunPreMarket(timer: null!, CancellationToken.None));

        Assert.Equal(1, handler.InvocationCount);
        var body = Assert.Single(handler.Bodies);
        Assert.Contains("Orchestration", body);
        Assert.Contains(nameof(TimeoutException), body);
        Assert.DoesNotContain("secret detail", body);
        Assert.DoesNotContain("7497", body);
    }

    // ---------- 4. Enabled=false stays quiet (B-006 pattern on the new path) ----------

    [Fact]
    public async Task Disabled_OperationalAlert_NoPost_PerCallDebugSkip()
    {
        var (alerts, handler, logger) = BuildAlertService(StubHandler.ReturnsNoContent(), enabled: false);

        await alerts.SendOperationalAlertAsync("Broker Connect Failure — Pre-Market", "RunId: deadbeef");

        Assert.Equal(0, handler.InvocationCount);
        Assert.Equal(1, LogCount(logger, LogLevel.Debug));
        // Exactly ONE Information entry: the one-time ctor disabled notice (S4-005). The
        // per-call skip itself must stay at Debug.
        Assert.Equal(1, LogCount(logger, LogLevel.Information));
    }

    // ---------- 5. alert transport failure never changes the orchestration outcome ----------

    [Fact]
    public async Task EndOfDay_AlertTransportFails_RunOutcomeUnchanged()
    {
        var (alerts, handler, _) = BuildAlertService(
            StubHandler.Throws(new HttpRequestException("connection refused https://discord.com/api/webhooks/123/" + SecretToken)));
        var broker = CreateBrokerMock(connectSucceeds: false);
        var snapshots = new CountingSnapshotRepository();
        var service = CreateEndOfDayService(broker, alerts, snapshots);

        // No secondary failure: the alert service swallows the transport error.
        var result = await service.RunAsync("run-1", CancellationToken.None);

        Assert.False(result.BrokerConnected);
        Assert.Equal(0, snapshots.SaveCount);
        Assert.Equal(1, handler.InvocationCount);
    }

    [Fact]
    public async Task RunPreMarket_AlertTransportFails_DegradeStillCleanNoThrow()
    {
        var (alerts, _, _) = BuildAlertService(StubHandler.Throws(new HttpRequestException("boom")));
        var broker = CreateBrokerMock(connectSucceeds: false);
        var provider = new ServiceCollection()
            .AddSingleton(broker.Object)
            .AddSingleton<IOperationalAlertService>(alerts)
            .BuildServiceProvider();
        var orchestrator = CreateOrchestrator(provider);

        await orchestrator.RunPreMarket(timer: null!, CancellationToken.None);
    }

    // ---------- 6. spam guard: once per run per failure category (Default D6) ----------

    [Fact]
    public async Task EndOfDay_SameRunId_ConnectFailureAlertsAtMostOnce()
    {
        var (alerts, handler, _) = BuildAlertService(StubHandler.ReturnsNoContent());
        var broker = CreateBrokerMock(connectSucceeds: false);
        var service = CreateEndOfDayService(broker, alerts, new CountingSnapshotRepository());

        // Same run scope (same runId) hits the failure path twice → exactly one POST.
        await service.RunAsync("run-1", CancellationToken.None);
        await service.RunAsync("run-1", CancellationToken.None);

        Assert.Equal(1, handler.InvocationCount);

        // A NEW run is a new scope — its failure alerts again (worst case ~2 ops alerts/day/run).
        await service.RunAsync("run-2", CancellationToken.None);
        Assert.Equal(2, handler.InvocationCount);
    }

    // ---------- 7. token redaction on the new path ----------

    [Fact]
    public async Task OperationalAlert_DeliveryFailure_NeverLogsWebhookToken()
    {
        var (alerts, _, logger) = BuildAlertService(
            StubHandler.Throws(new HttpRequestException(
                "POST https://discord.com/api/webhooks/123/" + SecretToken + " failed")));

        await alerts.SendOperationalAlertAsync("Broker Connect Failure — End of Day", "RunId: run-1");

        var logs = AllLogArgStrings(logger).ToList();
        Assert.True(LogCount(logger, LogLevel.Error) >= 1, "dropped ops alert should stay loud");
        Assert.Contains(logs, s => s.Contains("NOT delivered"));
        Assert.Contains(logs, s => s.Contains("AlertDropped=True"));
        Assert.DoesNotContain(logs, s => s.Contains(SecretToken));
        Assert.DoesNotContain(logs, s => s.Contains("/api/webhooks/123"));
    }

    // ---------- 8. regression: risk-stop path stays red with metrics fields ----------

    [Fact]
    public async Task RiskStopAlert_StillRedWithMetricsFields_OpsAlertOrangeWithoutFields()
    {
        var (alerts, handler, _) = BuildAlertService(StubHandler.ReturnsNoContent());
        var metrics = new RiskMetrics
        {
            DailyPnLPercent = -0.025m,
            WeeklyPnLPercent = -0.041m,
            CurrentDrawdown = -0.06m,
            OpenPositionCount = 3
        };

        await alerts.SendDailyStopTriggeredAsync(metrics);
        await alerts.SendOperationalAlertAsync("Broker Connect Failure — End of Day", "RunId: run-1");

        Assert.Equal(2, handler.InvocationCount);

        var riskBody = handler.Bodies[0];
        Assert.Contains(RiskStopRed.ToString(), riskBody);
        Assert.Contains("\"fields\"", riskBody);
        // Metrics fields block intact ("Daily P&L" serializes with &, so assert
        // escape-proof field names).
        Assert.Contains("Drawdown", riskBody);
        Assert.Contains("Open Positions", riskBody);
        Assert.DoesNotContain(OperationalOrange.ToString(), riskBody);

        var opsBody = handler.Bodies[1];
        Assert.Contains(OperationalOrange.ToString(), opsBody);
        Assert.DoesNotContain("\"fields\"", opsBody);
        Assert.DoesNotContain(RiskStopRed.ToString(), opsBody);
    }

    // ---------- wiring degrade: alert service not registered ----------

    [Fact]
    public async Task RunEndOfDay_NoOperationalAlertServiceRegistered_ThrowStillPropagatesCleanly()
    {
        var endOfDay = new Mock<IEndOfDayService>();
        endOfDay
            .Setup(s => s.RunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var provider = new ServiceCollection()
            .AddSingleton(endOfDay.Object)
            .BuildServiceProvider();
        var orchestrator = CreateOrchestrator(provider);

        // Null-tolerant: no IOperationalAlertService → identical pre-S5-003 behavior.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.RunEndOfDay(timer: null!, CancellationToken.None));
    }
}
