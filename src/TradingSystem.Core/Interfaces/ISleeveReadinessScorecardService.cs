using TradingSystem.Core.Models;

namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Produces the weekly per-sleeve paper-validation readiness scorecards (S4-002 / PDR-004).
/// Recommendation-only and strictly read-only: implementations never write config, never
/// change mode/risk/sleeve weights, and never place orders. Consumers (e.g. the S4-003
/// Discord report) depend on this seam rather than the concrete service.
/// </summary>
public interface ISleeveReadinessScorecardService
{
    /// <summary>
    /// Produces one scorecard per sleeve (Income, Options/Tactical) for the observation
    /// window ending at <paramref name="asOf"/>. Deterministic and safe to call from
    /// reporting paths.
    /// </summary>
    Task<IReadOnlyList<SleeveReadinessScorecard>> GenerateAsync(
        DateTime asOf, CancellationToken ct = default);
}
