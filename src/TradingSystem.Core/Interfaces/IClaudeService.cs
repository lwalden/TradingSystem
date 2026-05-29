namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Claude AI service interface for structured analysis requests.
/// Implementation in TradingSystem.AI; interface here so Strategies can depend on it.
/// </summary>
public interface IClaudeService
{
    Task<string> AnalyzeAsync(AIAnalysisRequest request, CancellationToken cancellationToken = default);
    Task<T> AnalyzeAsync<T>(AIAnalysisRequest request, CancellationToken cancellationToken = default)
        where T : class;
}

public class ClaudeConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string GatewayApiKey { get; set; } = string.Empty; // Bearer token for Claude Gateway (localhost:3131)
    public string Model { get; set; } = "claude-sonnet-4-20250514";
    public int MaxTokens { get; set; } = 2000;
    public double Temperature { get; set; } = 0.3;

    // TTL (minutes) for the cached MarketRegime result in CachingMarketDataService. Within this
    // window GetMarketRegimeAsync serves the cached regime and makes no Claude round-trip.
    public int RegimeCacheMinutes { get; set; } = 20;
}
