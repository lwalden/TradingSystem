using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Moq;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Models;
using TradingSystem.Functions;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace TradingSystem.Tests.Functions;

/// <summary>
/// S6-001 (Default D6): DiscordIncomeReportService renders the monthly reinvest plan as a
/// digest-class NEUTRAL embed (never risk red / ops orange) on its own named client
/// ("DiscordIncomeReport", 8s timeout in Program.cs), reusing the S3-004 hardening verbatim:
/// https-only Discord host allow-list, token-redacted logging, bounded 429 retry with an
/// injectable zero-wait delay, Enabled==false skip (one-time ctor Information notice,
/// per-call Debug skip), and the loud AlertDropped=true terminal log. ALL HTTP is mocked via
/// a controllable handler — no live POST is ever made. Content assertions lock the locked
/// posture line (recommendation-only by default), the D4 cash input, and the Default D8
/// "no buys proposed" honesty line. No secret and no exception message ever reaches the
/// payload or a log.
/// </summary>
public class DiscordIncomeReportServiceTests
{
    private const string SecretToken = "incomesecrettoken";

    private static readonly DateTime PlanDate = new(2026, 7, 1, 13, 30, 0, DateTimeKind.Utc);

    // ---------- HTTP stub (same pattern as the daily-report tests; duplicated so this test
    // file stays independently runnable — no cross-test-file imports) ----------

    private sealed class StubHandler : HttpMessageHandler
    {
        public int InvocationCount { get; private set; }
        public List<string> Bodies { get; } = new();
        private readonly Queue<Func<HttpResponseMessage>> _responders;
        private Func<HttpResponseMessage>? _last;

        private StubHandler(IEnumerable<Func<HttpResponseMessage>> responders)
        {
            _responders = new Queue<Func<HttpResponseMessage>>(responders);
        }

        public static StubHandler ReturnsStatuses(params HttpStatusCode[] codes)
            => new(codes.Select<HttpStatusCode, Func<HttpResponseMessage>>(c => () => new HttpResponseMessage(c)));

        public static StubHandler ReturnsResponses(params Func<HttpResponseMessage>[] factories)
            => new(factories);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            if (request.Content != null)
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));

            if (_responders.Count > 0)
                _last = _responders.Dequeue();
            return (_last ?? (() => new HttpResponseMessage(HttpStatusCode.NoContent)))();
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }

    // ---------- fixtures ----------

    private static DiscordConfig Config(bool enabled = true, string? url = null) => new()
    {
        Enabled = enabled,
        WebhookUrl = url ?? "https://discord.com/api/webhooks/456/" + SecretToken,
        Username = "TradingSystem Risk"
    };

    private static (DiscordIncomeReportService Service, StubHandler Handler, Mock<ILogger<DiscordIncomeReportService>> Logger)
        Build(DiscordConfig? config = null, StubHandler? handler = null, bool orderPlacementEnabled = false)
    {
        handler ??= StubHandler.ReturnsStatuses(HttpStatusCode.NoContent);
        var logger = new Mock<ILogger<DiscordIncomeReportService>>();
        var service = new DiscordIncomeReportService(
            new FakeHttpClientFactory(handler),
            MsOptions.Create(config ?? Config()),
            MsOptions.Create(new IncomeSleeveConfig { OrderPlacementEnabled = orderPlacementEnabled }),
            logger.Object,
            (_, _) => Task.CompletedTask); // zero-wait injectable delay
        return (service, handler, logger);
    }

    private static ReinvestmentPlan Plan(int buys = 2, decimal amountEach = 5_000m)
    {
        var plan = new ReinvestmentPlan
        {
            PlanDate = PlanDate,
            AvailableCash = 70_000m
        };
        for (var i = 0; i < buys; i++)
        {
            plan.ProposedBuys.Add(new ReinvestmentOrder
            {
                Symbol = $"SYM{i}",
                Category = IncomeCategory.EquityREIT,
                Amount = amountEach,
                Rationale = $"Reduce EquityREIT drift of -{i + 1}.0%"
            });
        }
        return plan;
    }

    private static IncomeSleeveState State() => new()
    {
        TotalValue = 50_000m,
        CategoryDrift = new Dictionary<IncomeCategory, decimal>
        {
            { IncomeCategory.DividendGrowthETF, 0.75m },
            { IncomeCategory.EquityREIT, -0.10m }
        }
    };

    private static List<(LogLevel Level, string Message)> CapturedLogs(Mock<ILogger<DiscordIncomeReportService>> logger)
    {
        var entries = new List<(LogLevel, string)>();
        foreach (var inv in logger.Invocations)
        {
            if (inv.Method.Name != nameof(ILogger.Log))
                continue;
            entries.Add(((LogLevel)inv.Arguments[0], inv.Arguments[2]?.ToString() ?? string.Empty));
        }
        return entries;
    }

    // ---------- content: title, posture, D4 cash, neutral color ----------

    [Fact]
    public async Task Send_PostsSingleEmbed_WithTitlePostureCashAndSleeveTotal()
    {
        var (service, handler, _) = Build();

        await service.SendReinvestmentPlanReportAsync(Plan(), State(), ordersPlaced: 0);

        var raw = Assert.Single(handler.Bodies);
        // System.Text.Json escapes non-ASCII (the em dash renders as —) — unescape so
        // the assertion locks the exact human-visible title the runbook documents.
        var body = System.Text.RegularExpressions.Regex.Unescape(raw);
        Assert.Contains("Income Reinvest Plan —", body);
        // Locked decision 1: the default posture is stated, with the exact flag key.
        Assert.Contains("recommendation-only", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IncomeSleeve:OrderPlacementEnabled=false", body);
        // D4 cash input, labeled as an estimate.
        Assert.Contains("70,000.00", body);
        Assert.Contains("estimate", body, StringComparison.OrdinalIgnoreCase);
        // Sleeve total value renders for context.
        Assert.Contains("50,000.00", body);
        // Mention parsing disabled.
        Assert.Contains("\"allowed_mentions\"", body);
    }

    [Fact]
    public async Task Send_UsesNeutralDigestColor_NotRiskRedOrOpsOrange()
    {
        var (service, handler, _) = Build();

        await service.SendReinvestmentPlanReportAsync(Plan(), State(), ordersPlaced: 0);

        var body = Assert.Single(handler.Bodies);
        // Digest-class neutral grey — never risk red (15158332) or ops orange (15105570).
        Assert.Contains("\"color\":9807270", body);
        Assert.DoesNotContain("15158332", body);
        Assert.DoesNotContain("15105570", body);
    }

    [Fact]
    public async Task Send_RendersProposedBuys_WithSymbolCategoryAmountAndRationale()
    {
        var (service, handler, _) = Build();

        await service.SendReinvestmentPlanReportAsync(Plan(buys: 2), State(), ordersPlaced: 0);

        var body = Assert.Single(handler.Bodies);
        Assert.Contains("SYM0", body);
        Assert.Contains("SYM1", body);
        Assert.Contains("EquityREIT", body);
        Assert.Contains("5,000.00", body);
        Assert.Contains("Reduce EquityREIT drift", body);
    }

    [Fact]
    public async Task Send_CapsRenderedBuys_WithOverflowLine()
    {
        var (service, handler, _) = Build();

        await service.SendReinvestmentPlanReportAsync(Plan(buys: 12), State(), ordersPlaced: 0);

        var body = Assert.Single(handler.Bodies);
        Assert.Contains("SYM9", body);            // 10th buy renders
        Assert.DoesNotContain("SYM10", body);     // 11th does not
        Assert.Contains("2 more", body);          // overflow note
    }

    [Fact]
    public async Task Send_EmptyPlan_SaysNoBuysProposed()
    {
        var (service, handler, _) = Build();

        await service.SendReinvestmentPlanReportAsync(Plan(buys: 0), State(), ordersPlaced: 0);

        var body = Assert.Single(handler.Bodies);
        // Default D8: the empty-sleeve report is proof the timer ran.
        Assert.Contains("No buys proposed", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Send_FlagOnPosture_StatesOrdersPlacedCount()
    {
        var (service, handler, _) = Build(orderPlacementEnabled: true);

        await service.SendReinvestmentPlanReportAsync(Plan(buys: 3), State(), ordersPlaced: 3);

        var body = Assert.Single(handler.Bodies);
        Assert.DoesNotContain("recommendation-only", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3", body);
        Assert.Contains("order", body, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- enabled/config guards ----------

    [Fact]
    public async Task Send_Disabled_MakesZeroPosts_AndSkipsAtDebug()
    {
        var (service, handler, logger) = Build(Config(enabled: false));

        await service.SendReinvestmentPlanReportAsync(Plan(), State(), ordersPlaced: 0);

        Assert.Equal(0, handler.InvocationCount);
        // One-time ctor Information notice + per-call Debug skip (B-006 pattern).
        Assert.Contains(CapturedLogs(logger), e => e.Level == LogLevel.Information && e.Message.Contains("disabled"));
        Assert.Contains(CapturedLogs(logger), e => e.Level == LogLevel.Debug);
    }

    [Fact]
    public async Task Send_MissingUrl_WarnsAndSkips()
    {
        var (service, handler, logger) = Build(Config(url: ""));

        await service.SendReinvestmentPlanReportAsync(Plan(), State(), ordersPlaced: 0);

        Assert.Equal(0, handler.InvocationCount);
        Assert.Contains(CapturedLogs(logger), e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Send_NonDiscordHost_RejectedWithRedactedLog()
    {
        var (service, handler, logger) = Build(Config(url: "https://evil.example/api/webhooks/456/" + SecretToken));

        await service.SendReinvestmentPlanReportAsync(Plan(), State(), ordersPlaced: 0);

        Assert.Equal(0, handler.InvocationCount);
        var logs = CapturedLogs(logger);
        Assert.Contains(logs, e => e.Level == LogLevel.Warning && e.Message.Contains("https://evil.example"));
        Assert.DoesNotContain(logs, e => e.Message.Contains(SecretToken));
    }

    // ---------- retry / failure degradation ----------

    [Fact]
    public async Task Send_RateLimited_RetriesWithinBudget_ThenSucceeds()
    {
        var handler = StubHandler.ReturnsResponses(
            () =>
            {
                var r = new HttpResponseMessage((HttpStatusCode)429);
                r.Headers.TryAddWithoutValidation("Retry-After", "0");
                return r;
            },
            () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var (service, _, logger) = Build(handler: handler);

        await service.SendReinvestmentPlanReportAsync(Plan(), State(), ordersPlaced: 0);

        Assert.Equal(2, handler.InvocationCount);
        Assert.Contains(CapturedLogs(logger),
            e => e.Level == LogLevel.Information && e.Message.Contains("Sent Discord income reinvest report"));
    }

    [Fact]
    public async Task Send_TerminalHttpFailure_NoThrow_LogsLoudAlertDropped_WithoutToken()
    {
        var handler = StubHandler.ReturnsStatuses(HttpStatusCode.InternalServerError);
        var (service, _, logger) = Build(handler: handler);

        // Must not throw — observability is never control.
        await service.SendReinvestmentPlanReportAsync(Plan(), State(), ordersPlaced: 0);

        Assert.Equal(1, handler.InvocationCount);
        var logs = CapturedLogs(logger);
        Assert.Contains(logs, e => e.Level == LogLevel.Error && e.Message.Contains("AlertDropped"));
        Assert.DoesNotContain(logs, e => e.Message.Contains(SecretToken));
    }

    [Fact]
    public async Task Send_TransportFailure_NoThrow_TypeNameOnlyInLogs()
    {
        var handler = StubHandler.ReturnsResponses(
            () => throw new HttpRequestException("connection refused to https://discord.com/api/webhooks/456/" + SecretToken));
        var (service, _, logger) = Build(handler: handler);

        await service.SendReinvestmentPlanReportAsync(Plan(), State(), ordersPlaced: 0);

        var logs = CapturedLogs(logger);
        Assert.Contains(logs, e => e.Level == LogLevel.Error && e.Message.Contains(nameof(HttpRequestException)));
        // The exception MESSAGE (which can echo the token-bearing URI) never reaches a log.
        Assert.DoesNotContain(logs, e => e.Message.Contains(SecretToken));
    }

    [Fact]
    public async Task Send_PayloadNeverContainsWebhookToken()
    {
        var (service, handler, logger) = Build();

        await service.SendReinvestmentPlanReportAsync(Plan(), State(), ordersPlaced: 0);

        Assert.DoesNotContain(handler.Bodies, b => b.Contains(SecretToken));
        Assert.DoesNotContain(CapturedLogs(logger), e => e.Message.Contains(SecretToken));
    }
}
