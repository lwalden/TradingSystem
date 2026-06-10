namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Claude AI service interface for structured analysis requests.
/// Implementation in TradingSystem.AI; interface here so Strategies can depend on it.
/// </summary>
public interface IClaudeService
{
    Task<string> AnalyzeAsync(AIAnalysisRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the analysis request and deserializes the response into <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// Structured/legacy split (S4-004): when the request is served by the gateway leg AND
    /// <see cref="AIAnalysisRequest.JsonSchema"/> is set, the gateway enforces structured output
    /// and its JSON-string <c>response</c> field is deserialized directly into
    /// <typeparamref name="T"/> (no brace-scanning). The direct Anthropic API leg ignores the
    /// schema entirely and always uses the legacy whole-string/brace-scan parse, as does the
    /// gateway leg when no schema is supplied. Parse failures on either path surface as
    /// <see cref="InvalidOperationException"/> so callers fall back to deterministic rules
    /// (ADR-029/030).
    /// </remarks>
    Task<T> AnalyzeAsync<T>(AIAnalysisRequest request, CancellationToken cancellationToken = default)
        where T : class;
}
