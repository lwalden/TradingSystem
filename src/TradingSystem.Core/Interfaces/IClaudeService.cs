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
