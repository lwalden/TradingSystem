using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;

namespace TradingSystem.AI.Services;

/// <summary>
/// Claude API integration service.
/// Routes through Claude Gateway (localhost:3131) first for subscription-based pricing.
/// Falls back to direct Anthropic API if the gateway is unavailable or returns an error.
/// </summary>
public class ClaudeService : IClaudeService
{
    // Named client key for the gateway leg, registered via AddHttpClient("ClaudeGateway", ...).
    public const string GatewayClientName = "ClaudeGateway";

    private readonly ILogger<ClaudeService> _logger;
    private readonly ClaudeConfig _config;
    private readonly HttpClient _httpClient;
    private readonly IHttpClientFactory _httpFactory;

    // Daily soft cap on metered direct-API calls. Instance state guarded by _counterLock;
    // it resets on a new UTC day AND on process restart. A restart-resetting counter is an
    // acceptable trade-off for a daily soft cap — the worst case is one extra day's worth of
    // budget after a redeploy, which fails toward availability, not toward overspend within a day.
    private readonly object _counterLock = new();
    private int _directCallsToday;
    private DateOnly _counterDate = DateOnly.FromDateTime(DateTime.UtcNow);

    public ClaudeService(
        ILogger<ClaudeService> logger,
        IOptions<ClaudeConfig> config,
        HttpClient httpClient,
        IHttpClientFactory httpFactory)
    {
        _logger = logger;
        _config = config.Value;
        _httpClient = httpClient;
        _httpFactory = httpFactory;

        _httpClient.BaseAddress = new Uri("https://api.anthropic.com/");
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _config.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        // Surface the active pricing path at startup so the cost posture is visible in logs.
        if (string.IsNullOrEmpty(_config.GatewayApiKey))
        {
            _logger.LogWarning(
                "Claude gateway key not set; ALL calls will use the metered direct API");
        }
        else
        {
            _logger.LogInformation(
                "Claude pricing path: gateway-first (subscription), metered fallback capped at {Max}/day",
                _config.MaxDirectApiCallsPerDay);
        }
    }

    public async Task<string> AnalyzeAsync(AIAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("AI analysis request. Strategy: {StrategyId}",
            request.StrategyId);

        // Try gateway first (CLI-based, subscription pricing)
        var gatewayResult = await TryGatewayAsync(request, cancellationToken);
        if (gatewayResult != null)
            return gatewayResult;

        // Gateway unavailable — the only remaining path is the metered direct API.
        // Enforce the daily cap (fail closed) before issuing any metered call.
        if (!TryReserveDirectCall(out var count, out var max))
        {
            // Cap reached: do NOT call Anthropic. Returning no content makes the generic
            // AnalyzeAsync<T> deserialize path fail into the caller's existing try/catch,
            // so the regime path falls back to deterministic rules. Mirrors the gateway-miss seam.
            _logger.LogWarning(
                "Daily metered-API cap {Max} reached; refusing direct call, falling back to rules",
                max);
            return string.Empty;
        }

        _logger.LogWarning(
            "Claude gateway unavailable; falling back to METERED direct API. DirectCallsToday={Count}/{Max}",
            count, max);
        return await CallAnthropicApiAsync(request, cancellationToken);
    }

    /// <summary>
    /// Reserve a slot for one metered direct call against the daily cap. Resets the counter on a
    /// new UTC day. Returns true and increments <see cref="_directCallsToday"/> when a call is
    /// permitted (the increment happens ONLY here, i.e. only when a direct call is actually about
    /// to be issued — never on gateway success). Returns false when the cap is already reached.
    /// </summary>
    private bool TryReserveDirectCall(out int countAfterReserve, out int max)
    {
        max = _config.MaxDirectApiCallsPerDay;
        lock (_counterLock)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (today != _counterDate)
            {
                _counterDate = today;
                _directCallsToday = 0;
            }

            if (_directCallsToday >= max)
            {
                countAfterReserve = _directCallsToday;
                return false;
            }

            _directCallsToday++;
            countAfterReserve = _directCallsToday;
            return true;
        }
    }

    public async Task<T> AnalyzeAsync<T>(AIAnalysisRequest request,
        CancellationToken cancellationToken = default) where T : class
    {
        var response = await AnalyzeAsync(request, cancellationToken);

        var jsonStart = response.IndexOf('{');
        var jsonEnd = response.LastIndexOf('}');

        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
            return JsonSerializer.Deserialize<T>(json)
                ?? throw new InvalidOperationException("Failed to deserialize Claude response");
        }

        throw new InvalidOperationException("Claude response did not contain valid JSON");
    }

    /// <summary>
    /// Try the local Claude Gateway (CLI-based, subscription pricing).
    /// Returns null if the gateway is unreachable or returns an error.
    /// </summary>
    private async Task<string?> TryGatewayAsync(AIAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_config.GatewayApiKey))
            return null;

        try
        {
            var gatewayRequest = new HttpRequestMessage(HttpMethod.Post, "ask")
            {
                Content = JsonContent.Create(new
                {
                    prompt = request.UserPrompt,
                    system = request.SystemPrompt,
                    model = request.PreferredModel ?? _config.Model
                })
            };
            gatewayRequest.Headers.Add("Authorization", $"Bearer {_config.GatewayApiKey}");

            // Resolve the named gateway client from the factory. Base address and the short
            // gateway timeout are configured on the named registration (see ClaudeServiceRegistration),
            // keeping the direct typed-client's 60s timeout independent.
            var gatewayClient = _httpFactory.CreateClient(GatewayClientName);
            var response = await gatewayClient.SendAsync(gatewayRequest, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gateway returned {StatusCode}", response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<GatewayResponse>(
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Gateway response via {Source}, model={Model}, {Duration}ms",
                result?.Source ?? "unknown", result?.Model ?? "unknown", result?.DurationMs ?? 0);

            return result?.Response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Gateway unreachable");
            return null;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Gateway request timed out");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gateway error");
            return null;
        }
    }

    /// <summary>
    /// Direct Anthropic API call (per-token pricing).
    /// </summary>
    private async Task<string> CallAnthropicApiAsync(AIAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            model = request.PreferredModel ?? _config.Model,
            max_tokens = request.MaxTokens > 0 ? request.MaxTokens : _config.MaxTokens,
            system = request.SystemPrompt,
            messages = new[]
            {
                new { role = "user", content = request.UserPrompt }
            }
        };

        var response = await _httpClient.PostAsJsonAsync(
            "v1/messages",
            payload,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AnthropicResponse>(
            cancellationToken: cancellationToken);

        var content = result?.Content?.FirstOrDefault()?.Text ?? string.Empty;

        _logger.LogInformation("Direct API response. Length: {Length} chars", content.Length);

        return content;
    }
}

internal class GatewayResponse
{
    public string? Response { get; set; }
    public string? Source { get; set; }
    public string? Model { get; set; }
    public int? DurationMs { get; set; }
}

internal class AnthropicResponse
{
    public List<ContentBlock>? Content { get; set; }
}

internal class ContentBlock
{
    public string? Type { get; set; }
    public string? Text { get; set; }
}
