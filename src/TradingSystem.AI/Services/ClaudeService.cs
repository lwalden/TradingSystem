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
        // Four-state (2x2: gateway key present? × DirectApiFallbackEnabled?).
        var hasGatewayKey = !string.IsNullOrEmpty(_config.GatewayApiKey);
        if (hasGatewayKey && _config.DirectApiFallbackEnabled)
        {
            _logger.LogInformation(
                "Claude pricing path: gateway-first (subscription), metered fallback capped at {Max}/day",
                _config.MaxDirectApiCallsPerDay);
        }
        else if (hasGatewayKey)
        {
            _logger.LogInformation(
                "Claude pricing path: gateway-only (subscription); metered direct fallback DISABLED");
        }
        else if (_config.DirectApiFallbackEnabled)
        {
            _logger.LogWarning(
                "Claude gateway key not set; ALL calls will use the metered direct API, capped at {Max}/day",
                _config.MaxDirectApiCallsPerDay);
        }
        else
        {
            _logger.LogWarning(
                "Claude gateway key missing AND direct fallback disabled — AI regime path is effectively OFF; regime will always use deterministic rules");
        }
    }

    public async Task<string> AnalyzeAsync(AIAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        var (content, _) = await AnalyzeCoreAsync(request, cancellationToken);
        return content;
    }

    /// <summary>
    /// Shared gateway-then-direct pipeline. Returns the response content AND which leg produced
    /// it, because the two legs have different content shapes when a JsonSchema is supplied:
    /// the gateway returns a schema-conforming JSON string (S4-004), while the direct Anthropic
    /// API always returns plain text that may merely embed JSON (legacy brace-scan territory).
    /// </summary>
    private async Task<(string Content, bool FromGateway)> AnalyzeCoreAsync(
        AIAnalysisRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("AI analysis request. Strategy: {StrategyId}",
            request.StrategyId);

        // Try gateway first (CLI-based, subscription pricing)
        var gatewayResult = await TryGatewayAsync(request, cancellationToken);
        if (gatewayResult != null)
            return (gatewayResult, true);

        // Gateway miss. The metered direct API is the only remaining path, and it ships OFF by
        // default. When disabled, do NOT touch the cap and do NOT call Anthropic — return no
        // content so AnalyzeAsync<T> fails into the caller's try/catch → deterministic rules.
        // This reuses the exact empty-string seam as the cap-reached path below.
        if (!_config.DirectApiFallbackEnabled)
        {
            _logger.LogWarning(
                "Direct API fallback disabled; gateway miss → deterministic rules");
            return (string.Empty, false);
        }

        // Fallback enabled — the only remaining path is the metered direct API.
        // Enforce the daily cap (fail closed) before issuing any metered call.
        if (!TryReserveDirectCall(out var count, out var max))
        {
            // Cap reached: do NOT call Anthropic. Returning no content makes the generic
            // AnalyzeAsync<T> deserialize path fail into the caller's existing try/catch,
            // so the regime path falls back to deterministic rules. Mirrors the gateway-miss seam.
            _logger.LogWarning(
                "Daily metered-API cap {Max} reached; refusing direct call, falling back to rules",
                max);
            return (string.Empty, false);
        }

        _logger.LogWarning(
            "Claude gateway unavailable; falling back to METERED direct API. DirectCallsToday={Count}/{Max}",
            count, max);
        return (await CallAnthropicApiAsync(request, cancellationToken), false);
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

    // Gateway structured output (jsonSchema) responses use the schema's camelCase property
    // names; DTOs are PascalCase. Case-insensitive matching applies ONLY to the structured
    // gateway leg — the legacy brace-scan path keeps default (case-sensitive) options.
    private static readonly JsonSerializerOptions StructuredOutputOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<T> AnalyzeAsync<T>(AIAnalysisRequest request,
        CancellationToken cancellationToken = default) where T : class
    {
        var (response, fromGateway) = await AnalyzeCoreAsync(request, cancellationToken);

        // S4-004 (B-005): with a JsonSchema, the gateway's `response` field IS the JSON document
        // (a string conforming to the schema) — deserialize it whole, no brace-scanning. Any
        // parse failure is surfaced as InvalidOperationException so the caller's existing
        // try/catch falls back to deterministic rules (ADR-029/030), never a silent default.
        // The direct-API leg never honors jsonSchema, so its responses (and empty no-content
        // seams) stay on the legacy brace-scan path below.
        if (fromGateway && request.JsonSchema is not null)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(response, StructuredOutputOptions)
                    ?? throw new InvalidOperationException(
                        "Gateway structured response deserialized to null");
            }
            catch (JsonException ex)
            {
                // Review S4-004: log before rethrowing so operators can distinguish "gateway
                // returned junk" from "gateway down". Raw payload truncated to 200 chars to
                // bound the leak surface (security lens); the rethrow below is unchanged, so
                // the ADR-029/030 fail-to-rules contract is preserved.
                _logger.LogWarning(ex,
                    "Gateway structured-output parse failed for {Type}; raw (truncated): {Raw}",
                    typeof(T).Name,
                    response.Length > 200 ? response[..200] : response);
                throw new InvalidOperationException(
                    "Gateway structured response was not valid JSON for the requested type", ex);
            }
        }

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
            // Dictionary (not an anonymous type) so the optional `jsonSchema` field is OMITTED
            // entirely when no schema is supplied — the gateway treats its presence as the
            // structured-output switch, so serializing `"jsonSchema": null` is not equivalent.
            var body = new Dictionary<string, object?>
            {
                ["prompt"] = request.UserPrompt,
                ["system"] = request.SystemPrompt,
                ["model"] = request.PreferredModel ?? _config.Model
            };
            if (request.JsonSchema is not null)
                body["jsonSchema"] = request.JsonSchema;

            var gatewayRequest = new HttpRequestMessage(HttpMethod.Post, "ask")
            {
                Content = JsonContent.Create(body)
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
