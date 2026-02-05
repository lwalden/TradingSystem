using TradingSystem.Core.Interfaces;

namespace TradingSystem.AI.Services;

public interface IClaudeService
{
    Task<string> AnalyzeAsync(AIAnalysisRequest request, CancellationToken cancellationToken = default);
    Task<T> AnalyzeAsync<T>(AIAnalysisRequest request, CancellationToken cancellationToken = default) 
        where T : class;
}

public class ClaudeConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-4-20250514";
    public int MaxTokens { get; set; } = 2000;
    public double Temperature { get; set; } = 0.3;
}
