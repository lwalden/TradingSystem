using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Core.Interfaces;

namespace TradingSystem.AI.Services;

/// <summary>
/// Claude API integration service.
/// Routes through Claude Gateway (localhost:3131) first for subscription-based pricing.
/// Falls back to direct Anthropic API if the gateway is unavailable or returns an error.
/// </summary>
public class ClaudeService : IClaudeService
{
    private readonly ILogger<ClaudeService> _logger;
    private readonly ClaudeConfig _config;
    private readonly HttpClient _httpClient;

    // Gateway is a separate HTTP target — build its client once
    private static readonly HttpClient _gatewayClient = new()
    {
        BaseAddress = new Uri("http://localhost:3131/"),
        Timeout = TimeSpan.FromSeconds(60)
    };

    public ClaudeService(
        ILogger<ClaudeService> logger,
        IOptions<ClaudeConfig> config,
        HttpClient httpClient)
    {
        _logger = logger;
        _config = config.Value;
        _httpClient = httpClient;

        _httpClient.BaseAddress = new Uri("https://api.anthropic.com/");
        _httpClient.DefaultRequestHeaders.Add("x-api-key", _config.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
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

        // Fall back to direct Anthropic API
        _logger.LogInformation("Gateway unavailable, falling back to direct API");
        return await CallAnthropicApiAsync(request, cancellationToken);
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

            var response = await _gatewayClient.SendAsync(gatewayRequest, cancellationToken);

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
