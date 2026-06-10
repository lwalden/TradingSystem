using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Moq;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Functions;
using Xunit;

namespace TradingSystem.Tests.Functions;

/// <summary>
/// S4-003: DiscordDailyReportService posts the Week-10 rich daily digest to the same Discord
/// webhook the risk alerts use, reusing the S3-004 hardening pattern verbatim: https-only host
/// allow-list, webhook-token redaction in logs, bounded 429/Retry-After retry with an injectable
/// zero-wait delay, and the Enabled==false skip (one-time ctor Information notice, per-call Debug
/// skip). ALL HTTP is mocked via a controllable HttpMessageHandler injected through a fake
/// IHttpClientFactory — no live Discord POST is ever made. Payload assertions parse the captured
/// POST body, locking the ADR-023 platform-vs-brokerage cost breakout and the core digest fields
/// (trade count, P&amp;L, open positions, regime).
/// </summary>
public class DiscordDailyReportServiceTests
{
    // Token substring that must never appear in any log argument.
    private const string SecretToken = "secrettoken";

    private static readonly DateTime ReportDate = new(2026, 6, 8);

    // Controllable handler: records each request (capturing the POST body at send time, since
    // JsonContent is not re-readable after disposal) and returns caller-supplied responses in
    // order (the last response repeats once the queue is drained). Optionally throws to simulate
    // a transport failure.
    private sealed class StubHandler : HttpMessageHandler
    {
        public int InvocationCount { get; private set; }
        public List<string> Bodies { get; } = new();
        private readonly Queue<Func<HttpResponseMessage>> _responders;
        private readonly Exception? _throw;
        private Func<HttpResponseMessage>? _last;

        private StubHandler(IEnumerable<Func<HttpResponseMessage>> responders, Exception? toThrow)
        {
            _responders = new Queue<Func<HttpResponseMessage>>(responders);
            _throw = toThrow;
        }

        public static StubHandler ReturnsStatuses(params HttpStatusCode[] codes)
            => new(codes.Select<HttpStatusCode, Func<HttpResponseMessage>>(c => () => new HttpResponseMessage(c)), null);

        public static StubHandler ReturnsResponses(params Func<HttpResponseMessage>[] factories)
            => new(factories, null);

        public static StubHandler Throws(Exception ex)
            => new(Array.Empty<Func<HttpResponseMessage>>(), ex);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            if (request.Content != null)
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            if (_throw != null)
                throw _throw;

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

    private static DiscordConfig Config(bool enabled = true, string? url = null) => new()
    {
        Enabled = enabled,
        WebhookUrl = url ?? "https://discord.com/api/webhooks/123/" + SecretToken,
        Username = "TradingSystem Risk"
    };

    private static DailySnapshot Snapshot() => new()
    {
        Date = ReportDate,
        NetLiquidationValue = 250_000m,
        DailyPnL = 1_234.56m,
        DailyPnLPercent = 0.0049m,
        RealizedPnL = 800.25m,
        UnrealizedPnL = 434.31m,
        TradesExecuted = 2,
        CommissionsPaid = 3.30m,
        OpenPositions = 5,
        MarketRegime = RegimeType.Cautious
    };

    private static Trade Fill(string symbol, decimal qty = 100m) => new()
    {
        Symbol = symbol,
        Action = OrderAction.Buy,
        Quantity = qty,
        EntryPrice = 50.00m,
        EntryTime = ReportDate.AddHours(10),
        Sleeve = SleeveType.Income
    };

    private sealed class Fixture
    {
        public StubHandler Handler { get; }
        public Mock<ILogger<DiscordDailyReportService>> Logger { get; } = new();
        public Mock<ISnapshotRepository> Snapshots { get; } = new();
        public Mock<ITradeRepository> Trades { get; } = new();
        public List<TimeSpan> Delays { get; } = new();
        public DiscordDailyReportService Service { get; }

        public Fixture(DiscordConfig config, StubHandler handler,
            DailySnapshot? snapshot, List<Trade>? trades,
            IReadOnlyList<SleeveReadinessScorecard>? scorecards = null,
            ISleeveReadinessScorecardService? scorecardServiceOverride = null,
            ReportingConfig? reporting = null,
            bool omitReportingConfig = false)
        {
            Handler = handler;
            Snapshots.Setup(s => s.GetSnapshotAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(snapshot);
            Trades.Setup(t => t.GetByDateRangeAsync(
                    It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(trades ?? new List<Trade>());

            ISleeveReadinessScorecardService? scorecardService = scorecardServiceOverride;
            if (scorecardService == null && scorecards != null)
            {
                var mock = new Mock<ISleeveReadinessScorecardService>();
                mock.Setup(m => m.GenerateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(scorecards);
                scorecardService = mock.Object;
            }

            Func<TimeSpan, CancellationToken, Task> delay = (ts, _) =>
            {
                Delays.Add(ts);
                return Task.CompletedTask;
            };

            // S5-002 cadence trap defusal: ReportDate (2026-06-08) is a MONDAY, so with the
            // production default (Friday) every pre-existing readiness assertion would silently
            // stop exercising the embed path. Unless a test opts out (omitReportingConfig) or
            // passes its own cadence, pin the scorecard day to ReportDate's weekday.
            var reportingOptions = omitReportingConfig
                ? null
                : Microsoft.Extensions.Options.Options.Create(
                    reporting ?? new ReportingConfig { WeeklyScorecardDay = ReportDate.DayOfWeek });

            Service = new DiscordDailyReportService(
                new FakeHttpClientFactory(handler),
                Microsoft.Extensions.Options.Options.Create(config),
                Snapshots.Object,
                Trades.Object,
                Logger.Object,
                delay,
                scorecardService,
                reportingOptions);
        }
    }

    // Every string-shaped argument across every logger invocation, including the message template
    // and structured args, so a token leak anywhere is caught.
    private static IEnumerable<string> AllLogArgStrings(Mock<ILogger<DiscordDailyReportService>> logger)
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

    private static int LogCount(Mock<ILogger<DiscordDailyReportService>> logger, LogLevel level) =>
        logger.Invocations.Count(i =>
            i.Method.Name == nameof(ILogger.Log) &&
            i.Arguments.Count > 0 &&
            i.Arguments[0] is LogLevel l && l == level);

    private static HttpResponseMessage TooManyRequests(string retryAfter)
    {
        var r = new HttpResponseMessage((HttpStatusCode)429);
        r.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        return r;
    }

    // ---- 1. Enabled==false → no HTTP POST, returns without throwing ----

    [Fact]
    public async Task Disabled_DoesNotPost_ReturnsWithoutThrowing()
    {
        var fx = new Fixture(Config(enabled: false), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            Snapshot(), new List<Trade> { Fill("MSFT") });

        await fx.Service.SendDailyReportAsync(ReportDate);

        Assert.Equal(0, fx.Handler.InvocationCount);
        // Disabled also means no repository reads — the skip happens before any data fetch.
        fx.Snapshots.Verify(s => s.GetSnapshotAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Disabled_CtorLogsInformationNoticeOnce_PerCallSkipIsDebug()
    {
        // Same pattern as DiscordRiskAlertService (B-006 / review S4-005): one-time ctor
        // Information notice (Program.cs min level is Information), per-call skip at Debug.
        var fx = new Fixture(Config(enabled: false), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            Snapshot(), new List<Trade>());

        Assert.Equal(1, LogCount(fx.Logger, LogLevel.Information));
        Assert.Contains(AllLogArgStrings(fx.Logger), s =>
            s.Contains("disabled", StringComparison.OrdinalIgnoreCase));

        await fx.Service.SendDailyReportAsync(ReportDate);
        await fx.Service.SendDailyReportAsync(ReportDate);

        Assert.Equal(1, LogCount(fx.Logger, LogLevel.Information));
        Assert.Equal(2, LogCount(fx.Logger, LogLevel.Debug));
    }

    [Fact]
    public void Enabled_CtorEmitsNoDisabledNotice()
    {
        var fx = new Fixture(Config(enabled: true), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            Snapshot(), new List<Trade>());

        Assert.Equal(0, LogCount(fx.Logger, LogLevel.Information));
    }

    // ---- 2. Embed payload carries trade count, P&L, open positions, regime ----

    [Fact]
    public async Task Payload_ContainsTradeCount_PnL_OpenPositions_Regime()
    {
        var fx = new Fixture(Config(), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            Snapshot(), new List<Trade> { Fill("MSFT"), Fill("JNJ") });

        await fx.Service.SendDailyReportAsync(ReportDate);

        var body = Assert.Single(fx.Handler.Bodies);
        Assert.Contains("embeds", body);
        // Day's executed-trade count (2 fills returned by the repo).
        Assert.Contains("2", body);
        Assert.Contains("MSFT", body);
        // Realized / unrealized P&L from the snapshot.
        Assert.Contains("800.25", body);
        Assert.Contains("434.31", body);
        // Open position count and regime.
        Assert.Contains("5", body);
        Assert.Contains("Cautious", body);
    }

    [Fact]
    public async Task Payload_FieldOrder_LeadsWithDailyPnLAndRegime_NetLiquidationLast()
    {
        var fx = new Fixture(Config(), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            Snapshot(), new List<Trade> { Fill("MSFT"), Fill("JNJ") });

        await fx.Service.SendDailyReportAsync(ReportDate);

        var body = Assert.Single(fx.Handler.Bodies);
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var summary = doc.RootElement.GetProperty("embeds")[0];
        var fieldNames = summary.GetProperty("fields").EnumerateArray()
            .Select(f => f.GetProperty("name").GetString())
            .ToList();

        // Mobile-first order (review S4-003): the day's news leads, net liquidation is context
        // and renders LAST — even on trade days when a Fills field is present.
        Assert.Equal("Daily P&L", fieldNames[0]);
        Assert.Equal("Market Regime", fieldNames[1]);
        Assert.Equal("Net Liquidation", fieldNames[^1]);
        Assert.Contains("Fills", fieldNames);
    }

    [Fact]
    public async Task Payload_Description_CarriesOutcomeLine_DirectionMagnitudeAndTradeCount()
    {
        var fx = new Fixture(Config(), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            Snapshot(), new List<Trade> { Fill("MSFT"), Fill("JNJ") });

        await fx.Service.SendDailyReportAsync(ReportDate);

        var body = Assert.Single(fx.Handler.Bodies);
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var description = doc.RootElement.GetProperty("embeds")[0].GetProperty("description").GetString();

        // First-read mobile text: direction, magnitude, percent, activity.
        Assert.NotNull(description);
        Assert.StartsWith("Up $1,234.56", description);
        Assert.Contains("0.49", description);
        Assert.Contains("2 trade(s)", description);
    }

    [Fact]
    public async Task Payload_DisablesMentionParsing()
    {
        // Same anti-ping hardening as the risk alerts: content can never @everyone/@here.
        var fx = new Fixture(Config(), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            Snapshot(), new List<Trade> { Fill("MSFT") });

        await fx.Service.SendDailyReportAsync(ReportDate);

        var body = Assert.Single(fx.Handler.Bodies);
        Assert.Contains("allowed_mentions", body);
    }

    // ---- 3. ADR-023 cost breakout: platform and brokerage are SEPARATE fields ----

    [Fact]
    public async Task Payload_BreaksOutPlatformAndBrokerageCosts_AsDistinctFields()
    {
        var fx = new Fixture(Config(), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            Snapshot(), new List<Trade> { Fill("MSFT") });

        await fx.Service.SendDailyReportAsync(ReportDate);

        var body = Assert.Single(fx.Handler.Bodies);
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var fieldNames = doc.RootElement.GetProperty("embeds").EnumerateArray()
            .Where(e => e.TryGetProperty("fields", out _))
            .SelectMany(e => e.GetProperty("fields").EnumerateArray())
            .Select(f => f.GetProperty("name").GetString() ?? string.Empty)
            .ToList();

        // ADR-023: platform (Azure+Polygon+Claude) and brokerage (commissions/forecast) must be
        // DISTINCT fields — a single merged "Costs" field fails this test.
        var platformField = fieldNames.SingleOrDefault(n => n.Contains("Platform", StringComparison.OrdinalIgnoreCase));
        var brokerageField = fieldNames.SingleOrDefault(n => n.Contains("Brokerage", StringComparison.OrdinalIgnoreCase));
        Assert.False(string.IsNullOrEmpty(platformField), "payload must carry a dedicated platform-cost field");
        Assert.False(string.IsNullOrEmpty(brokerageField), "payload must carry a dedicated brokerage-cost field");
        Assert.NotEqual(platformField, brokerageField);

        // Brokerage field carries the day's commissions (DailySnapshot.CommissionsPaid).
        Assert.Contains("3.30", body);
    }

    // ---- 4. Token redaction on a forced failure ----

    [Fact]
    public async Task TransportFailure_NoLogArgContainsTokenPathSegment_NoThrow()
    {
        var fx = new Fixture(Config(), StubHandler.Throws(new HttpRequestException("connection reset")),
            Snapshot(), new List<Trade> { Fill("MSFT") });

        // Degrades to logs — no throw escapes (a missed report is not a risk event).
        await fx.Service.SendDailyReportAsync(ReportDate);

        Assert.True(LogCount(fx.Logger, LogLevel.Error) >= 1, "transport failure should log an error");
        var logs = AllLogArgStrings(fx.Logger).ToList();
        Assert.DoesNotContain(logs, s => s.Contains(SecretToken));
        Assert.DoesNotContain(logs, s => s.Contains("/api/webhooks/123"));
    }

    [Fact]
    public async Task ExhaustedRetries_NoLogArgContainsToken()
    {
        var always429 = StubHandler.ReturnsResponses(() => TooManyRequests("0"));
        var fx = new Fixture(Config(), always429, Snapshot(), new List<Trade>());

        await fx.Service.SendDailyReportAsync(ReportDate);

        // Initial attempt + bounded retries — never unbounded.
        Assert.True(fx.Handler.InvocationCount is >= 2 and <= 4,
            $"expected 2..4 POSTs, got {fx.Handler.InvocationCount}");
        var logs = AllLogArgStrings(fx.Logger).ToList();
        Assert.True(LogCount(fx.Logger, LogLevel.Error) >= 1, "exhaustion should log an error");
        Assert.DoesNotContain(logs, s => s.Contains(SecretToken));
        Assert.DoesNotContain(logs, s => s.Contains("/api/webhooks/123"));
    }

    // ---- 5. 429 then 204 → exactly one retry honoring Retry-After, success logged ----

    [Fact]
    public async Task RateLimited429Then204_RetriesExactlyOnce_HonorsRetryAfter_LogsSuccess()
    {
        var handler = StubHandler.ReturnsResponses(
            () => TooManyRequests("0"),
            () => new HttpResponseMessage(HttpStatusCode.NoContent));
        var fx = new Fixture(Config(), handler, Snapshot(), new List<Trade> { Fill("MSFT") });

        await fx.Service.SendDailyReportAsync(ReportDate);

        Assert.Equal(2, fx.Handler.InvocationCount);
        var delay = Assert.Single(fx.Delays);
        Assert.Equal(TimeSpan.Zero, delay);
        Assert.True(LogCount(fx.Logger, LogLevel.Information) >= 1, "success should be logged");
        Assert.Equal(0, LogCount(fx.Logger, LogLevel.Error));
    }

    // ---- 6. Malformed / non-Discord webhook host → redacted warning, no POST ----

    [Theory]
    [InlineData("https://evil.example.com/api/webhooks/1/" + SecretToken, "evil.example.com")]
    [InlineData("http://discord.com/api/webhooks/123/" + SecretToken, "discord.com")]
    public async Task DisallowedOrNonHttpsHost_SkipsWithRedactedWarning_NoPost(string url, string redactedHost)
    {
        var fx = new Fixture(Config(url: url), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            Snapshot(), new List<Trade>());

        await fx.Service.SendDailyReportAsync(ReportDate);

        Assert.Equal(0, fx.Handler.InvocationCount);
        Assert.True(LogCount(fx.Logger, LogLevel.Warning) >= 1, "rejected webhook should warn");
        var logs = AllLogArgStrings(fx.Logger).ToList();
        Assert.Contains(logs, s => s.Contains(redactedHost));
        Assert.DoesNotContain(logs, s => s.Contains(SecretToken));
        Assert.DoesNotContain(logs, s => s.Contains("/api/webhooks"));
    }

    [Fact]
    public async Task MalformedWebhookUrl_SkipsWithWarning_NoPost()
    {
        var fx = new Fixture(Config(url: "not a url at all"), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            Snapshot(), new List<Trade>());

        await fx.Service.SendDailyReportAsync(ReportDate);

        Assert.Equal(0, fx.Handler.InvocationCount);
        Assert.True(LogCount(fx.Logger, LogLevel.Warning) >= 1);
    }

    [Fact]
    public async Task EmptyWebhookUrl_SkipsWithWarning_NoPost()
    {
        var fx = new Fixture(Config(url: ""), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            Snapshot(), new List<Trade>());

        await fx.Service.SendDailyReportAsync(ReportDate);

        Assert.Equal(0, fx.Handler.InvocationCount);
        Assert.True(LogCount(fx.Logger, LogLevel.Warning) >= 1);
    }

    // ---- 7. Zero-trade day still produces a valid "no trades today" embed ----

    [Fact]
    public async Task ZeroTrades_ProducesValidNoTradesEmbed_NoThrow()
    {
        var snapshot = Snapshot();
        snapshot.TradesExecuted = 0;
        snapshot.CommissionsPaid = 0m;
        var fx = new Fixture(Config(), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            snapshot, new List<Trade>());

        await fx.Service.SendDailyReportAsync(ReportDate);

        Assert.Equal(1, fx.Handler.InvocationCount);
        var body = Assert.Single(fx.Handler.Bodies);
        // Still a structurally valid embed payload; the zero-trade description carries the
        // outcome line plus the regime ("... — no trades — regime: Cautious.").
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("embeds").GetArrayLength() >= 1);
        var description = doc.RootElement.GetProperty("embeds")[0].GetProperty("description").GetString();
        Assert.NotNull(description);
        Assert.Contains("no trades", description);
        Assert.Contains("regime: Cautious", description);
        Assert.Equal(0, LogCount(fx.Logger, LogLevel.Error));
    }

    // ---- Missing snapshot: nothing to report — skip gracefully, no POST, no throw ----

    [Fact]
    public async Task MissingSnapshot_SkipsWithWarning_NoPost_NoThrow()
    {
        var fx = new Fixture(Config(), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            snapshot: null, new List<Trade>());

        await fx.Service.SendDailyReportAsync(ReportDate);

        Assert.Equal(0, fx.Handler.InvocationCount);
        Assert.True(LogCount(fx.Logger, LogLevel.Warning) >= 1, "missing snapshot should warn");
    }

    // ---- Optional readiness section (S4-002 scorecards) ----

    [Fact]
    public async Task ReadinessSection_RendersUndefinedProfitFactorAsInfinity_NeverSentinel()
    {
        var scorecards = new List<SleeveReadinessScorecard>
        {
            new()
            {
                SleeveName = "Income",
                Sleeve = SleeveType.Income,
                Readiness = SleeveReadinessState.Ready,
                IsProfitFactorUndefined = true,
                Confidence = 0.75m,
                ClosedTradeCount = 12,
                Evaluation = new ThresholdResult
                {
                    ActualMetrics = new SleeveMetrics { ProfitFactor = 999m }
                }
            }
        };
        var fx = new Fixture(Config(), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            Snapshot(), new List<Trade> { Fill("MSFT") }, scorecards);

        await fx.Service.SendDailyReportAsync(ReportDate);

        var body = Assert.Single(fx.Handler.Bodies);
        // The no-loss sentinel must NEVER leak into the rendered report (S4-002 contract).
        Assert.DoesNotContain("999", body);
        // Assert on the DECODED payload (what Discord renders) — the serializer escapes "∞"
        // as ∞ in the raw body.
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var fieldTexts = doc.RootElement.GetProperty("embeds").EnumerateArray()
            .Where(e => e.TryGetProperty("fields", out _))
            .SelectMany(e => e.GetProperty("fields").EnumerateArray())
            .Select(f => $"{f.GetProperty("name").GetString()}: {f.GetProperty("value").GetString()}")
            .ToList();
        Assert.Contains(fieldTexts, t => t.Contains("∞ (no losses)"));
        Assert.DoesNotContain(fieldTexts, t => t.Contains("999"));
        // Confidence appears inline per sleeve.
        Assert.Contains(fieldTexts, t => t.Contains("Income") && t.Contains("75"));
        // MeetsMinimumCapital defaults false on the test scorecard → human copy, not jargon.
        Assert.Contains(fieldTexts, t => t.Contains("below capital minimum"));
        Assert.DoesNotContain(fieldTexts, t => t.Contains("capital-gated"));

        // Readiness embed: per-sleeve status line up front, recommendation-only disclaimer
        // in the footer (review S4-003 mobile-first copy).
        var readinessEmbed = doc.RootElement.GetProperty("embeds").EnumerateArray()
            .Single(e => (e.GetProperty("title").GetString() ?? string.Empty).Contains("Readiness"));
        Assert.Equal("Income: Ready", readinessEmbed.GetProperty("description").GetString());
        Assert.Contains("Recommendation-only",
            readinessEmbed.GetProperty("footer").GetProperty("text").GetString());
    }

    [Fact]
    public async Task ReadinessSection_OmittedWhenScorecardServiceAbsent_CoreReportStillSent()
    {
        var fx = new Fixture(Config(), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            Snapshot(), new List<Trade> { Fill("MSFT") }, scorecards: null);

        await fx.Service.SendDailyReportAsync(ReportDate);

        Assert.Equal(1, fx.Handler.InvocationCount);
        var body = Assert.Single(fx.Handler.Bodies);
        Assert.DoesNotContain("Readiness", body);
        Assert.Equal(0, LogCount(fx.Logger, LogLevel.Error));
    }

    [Fact]
    public async Task ReadinessSection_ScorecardFailure_OmitsSectionWithWarning_CoreReportStillSent()
    {
        // Documented contract: the readiness section is best-effort — a scorecard failure must
        // NEVER drop the core daily report. It degrades to a warning and the section is omitted.
        var throwing = new Mock<ISleeveReadinessScorecardService>();
        throwing.Setup(m => m.GenerateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("scorecard store unavailable"));
        var fx = new Fixture(Config(), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            Snapshot(), new List<Trade> { Fill("MSFT") }, scorecardServiceOverride: throwing.Object);

        await fx.Service.SendDailyReportAsync(ReportDate);

        // Core report still POSTed exactly once, without the readiness embed.
        Assert.Equal(1, fx.Handler.InvocationCount);
        var body = Assert.Single(fx.Handler.Bodies);
        Assert.DoesNotContain("Readiness", body);
        // Degrades to a warning (section omitted), never an error/throw.
        Assert.True(LogCount(fx.Logger, LogLevel.Warning) >= 1, "scorecard failure should warn");
        Assert.Equal(0, LogCount(fx.Logger, LogLevel.Error));
    }

    [Fact]
    public async Task ReadinessSection_EmptyScorecards_OmitsSection_CoreReportStillSent()
    {
        var fx = new Fixture(Config(), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            Snapshot(), new List<Trade> { Fill("MSFT") },
            scorecards: new List<SleeveReadinessScorecard>());

        await fx.Service.SendDailyReportAsync(ReportDate);

        Assert.Equal(1, fx.Handler.InvocationCount);
        var body = Assert.Single(fx.Handler.Bodies);
        Assert.DoesNotContain("Readiness", body);
        Assert.Equal(0, LogCount(fx.Logger, LogLevel.Error));
    }

    // ---- S5-002: weekly readiness cadence (Default D7, locked decision 5) ----

    private static List<SleeveReadinessScorecard> ReadyIncomeScorecard() => new()
    {
        new()
        {
            SleeveName = "Income",
            Sleeve = SleeveType.Income,
            Readiness = SleeveReadinessState.Ready,
            Confidence = 0.75m,
            ClosedTradeCount = 12,
            MeetsMinimumCapital = true,
            Evaluation = new ThresholdResult
            {
                ActualMetrics = new SleeveMetrics { ProfitFactor = 2.5m }
            }
        }
    };

    [Fact]
    public async Task WeeklyCadence_OnScorecardDay_AppendsReadinessEmbed()
    {
        // ReportDate is a Monday; the cadence explicitly selects Monday → readiness appended.
        var fx = new Fixture(Config(), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            Snapshot(), new List<Trade> { Fill("MSFT") }, ReadyIncomeScorecard(),
            reporting: new ReportingConfig { WeeklyScorecardDay = ReportDate.DayOfWeek });

        await fx.Service.SendDailyReportAsync(ReportDate);

        var body = Assert.Single(fx.Handler.Bodies);
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        Assert.Equal(2, doc.RootElement.GetProperty("embeds").GetArrayLength());
        Assert.Contains("Readiness", body);
        Assert.Contains("Income: Ready", body);
    }

    [Fact]
    public async Task WeeklyCadence_OffDay_OmitsReadinessEmbed_CoreReportUnchanged()
    {
        // Cadence = Friday, ReportDate = Monday: the scorecard service must not even be
        // consulted, and the core digest must be untouched (all S4-003 fields present).
        var scorecardService = new Mock<ISleeveReadinessScorecardService>(MockBehavior.Strict);
        var fx = new Fixture(Config(), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            Snapshot(), new List<Trade> { Fill("MSFT"), Fill("JNJ") },
            scorecardServiceOverride: scorecardService.Object,
            reporting: new ReportingConfig { WeeklyScorecardDay = DayOfWeek.Friday });

        await fx.Service.SendDailyReportAsync(ReportDate);

        var body = Assert.Single(fx.Handler.Bodies);
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        // Single embed: the core daily digest only.
        Assert.Equal(1, doc.RootElement.GetProperty("embeds").GetArrayLength());
        Assert.DoesNotContain("Readiness", body);
        scorecardService.VerifyNoOtherCalls();

        // Core report fields all present on the off day (trades, P&L, positions, regime,
        // ADR-023 cost breakout).
        var fieldNames = doc.RootElement.GetProperty("embeds")[0].GetProperty("fields")
            .EnumerateArray()
            .Select(f => f.GetProperty("name").GetString())
            .ToList();
        Assert.Contains("Daily P&L", fieldNames);
        Assert.Contains("Market Regime", fieldNames);
        Assert.Contains("Trades Executed", fieldNames);
        Assert.Contains("Open Positions", fieldNames);
        Assert.Contains("Platform Costs", fieldNames);
        Assert.Contains("Brokerage Costs", fieldNames);
        Assert.Contains("800.25", body);
        Assert.Contains("Cautious", body);
        Assert.Equal(0, LogCount(fx.Logger, LogLevel.Error));
    }

    [Fact]
    public async Task WeeklyCadence_UnconfiguredReportingSection_DefaultsToFriday()
    {
        // Locked decision 5: no Reporting section bound → Friday is the scorecard day.
        Assert.Equal(DayOfWeek.Friday, new ReportingConfig().WeeklyScorecardDay);

        var friday = new DateTime(2026, 6, 12);
        Assert.Equal(DayOfWeek.Friday, friday.DayOfWeek);

        var fx = new Fixture(Config(), StubHandler.ReturnsStatuses(HttpStatusCode.NoContent),
            Snapshot(), new List<Trade> { Fill("MSFT") }, ReadyIncomeScorecard(),
            omitReportingConfig: true);

        // Monday (ReportDate): no readiness embed under the default cadence.
        await fx.Service.SendDailyReportAsync(ReportDate);
        // Friday: readiness embed appended under the default cadence.
        await fx.Service.SendDailyReportAsync(friday);

        Assert.Equal(2, fx.Handler.Bodies.Count);
        Assert.DoesNotContain("Readiness", fx.Handler.Bodies[0]);
        Assert.Contains("Readiness", fx.Handler.Bodies[1]);
    }
}
