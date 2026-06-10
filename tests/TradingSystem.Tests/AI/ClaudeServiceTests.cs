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
using TradingSystem.Core.Configuration;
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
        MaxDirectApiCallsPerDay = maxDirect,
        // S3-003: the metered direct fallback now defaults OFF. The S2-002 cap/fallback tests
        // below exercise the metered path on purpose, so enable the flag here to keep their
        // intent intact (a gateway miss reaches the metered branch as it did before S3-003).
        DirectApiFallbackEnabled = true
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
        GatewayTimeoutSeconds = timeoutSeconds,
        // S3-003: enable the metered fallback so the S2-004 gateway-hang test still falls through
        // to the direct API on a gateway miss (its original intent). The flag now defaults off.
        DirectApiFallbackEnabled = true
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

    // ---- S3-003: DirectApiFallbackEnabled flag (default off) + 35s gateway timeout ----

    // Like EmptyGatewayConfig but leaves DirectApiFallbackEnabled at its real default (false),
    // so a gateway miss must NOT reach the metered path. ApiKey is still set to prove that the
    // gate is the flag, not a missing key.
    private static ClaudeConfig FlagOffNoGatewayConfig(int maxDirect = 50) => new()
    {
        ApiKey = "test-key",
        GatewayApiKey = string.Empty, // gateway leg skipped
        Model = "claude-sonnet-4-20250514",
        MaxDirectApiCallsPerDay = maxDirect
        // DirectApiFallbackEnabled defaults to false — the behavior under test.
    };

    [Fact]
    public async Task FlagOff_GatewayDown_WithKey_DoesNotCallMeteredApi_AndFailsToRules()
    {
        var (service, handler, _) = BuildService(FlagOffNoGatewayConfig());

        var result = await service.AnalyzeAsync(SampleRequest());

        Assert.Equal(0, handler.InvocationCount); // metered API NOT hit
        Assert.Equal(0, GetDirectCallsToday(service)); // cap counter untouched
        Assert.True(string.IsNullOrEmpty(result), "flag-off gateway miss returns no content");

        // Generic path must throw into the caller's try/catch → deterministic rules.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AnalyzeAsync<ClaudeRegimeStub>(SampleRequest()));
        Assert.Equal(0, handler.InvocationCount); // still no metered call
    }

    [Fact]
    public async Task FlagOn_GatewayDown_WithKey_WithinCap_CallsMeteredApiOnce()
    {
        // EmptyGatewayConfig sets DirectApiFallbackEnabled = true (S2-002 path intact).
        var (service, handler, _) = BuildService(EmptyGatewayConfig());

        await service.AnalyzeAsync(SampleRequest());

        Assert.Equal(1, handler.InvocationCount);
        Assert.Equal(1, GetDirectCallsToday(service));
    }

    [Fact]
    public async Task FlagOn_CapReached_FailsClosedToRules()
    {
        var (service, handler, _) = BuildService(EmptyGatewayConfig(maxDirect: 1));

        // First call allowed, hits the metered API.
        await service.AnalyzeAsync(SampleRequest());
        Assert.Equal(1, handler.InvocationCount);

        // Second call refused (cap == 1): no new HTTP hit, returns no content.
        var emptyResult = await service.AnalyzeAsync(SampleRequest());
        Assert.Equal(1, handler.InvocationCount);
        Assert.True(string.IsNullOrEmpty(emptyResult), "capped call returns no content");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AnalyzeAsync<ClaudeRegimeStub>(SampleRequest()));
        Assert.Equal(1, handler.InvocationCount);
    }

    [Fact]
    public async Task GatewaySuccess_FlagOff_StillReturnsGatewayContent_NoMeteredCall()
    {
        var config = GatewayConfig(); // GatewayApiKey set
        config.DirectApiFallbackEnabled = false; // explicit: gateway success must not need the flag
        var gatewayHandler = new StubHandler(HttpStatusCode.OK,
            "{\"response\":\"{\\\"regime\\\":\\\"riskon\\\"}\",\"source\":\"subscription\",\"model\":\"claude\",\"durationMs\":12}");
        var (service, directHandler, _) = BuildGatewayService(config, gatewayHandler);

        var result = await service.AnalyzeAsync(SampleRequest());

        Assert.Contains("regime", result); // gateway content returned
        Assert.Equal(0, directHandler.InvocationCount); // metered API NOT hit
        Assert.Equal(0, GetDirectCallsToday(service)); // cap counter untouched
    }

    [Fact]
    public void GatewayTimeout_DefaultsTo35Seconds()
    {
        // The named ClaudeGateway client timeout binds from ClaudeConfig.GatewayTimeoutSeconds,
        // whose default is now 35s (S3-003). Wire the production DI registration with no explicit
        // timeout in config so the default flows through, then resolve the named client.
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig(
            ("Claude:GatewayApiKey", "gw-token"),
            ("Claude:GatewayBaseUrl", "http://localhost:3131/"));
        ClaudeServiceRegistration.Add(services, config);
        var provider = services.BuildServiceProvider();

        var namedClient = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ClaudeService.GatewayClientName);
        Assert.Equal(TimeSpan.FromSeconds(35), namedClient.Timeout);

        // The direct typed client must stay independent. AddHttpClient<IClaudeService, ClaudeService>()
        // registers its named client under nameof(ClaudeService); it carries the .NET default 100s
        // timeout (the registration sets no timeout on it), proving the 35s gateway change did not
        // leak onto the metered direct leg.
        var directClient = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(ClaudeService));
        Assert.NotEqual(TimeSpan.FromSeconds(35), directClient.Timeout);

        // And the bound config default itself is 35s.
        var bound = new ClaudeConfig();
        Assert.Equal(35, bound.GatewayTimeoutSeconds);
    }

    // ---- S4-005 (B-008): GatewayTimeoutSeconds upper-bound clamp ----

    // Captures every log entry emitted through the DI logging pipeline so registration-time
    // warnings (e.g. the timeout clamp) can be asserted without Moq-ing ILogger<T> categories.
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);
        public void Dispose() { }

        private sealed class CapturingLogger : ILogger
        {
            private readonly List<(LogLevel, string)> _entries;
            public CapturingLogger(List<(LogLevel, string)> entries) => _entries = entries;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
                => _entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private static (ServiceProvider Provider, CapturingLoggerProvider Logs)
        BuildGatewayRegistration(string timeoutSeconds)
    {
        var services = new ServiceCollection();
        var capture = new CapturingLoggerProvider();
        services.AddLogging(b => b.AddProvider(capture));
        var config = BuildConfig(
            ("Claude:GatewayApiKey", "gw-token"),
            ("Claude:GatewayBaseUrl", "http://localhost:3131/"),
            ("Claude:GatewayTimeoutSeconds", timeoutSeconds));
        ClaudeServiceRegistration.Add(services, config);
        return (services.BuildServiceProvider(), capture);
    }

    [Fact]
    public void GatewayTimeout_AboveMax_ClampsToMax_AndWarns()
    {
        // B-008: a fat-fingered timeout (e.g. 600s) must not produce a multi-minute hung gateway
        // leg. The registration clamps the named-client timeout to MaxGatewayTimeoutSeconds and
        // emits a warning — clamp, not throw, so a config typo degrades loudly instead of
        // crashing the Functions host (fails toward availability).
        var (provider, logs) = BuildGatewayRegistration("600");

        var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ClaudeService.GatewayClientName);

        Assert.Equal(TimeSpan.FromSeconds(ClaudeConfig.MaxGatewayTimeoutSeconds), client.Timeout);
        Assert.Equal(120, ClaudeConfig.MaxGatewayTimeoutSeconds);
        Assert.Contains(logs.Entries, e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("600") &&
            e.Message.Contains("120"));
    }

    [Fact]
    public void GatewayTimeout_AboveMax_StillYieldsUsableClaudeService()
    {
        // The clamp must fail toward availability: an out-of-range value still resolves a working
        // IClaudeService rather than throwing at startup.
        var (provider, _) = BuildGatewayRegistration("9999");

        Assert.NotNull(provider.GetService<IClaudeService>());
        var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ClaudeService.GatewayClientName);
        Assert.True(client.Timeout <= TimeSpan.FromSeconds(ClaudeConfig.MaxGatewayTimeoutSeconds));
    }

    [Fact]
    public void GatewayTimeout_InRange_Unchanged_NoWarning()
    {
        // Regression guard: the 35s default posture passes through untouched and silently.
        var (provider, logs) = BuildGatewayRegistration("35");

        var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(ClaudeService.GatewayClientName);

        Assert.Equal(TimeSpan.FromSeconds(35), client.Timeout);
        Assert.DoesNotContain(logs.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Ctor_GatewayKeyPresent_FallbackOff_LogsGatewayOnlyPosture()
    {
        var config = new ClaudeConfig
        {
            ApiKey = "test-key",
            GatewayApiKey = "gw-token",
            DirectApiFallbackEnabled = false
        };
        var handler = new CountingHandler();
        var logger = new Mock<ILogger<ClaudeService>>();
        var gatewayHandler = new StubHandler(HttpStatusCode.OK, "{}");
        var factory = new FakeHttpClientFactory(gatewayHandler, config);

        _ = new ClaudeService(
            logger.Object, Microsoft.Extensions.Options.Options.Create(config),
            new HttpClient(handler), factory);

        Assert.True(LoggedAtLeastOnce(logger, LogLevel.Information),
            "gateway key present + fallback off should log the gateway-only posture at Info");
    }

    [Fact]
    public void Ctor_NoGatewayKey_FallbackOff_LogsAiEffectivelyOffWarning()
    {
        var config = new ClaudeConfig
        {
            ApiKey = "test-key",
            GatewayApiKey = string.Empty,
            DirectApiFallbackEnabled = false
        };
        var handler = new CountingHandler();
        var logger = new Mock<ILogger<ClaudeService>>();
        var gatewayHandler = new StubHandler(HttpStatusCode.OK, "{}");
        var factory = new FakeHttpClientFactory(gatewayHandler, config);

        _ = new ClaudeService(
            logger.Object, Microsoft.Extensions.Options.Options.Create(config),
            new HttpClient(handler), factory);

        Assert.True(LoggedAtLeastOnce(logger, LogLevel.Warning),
            "no gateway key + fallback off should warn that the AI regime path is effectively OFF");
    }

    [Fact]
    public async Task Gateway_RequestContract_PostsAskWithBearerAndExpectedFields()
    {
        var config = GatewayConfig();
        var gatewayHandler = new StubHandler(HttpStatusCode.OK,
            "{\"response\":\"{\\\"regime\\\":\\\"riskon\\\"}\"}");
        var (service, _, _) = BuildGatewayService(config, gatewayHandler);

        await service.AnalyzeAsync(SampleRequest());

        Assert.Equal(1, gatewayHandler.InvocationCount);
        var req = gatewayHandler.LastRequest;
        Assert.NotNull(req);
        Assert.Equal(HttpMethod.Post, req!.Method);
        // Constructed with the relative path "ask"; HttpClient resolves it against the gateway
        // base address before the handler sees it, so assert the resolved path is "/ask".
        Assert.EndsWith("/ask", req.RequestUri!.AbsolutePath);
        Assert.True(req.Headers.TryGetValues("Authorization", out var auth));
        Assert.Equal($"Bearer {config.GatewayApiKey}", auth!.Single());

        var body = await req.Content!.ReadAsStringAsync();
        Assert.Contains("prompt", body);
        Assert.Contains("system", body);
        Assert.Contains("model", body);
    }

    // Minimal deserialization target mirroring the regime response shape used by the caller.
    private sealed class ClaudeRegimeStub
    {
        public string? Regime { get; set; }
    }
}
