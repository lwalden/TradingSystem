using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Functions;
using Xunit;

namespace TradingSystem.Tests.Functions;

/// <summary>
/// S3-004: DiscordRiskAlertService posts risk alerts to the existing active bots-repo webhook
/// channel using the raw-webhook pattern. Verifies host allow-list (https + discord.com /
/// discordapp.com / subdomain), webhook-token redaction in logs, and 429/Retry-After bounded
/// retry. ALL HTTP is mocked via a controllable HttpMessageHandler injected through a fake
/// IHttpClientFactory for the "DiscordRiskAlerts" named client. No live Discord POST is ever made.
/// The injected delay runs zero real wait, so retry/budget arithmetic is exercised instantly.
/// </summary>
public class DiscordRiskAlertServiceTests
{
    private const string ClientName = "DiscordRiskAlerts";

    // Token substring that must never appear in any log argument.
    private const string SecretToken = "secrettoken";

    // Controllable handler: records each request and returns caller-supplied responses in order
    // (the last response repeats once the queue is drained). Optionally throws to simulate a
    // transport failure. Captures POST bodies so a test can confirm a payload was sent.
    private sealed class StubHandler : HttpMessageHandler
    {
        public int InvocationCount { get; private set; }
        public List<HttpRequestMessage> Requests { get; } = new();
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders;
        private readonly Func<HttpRequestMessage, HttpResponseMessage>? _default;
        private readonly Exception? _throw;

        private StubHandler(
            IEnumerable<Func<HttpRequestMessage, HttpResponseMessage>> responders,
            Exception? toThrow)
        {
            _responders = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>(responders);
            _default = _responders.Count > 0 ? null : (_ => new HttpResponseMessage(HttpStatusCode.NoContent));
            _throw = toThrow;
        }

        public static StubHandler ReturnsStatuses(params HttpResponseMessage[] responses)
            => new(responses.Select<HttpResponseMessage, Func<HttpRequestMessage, HttpResponseMessage>>(r => _ => r), null);

        public static StubHandler ReturnsForever(Func<HttpResponseMessage> factory)
            => new(new Func<HttpRequestMessage, HttpResponseMessage>[] { _ => factory() }, null) { _repeatLast = true };

        public static StubHandler Throws(Exception ex)
            => new(Array.Empty<Func<HttpRequestMessage, HttpResponseMessage>>(), ex);

        private bool _repeatLast;
        private Func<HttpRequestMessage, HttpResponseMessage>? _last;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            Requests.Add(request);
            if (_throw != null)
                throw _throw;

            Func<HttpRequestMessage, HttpResponseMessage> responder;
            if (_responders.Count > 0)
            {
                responder = _responders.Dequeue();
                _last = responder;
            }
            else
            {
                responder = _repeatLast && _last != null ? _last : (_default ?? (_ => new HttpResponseMessage(HttpStatusCode.NoContent)));
            }
            return Task.FromResult(responder(request));
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

    private static RiskMetrics Metrics() => new()
    {
        DailyPnLPercent = -0.025m,
        WeeklyPnLPercent = -0.041m,
        CurrentDrawdown = -0.06m,
        OpenPositionCount = 3
    };

    // Builds a service whose HTTP leg is served by the supplied handler and whose retry delay is
    // captured (no real wait). Returns the recorded delay durations so the retry/clamp math can be
    // asserted. The CancellationToken plumbing is preserved.
    private static (DiscordRiskAlertService Service, StubHandler Handler,
                    Mock<ILogger<DiscordRiskAlertService>> Logger, List<TimeSpan> Delays)
        Build(DiscordConfig config, StubHandler handler)
    {
        var logger = new Mock<ILogger<DiscordRiskAlertService>>();
        var factory = new FakeHttpClientFactory(handler);
        var delays = new List<TimeSpan>();
        Func<TimeSpan, CancellationToken, Task> delay = (ts, _) =>
        {
            delays.Add(ts);
            return Task.CompletedTask;
        };
        var service = new DiscordRiskAlertService(
            factory, Microsoft.Extensions.Options.Options.Create(config), logger.Object, delay);
        return (service, handler, logger, delays);
    }

    // Every string-shaped argument across every logger invocation, including the message template
    // and structured args, so a token leak anywhere is caught.
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

    private static HttpResponseMessage Status(HttpStatusCode code) => new(code);

    private static HttpResponseMessage TooManyRequests(
        string? retryAfter = null, string? resetAfter = null)
    {
        var r = new HttpResponseMessage((HttpStatusCode)429);
        if (retryAfter != null) r.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        if (resetAfter != null) r.Headers.TryAddWithoutValidation("X-RateLimit-Reset-After", resetAfter);
        return r;
    }

    [Fact]
    public async Task HappyPath_Posts2xx_NoRetry()
    {
        var handler = StubHandler.ReturnsStatuses(Status(HttpStatusCode.NoContent));
        var (service, h, logger, delays) = Build(Config(), handler);

        await service.SendDailyStopTriggeredAsync(Metrics());

        Assert.Equal(1, h.InvocationCount);
        Assert.Empty(delays);
        Assert.Equal(0, LogCount(logger, LogLevel.Warning));
        Assert.Equal(0, LogCount(logger, LogLevel.Error));
    }

    [Fact]
    public async Task DisabledConfig_DoesNotPost()
    {
        var handler = StubHandler.ReturnsStatuses(Status(HttpStatusCode.NoContent));
        var (service, h, _, _) = Build(Config(enabled: false), handler);

        await service.SendDailyStopTriggeredAsync(Metrics());

        Assert.Equal(0, h.InvocationCount);
    }

    [Fact]
    public async Task EmptyWebhookUrl_DoesNotPost_LogsWarning_NoUrlInLog()
    {
        var handler = StubHandler.ReturnsStatuses(Status(HttpStatusCode.NoContent));
        var (service, h, logger, _) = Build(Config(url: ""), handler);

        await service.SendWeeklyStopTriggeredAsync(Metrics());

        Assert.Equal(0, h.InvocationCount);
        Assert.True(LogCount(logger, LogLevel.Warning) >= 1, "empty webhook URL should warn");
        // No URL/token leakage even in the empty case.
        Assert.DoesNotContain(AllLogArgStrings(logger), s => s.Contains("http", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NonHttpsHost_RejectsAndRedacts()
    {
        var handler = StubHandler.ReturnsStatuses(Status(HttpStatusCode.NoContent));
        var url = "http://discord.com/api/webhooks/123/" + SecretToken;
        var (service, h, logger, _) = Build(Config(url: url), handler);

        await service.SendDailyStopTriggeredAsync(Metrics());

        Assert.Equal(0, h.InvocationCount);
        var logs = AllLogArgStrings(logger).ToList();
        Assert.Contains(logs, s => s.Contains("http://discord.com"));
        Assert.DoesNotContain(logs, s => s.Contains(SecretToken));
        Assert.DoesNotContain(logs, s => s.Contains("/api/webhooks/123"));
    }

    [Fact]
    public async Task DisallowedHost_Rejects()
    {
        var handler = StubHandler.ReturnsStatuses(Status(HttpStatusCode.NoContent));
        var url = "https://evil.example.com/api/webhooks/1/tok";
        var (service, h, logger, _) = Build(Config(url: url), handler);

        await service.SendDrawdownHaltTriggeredAsync(Metrics());

        Assert.Equal(0, h.InvocationCount);
        var logs = AllLogArgStrings(logger).ToList();
        Assert.Contains(logs, s => s.Contains("https://evil.example.com"));
        Assert.DoesNotContain(logs, s => s.Contains("/api/webhooks/1/tok"));
        Assert.DoesNotContain(logs, s => s.Contains("tok") && s.Contains("webhooks"));
    }

    [Theory]
    [InlineData("https://ptb.discord.com/api/webhooks/999/" + SecretToken)]
    [InlineData("https://discordapp.com/api/webhooks/999/" + SecretToken)]
    [InlineData("https://discord.com/api/webhooks/999/" + SecretToken)]
    public async Task AllowsDiscordappAndSubdomain(string url)
    {
        var handler = StubHandler.ReturnsStatuses(Status(HttpStatusCode.NoContent));
        var (service, h, logger, _) = Build(Config(url: url), handler);

        await service.SendDailyStopTriggeredAsync(Metrics());

        Assert.Equal(1, h.InvocationCount);
        Assert.Equal(0, LogCount(logger, LogLevel.Error));
        Assert.DoesNotContain(AllLogArgStrings(logger), s => s.Contains(SecretToken));
    }

    [Fact]
    public async Task RetryAfter_429ThenSuccess_RetriesAfterRetryAfter()
    {
        var handler = StubHandler.ReturnsStatuses(
            TooManyRequests(retryAfter: "0"),
            Status(HttpStatusCode.NoContent));
        var (service, h, _, delays) = Build(Config(), handler);

        await service.SendDailyStopTriggeredAsync(Metrics());

        Assert.Equal(2, h.InvocationCount);
        Assert.Single(delays);
        Assert.Equal(TimeSpan.Zero, delays[0]);
    }

    [Fact]
    public async Task Retry_UsesXRateLimitResetAfter_WhenNoRetryAfter()
    {
        var handler = StubHandler.ReturnsStatuses(
            TooManyRequests(resetAfter: "0.5"),
            Status(HttpStatusCode.NoContent));
        var (service, h, _, delays) = Build(Config(), handler);

        await service.SendDailyStopTriggeredAsync(Metrics());

        Assert.Equal(2, h.InvocationCount);
        Assert.Single(delays);
        Assert.Equal(TimeSpan.FromSeconds(0.5), delays[0]);
    }

    [Fact]
    public async Task Retry_ExhaustsBudget_LogsScrubbedFailure_NoThrow()
    {
        // Always 429 with Retry-After 0 so the loop is bounded by maxRetries, not wall time.
        var handler = StubHandler.ReturnsForever(() => TooManyRequests(retryAfter: "0"));
        var (service, h, logger, _) = Build(Config(), handler);

        // Must not throw — degrade to logs per ADR-025.
        await service.SendDailyStopTriggeredAsync(Metrics());

        // Initial attempt + up to maxRetries(3) retries = at most 4 POSTs.
        Assert.True(h.InvocationCount <= 4, $"expected <= 4 POSTs, got {h.InvocationCount}");
        Assert.True(h.InvocationCount >= 2, "should have retried at least once");
        Assert.True(LogCount(logger, LogLevel.Error) >= 1, "exhaustion should log an error");
        var logs = AllLogArgStrings(logger).ToList();
        Assert.DoesNotContain(logs, s => s.Contains(SecretToken));
        Assert.DoesNotContain(logs, s => s.Contains("/api/webhooks/123"));
    }

    [Fact]
    public async Task Retry_ClampsHugeRetryAfter()
    {
        var handler = StubHandler.ReturnsStatuses(
            TooManyRequests(retryAfter: "99999"),
            Status(HttpStatusCode.NoContent));
        var (service, _, _, delays) = Build(Config(), handler);

        await service.SendDailyStopTriggeredAsync(Metrics());

        Assert.NotEmpty(delays);
        Assert.True(delays[0] <= TimeSpan.FromSeconds(60), $"single wait must clamp to <=60s, got {delays[0]}");
    }

    [Fact]
    public async Task HttpRequestException_LogsScrubbed_NoTokenLeak()
    {
        var handler = StubHandler.Throws(new HttpRequestException("connection reset"));
        var (service, _, logger, _) = Build(Config(), handler);

        // Degrades to logs — no throw escapes.
        await service.SendDailyStopTriggeredAsync(Metrics());

        Assert.True(LogCount(logger, LogLevel.Error) >= 1, "transport failure should log an error");
        var logs = AllLogArgStrings(logger).ToList();
        Assert.DoesNotContain(logs, s => s.Contains(SecretToken));
        Assert.DoesNotContain(logs, s => s.Contains("/api/webhooks/123"));
    }

    [Fact]
    public async Task NoLog_EverContainsWebhookToken()
    {
        // Representative send that exercises a 429 retry then success, so multiple log paths run.
        var handler = StubHandler.ReturnsStatuses(
            TooManyRequests(retryAfter: "0"),
            Status(HttpStatusCode.NoContent));
        var (service, _, logger, _) = Build(Config(), handler);

        await service.SendDailyStopTriggeredAsync(Metrics());
        await service.SendWeeklyStopTriggeredAsync(Metrics());

        Assert.DoesNotContain(AllLogArgStrings(logger), s => s.Contains(SecretToken));
    }
}
