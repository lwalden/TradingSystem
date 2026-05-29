using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TradingSystem.AI.Services;
using TradingSystem.Core.Interfaces;
using Xunit;

namespace TradingSystem.Tests.AI;

/// <summary>
/// S2-002: fail-closed cost controls on the metered direct-API fallback.
/// All tests leave GatewayApiKey EMPTY so the gateway path is skipped without any HTTP,
/// driving the metered direct-API branch in isolation (independent of S2-004 gateway work).
/// </summary>
public class ClaudeServiceTests
{
    // Stub handler: counts how many times the direct (metered) Anthropic API is actually hit,
    // and returns a canned, valid Anthropic content payload so AnalyzeAsync succeeds.
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int InvocationCount { get; private set; }
        private readonly string _responseText;

        public CountingHandler(string responseText = "{\"regime\":\"riskon\"}")
        {
            _responseText = responseText;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            var json = "{\"content\":[{\"type\":\"text\",\"text\":" +
                       System.Text.Json.JsonSerializer.Serialize(_responseText) + "}]}";
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private static ClaudeConfig EmptyGatewayConfig(int maxDirect = 50) => new()
    {
        ApiKey = "test-key",
        GatewayApiKey = string.Empty, // forces the metered direct path
        Model = "claude-sonnet-4-20250514",
        MaxDirectApiCallsPerDay = maxDirect
    };

    private static AIAnalysisRequest SampleRequest() => new()
    {
        StrategyId = "test",
        SystemPrompt = "sys",
        UserPrompt = "user"
    };

    private static (ClaudeService Service, CountingHandler Handler, Mock<ILogger<ClaudeService>> Logger)
        BuildService(ClaudeConfig config, string responseText = "{\"regime\":\"riskon\"}")
    {
        var handler = new CountingHandler(responseText);
        var httpClient = new HttpClient(handler);
        var logger = new Mock<ILogger<ClaudeService>>();
        var service = new ClaudeService(logger.Object, Microsoft.Extensions.Options.Options.Create(config), httpClient);
        return (service, handler, logger);
    }

    private static int GetDirectCallsToday(ClaudeService service)
    {
        var field = typeof(ClaudeService).GetField("_directCallsToday",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return (int)field!.GetValue(service)!;
    }

    private static bool LoggedAtLeastOnce(Mock<ILogger<ClaudeService>> logger, LogLevel level) =>
        logger.Invocations.Any(i =>
            i.Method.Name == nameof(ILogger.Log) &&
            i.Arguments.Count > 0 &&
            i.Arguments[0] is LogLevel l && l == level);

    [Fact]
    public async Task Fallback_IncrementsCounterOnlyOnDirectCall()
    {
        var (service, _, _) = BuildService(EmptyGatewayConfig());

        await service.AnalyzeAsync(SampleRequest());

        Assert.Equal(1, GetDirectCallsToday(service));
    }

    [Fact]
    public async Task Fallback_EmitsWarningLog()
    {
        var (service, _, logger) = BuildService(EmptyGatewayConfig());

        await service.AnalyzeAsync(SampleRequest());

        Assert.True(LoggedAtLeastOnce(logger, LogLevel.Warning),
            "fallback to metered direct API should emit a Warning");
    }

    [Fact]
    public async Task CapExceeded_DoesNotCallAnthropic_AndFailsClosed()
    {
        var (service, handler, _) = BuildService(EmptyGatewayConfig(maxDirect: 1));

        // First call is allowed and hits the metered API.
        await service.AnalyzeAsync(SampleRequest());
        Assert.Equal(1, handler.InvocationCount);

        // Second call must be refused (cap == 1): no new HTTP hit, and the no-content
        // result fails closed for the generic deserialize path → caller falls back to rules.
        var emptyResult = await service.AnalyzeAsync(SampleRequest());
        Assert.Equal(1, handler.InvocationCount); // handler NOT invoked a second time
        Assert.True(string.IsNullOrEmpty(emptyResult), "capped call should return no content");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AnalyzeAsync<ClaudeRegimeStub>(SampleRequest()));
        Assert.Equal(1, handler.InvocationCount); // still no metered call
    }

    [Fact]
    public async Task Counter_ResetsOnNewDay()
    {
        var (service, _, _) = BuildService(EmptyGatewayConfig());

        // Force the counter date to yesterday and seed a non-zero count.
        var dateField = typeof(ClaudeService).GetField("_counterDate",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var countField = typeof(ClaudeService).GetField("_directCallsToday",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(dateField);
        Assert.NotNull(countField);
        dateField!.SetValue(service, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
        countField!.SetValue(service, 7);

        await service.AnalyzeAsync(SampleRequest());

        // New day resets to 0 then increments for this one direct call.
        Assert.Equal(1, GetDirectCallsToday(service));
    }

    [Fact]
    public void Ctor_EmptyGatewayKey_LogsMeteredWarning()
    {
        var (_, _, logger) = BuildService(EmptyGatewayConfig());

        Assert.True(LoggedAtLeastOnce(logger, LogLevel.Warning),
            "empty gateway key should warn that all calls use the metered direct API");
    }

    [Fact]
    public void Ctor_GatewayKeyPresent_LogsGatewayFirstPath()
    {
        var config = EmptyGatewayConfig();
        config.GatewayApiKey = "gw-token";
        var handler = new CountingHandler();
        var logger = new Mock<ILogger<ClaudeService>>();

        _ = new ClaudeService(logger.Object, Microsoft.Extensions.Options.Options.Create(config), new HttpClient(handler));

        Assert.True(LoggedAtLeastOnce(logger, LogLevel.Information),
            "present gateway key should log the gateway-first pricing path at Info");
    }

    // Minimal deserialization target mirroring the regime response shape used by the caller.
    private sealed class ClaudeRegimeStub
    {
        public string? Regime { get; set; }
    }
}
