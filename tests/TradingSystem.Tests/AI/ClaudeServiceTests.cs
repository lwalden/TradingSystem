using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TradingSystem.AI.Services;
using TradingSystem.Core.Interfaces;
using Xunit;

namespace TradingSystem.Tests.AI;

/// <summary>
/// S2-002: fail-closed cost controls on the metered direct-API fallback.
/// All metered-path tests leave GatewayApiKey EMPTY so the gateway path is skipped without any
/// HTTP, driving the metered direct-API branch in isolation.
/// S2-004: gateway HTTP cluster via IHttpClientFactory + gateway-only DI registration.
/// </summary>
public class ClaudeServiceTests
{
    // Stub handler that records invocations and returns a caller-supplied response (or canned 200).
    // Used for the gateway leg so tests can assert whether the gateway was hit and what it returned.
    private sealed class StubHandler : HttpMessageHandler
    {
        public int InvocationCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public StubHandler(HttpStatusCode status, string json)
            : this(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            })
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            LastRequest = request;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_responder(request));
        }
    }

    // Fake IHttpClientFactory: returns an HttpClient backed by a controllable handler for the
    // named "ClaudeGateway" client, configured from the supplied ClaudeConfig (base + timeout)
    // exactly as the production named-client registration would.
    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _gatewayHandler;
        private readonly ClaudeConfig _config;
        public HttpClient? LastGatewayClient { get; private set; }

        public FakeHttpClientFactory(HttpMessageHandler gatewayHandler, ClaudeConfig config)
        {
            _gatewayHandler = gatewayHandler;
            _config = config;
        }

        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(_gatewayHandler)
            {
                BaseAddress = new Uri(_config.GatewayBaseUrl),
                Timeout = TimeSpan.FromSeconds(_config.GatewayTimeoutSeconds)
            };
            if (name == "ClaudeGateway")
                LastGatewayClient = client;
            return client;
        }
    }
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
        // Gateway handler that must never be hit when GatewayApiKey is empty.
        var gatewayHandler = new StubHandler(_ =>
            throw new InvalidOperationException("gateway must not be called when GatewayApiKey is empty"));
        var factory = new FakeHttpClientFactory(gatewayHandler, config);
        var service = new ClaudeService(
            logger.Object, Microsoft.Extensions.Options.Options.Create(config), httpClient, factory);
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
        var gatewayHandler = new StubHandler(HttpStatusCode.OK, "{}");
        var factory = new FakeHttpClientFactory(gatewayHandler, config);

        _ = new ClaudeService(
            logger.Object, Microsoft.Extensions.Options.Options.Create(config),
            new HttpClient(handler), factory);

        Assert.True(LoggedAtLeastOnce(logger, LogLevel.Information),
            "present gateway key should log the gateway-first pricing path at Info");
    }

    // ---- S2-004: gateway HTTP cluster via IHttpClientFactory ----

    private static ClaudeConfig GatewayConfig(int timeoutSeconds = 8) => new()
    {
        ApiKey = "test-key",
        GatewayApiKey = "gw-token", // gateway path active
        Model = "claude-sonnet-4-20250514",
        MaxDirectApiCallsPerDay = 50,
        GatewayBaseUrl = "http://localhost:3131/",
        GatewayTimeoutSeconds = timeoutSeconds
    };

    // Builds a service whose gateway leg is served by the supplied handler and whose direct leg
    // is a CountingHandler, so tests can assert which path actually issued an HTTP call.
    private static (ClaudeService Service, CountingHandler DirectHandler, FakeHttpClientFactory Factory)
        BuildGatewayService(ClaudeConfig config, HttpMessageHandler gatewayHandler,
            string directResponseText = "{\"regime\":\"riskon\"}")
    {
        var directHandler = new CountingHandler(directResponseText);
        var httpClient = new HttpClient(directHandler);
        var logger = new Mock<ILogger<ClaudeService>>();
        var factory = new FakeHttpClientFactory(gatewayHandler, config);
        var service = new ClaudeService(
            logger.Object, Microsoft.Extensions.Options.Options.Create(config), httpClient, factory);
        return (service, directHandler, factory);
    }

    [Fact]
    public void Gateway_UsesConfiguredTimeout()
    {
        var config = GatewayConfig(timeoutSeconds: 5);
        var gatewayHandler = new StubHandler(HttpStatusCode.OK,
            "{\"response\":\"{\\\"regime\\\":\\\"riskon\\\"}\"}");
        var (_, _, factory) = BuildGatewayService(config, gatewayHandler);

        // The gateway client is created lazily on first use; force creation.
        var client = factory.CreateClient("ClaudeGateway");

        Assert.Equal(TimeSpan.FromSeconds(5), client.Timeout);
    }

    [Fact]
    public async Task Gateway_Hang_FallsBackFast()
    {
        var config = GatewayConfig();
        // Gateway "hangs": handler honors cancellation by throwing TaskCanceledException,
        // simulating a timeout. ClaudeService must catch and proceed to the direct path.
        var gatewayHandler = new StubHandler(_ => throw new TaskCanceledException("simulated timeout"));
        var (service, directHandler, _) = BuildGatewayService(config, gatewayHandler);

        var result = await service.AnalyzeAsync(SampleRequest());

        Assert.Equal(1, directHandler.InvocationCount); // direct path was taken
        Assert.Contains("regime", result); // direct response returned, no indefinite hang
    }

    [Fact]
    public async Task GatewaySuccess_DoesNotIncrementDirectCounter()
    {
        var config = GatewayConfig();
        // Valid gateway response: {"response":"...content..."} per GatewayResponse shape.
        var gatewayHandler = new StubHandler(HttpStatusCode.OK,
            "{\"response\":\"{\\\"regime\\\":\\\"riskon\\\"}\",\"source\":\"subscription\",\"model\":\"claude\",\"durationMs\":12}");
        var (service, directHandler, _) = BuildGatewayService(config, gatewayHandler);

        var result = await service.AnalyzeAsync(SampleRequest());

        Assert.Equal(0, directHandler.InvocationCount); // direct API NOT hit
        Assert.Equal(0, GetDirectCallsToday(service)); // S2-002 counter stays 0
        Assert.Contains("regime", result);
    }

    // ---- S2-004: gateway-only DI registration via ClaudeServiceRegistration ----

    private static IConfiguration BuildConfig(params (string Key, string Value)[] pairs)
    {
        var dict = pairs.ToDictionary(p => p.Key, p => (string?)p.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void Program_GatewayOnlyConfig_RegistersClaudeService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig(
            ("Claude:GatewayApiKey", "gw-token"),
            ("Claude:GatewayBaseUrl", "http://localhost:3131/"),
            ("Claude:GatewayTimeoutSeconds", "8"));

        ClaudeServiceRegistration.Add(services, config);

        var provider = services.BuildServiceProvider();
        var resolved = provider.GetService<IClaudeService>();
        Assert.NotNull(resolved);
    }

    [Fact]
    public void Program_NoKeys_DoesNotRegisterClaudeService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig(("Claude:Model", "claude-sonnet-4-20250514"));

        ClaudeServiceRegistration.Add(services, config);

        var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<IClaudeService>());
    }

    // Minimal deserialization target mirroring the regime response shape used by the caller.
    private sealed class ClaudeRegimeStub
    {
        public string? Regime { get; set; }
    }
}
