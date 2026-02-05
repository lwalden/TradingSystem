using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Core.Interfaces;

namespace TradingSystem.AI.Services;

/// <summary>
/// Claude API integration service
/// </summary>
public class ClaudeService : IClaudeService
{
    private readonly ILogger<ClaudeService> _logger;
    private readonly ClaudeConfig _config;
    private readonly HttpClient _httpClient;

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
        _logger.LogInformation("Sending analysis request to Claude. Strategy: {StrategyId}", 
            request.StrategyId);

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

        var result = await response.Content.ReadFromJsonAsync<ClaudeResponse>(
            cancellationToken: cancellationToken);

        var content = result?.Content?.FirstOrDefault()?.Text ?? string.Empty;
        
        _logger.LogInformation("Received Claude response. Length: {Length} chars", content.Length);
        
        return content;
    }

    public async Task<T> AnalyzeAsync<T>(AIAnalysisRequest request, 
        CancellationToken cancellationToken = default) where T : class
    {
        var response = await AnalyzeAsync(request, cancellationToken);
        
        // Try to extract JSON from response
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
}

internal class ClaudeResponse
{
    public List<ContentBlock>? Content { get; set; }
}

internal class ContentBlock
{
    public string? Type { get; set; }
    public string? Text { get; set; }
}
