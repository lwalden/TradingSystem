using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Strategies.Services;

/// <summary>
/// Owns market-regime detection: the single-slot regime cache + stampede guard, the
/// Claude-then-rules computation, the AI-multiplier safety clamp, and the rule-based
/// multiplier table. Extracted from <see cref="CachingMarketDataService"/> (KD-004 / ADR-017)
/// with zero behavior change. The facade composes this internally and delegates
/// <c>GetMarketRegimeAsync</c> to it.
///
/// SPY indicators and the VIX quote are reached through the facade's cached accessors
/// (passed as delegates) so the existing quote/indicator caches are reused — exactly as
/// before the extraction.
///
/// internal (not private) so the existing InternalsVisibleTo("TradingSystem.Tests") seam can
/// unit-test <see cref="SanitizeRationale"/> and mock <see cref="ClaudeRegimeResponse"/>.
/// </summary>
internal class MarketRegimeProvider
{
    private readonly Func<string, CancellationToken, Task<TechnicalIndicators>> _getIndicators;
    private readonly Func<string, CancellationToken, Task<Quote>> _getQuote;
    private readonly ILogger _logger;
    private readonly IClaudeService? _claudeService;

    // Single-slot regime cache (GetMarketRegimeAsync takes no key). Mirrors the quote-cache style.
    // The semaphore is a stampede guard: N concurrent callers serialize on it so at most one
    // underlying computation (and at most one Claude round-trip) runs per TTL window.
    private (MarketRegime Regime, DateTime CachedAt)? _regimeCache;
    private readonly SemaphoreSlim _regimeLock = new(1, 1);
    private readonly TimeSpan _regimeCacheDuration;

    // Safety bounds (NOT config) for the Claude-supplied RiskMultiplier. The clamp applies
    // ONLY to the AI regime-detection path; the rule-based regime multiplier table
    // (RiskOn=1.0 .. RiskOff=0.25 in ComputeMarketRegimeAsync) is intentionally left unchanged.
    private const decimal MinRiskMultiplier = 0.5m;
    private const decimal MaxRiskMultiplier = 1.0m;

    // JSON Schema for the gateway's structured-output (`jsonSchema`) request field (S4-004 /
    // B-005). Mirrors ClaudeRegimeResponse: the gateway enforces this shape and returns the
    // `response` body field as a conforming JSON string, eliminating brace-scan extraction.
    // The schema's bounds are advisory to the model; the authoritative risk-multiplier clamp
    // in DetectRegimeWithClaudeAsync is unchanged and still applies to whatever comes back.
    private static readonly object RegimeResponseSchema = new
    {
        type = "object",
        properties = new
        {
            regime = new
            {
                type = "string",
                @enum = new[] { "RiskOn", "Cautious", "RiskOff", "Recovery" }
            },
            riskMultiplier = new { type = "number", minimum = 0.5, maximum = 1.0 },
            rationale = new { type = "string" },
            keyFactors = new { type = "array", items = new { type = "string" } }
        },
        required = new[] { "regime", "riskMultiplier", "rationale" }
    };

    public MarketRegimeProvider(
        Func<string, CancellationToken, Task<TechnicalIndicators>> getIndicators,
        Func<string, CancellationToken, Task<Quote>> getQuote,
        ILogger logger,
        IClaudeService? claudeService,
        IOptions<ClaudeConfig>? claudeOptions)
    {
        _getIndicators = getIndicators;
        _getQuote = getQuote;
        _logger = logger;
        _claudeService = claudeService;
        _regimeCacheDuration = TimeSpan.FromMinutes(claudeOptions?.Value.RegimeCacheMinutes ?? 20);
    }

    public async Task<MarketRegime> GetMarketRegimeAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: serve the cached regime if it is still within TTL (0 Claude calls).
        if (_regimeCache is { } cached && DateTime.UtcNow - cached.CachedAt < _regimeCacheDuration)
            return cached.Regime;

        // Stampede guard: serialize concurrent callers so only one recomputes per TTL window.
        await _regimeLock.WaitAsync(cancellationToken);
        try
        {
            // Double-checked locking: a caller that queued behind the computation gets the fresh
            // value here without triggering another (Claude-then-rules) computation.
            if (_regimeCache is { } current && DateTime.UtcNow - current.CachedAt < _regimeCacheDuration)
                return current.Regime;

            var result = await ComputeMarketRegimeAsync(cancellationToken);
            _regimeCache = (result, DateTime.UtcNow);
            return result;
        }
        finally
        {
            _regimeLock.Release();
        }
    }

    // Runs the existing Claude-then-rules detection. Both outcomes (Claude result AND rule
    // fallback) are returned to the caller, which caches whichever is produced.
    private async Task<MarketRegime> ComputeMarketRegimeAsync(CancellationToken cancellationToken)
    {
        var spyIndicators = await _getIndicators("SPY", cancellationToken);
        var vixQuote = await _getQuote("VIX", cancellationToken);

        // Try Claude-enhanced regime detection if available
        if (_claudeService != null)
        {
            try
            {
                var claudeRegime = await DetectRegimeWithClaudeAsync(vixQuote, spyIndicators, cancellationToken);
                if (claudeRegime != null)
                {
                    _logger.LogInformation(
                        "Claude regime: {Regime}, RiskMultiplier: {Mult}, Rationale: {Rationale}",
                        claudeRegime.Regime, claudeRegime.RiskMultiplier,
                        SanitizeRationale(claudeRegime.Rationale));
                    return claudeRegime;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Claude regime detection failed, falling back to rules");
            }
        }

        // Rule-based fallback (always available)
        var regime = DetermineRegime(vixQuote.Last, spyIndicators);
        var riskMultiplier = regime switch
        {
            RegimeType.RiskOn => 1.0m,
            RegimeType.Recovery => 0.75m,
            RegimeType.Cautious => 0.5m,
            RegimeType.RiskOff => 0.25m,
            _ => 1.0m
        };

        _logger.LogInformation("Rule-based regime: {Regime}, RiskMultiplier: {Mult}", regime, riskMultiplier);

        return new MarketRegime
        {
            VIX = vixQuote.Last,
            SPYPrice = spyIndicators.SMA20 ?? 0,
            SPY50DMA = spyIndicators.SMA50 ?? 0,
            SPY200DMA = spyIndicators.SMA200 ?? 0,
            SPYDistanceFrom50DMA = spyIndicators.DistanceFrom50DMA ?? 0,
            Regime = regime,
            RiskMultiplier = riskMultiplier,
            Timestamp = DateTime.UtcNow
        };
    }

    // Sanitize an AI-supplied rationale for safe structured logging (App Insights): collapses
    // whitespace, strips control characters (defends against log injection / forging via embedded
    // newlines), and caps length so a runaway rationale cannot bloat a log record. The stored
    // MarketRegime.Rationale model value is NEVER mutated — only the log projection is sanitized.
    // internal (not private) so the existing InternalsVisibleTo("TradingSystem.Tests") can unit-test it.
    internal static string SanitizeRationale(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "none";

        const int MaxLen = 500;
        var sb = new System.Text.StringBuilder(Math.Min(raw.Length, MaxLen) + 1);
        var lastWasSpace = false;

        foreach (var ch in raw)
        {
            // Treat any control char (newline, tab, CR, etc.) or whitespace as a single space.
            if (char.IsControl(ch) || char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }

            sb.Append(ch);
            lastWasSpace = false;

            if (sb.Length >= MaxLen)
                break;
        }

        // Trim a trailing collapsed space, then mark truncation if we stopped early.
        if (sb.Length > 0 && sb[^1] == ' ')
            sb.Length--;

        var truncated = sb.Length >= MaxLen;
        var cleaned = sb.ToString();
        if (cleaned.Length == 0)
            return "none";

        return truncated ? cleaned + "…" : cleaned;
    }

    private async Task<MarketRegime?> DetectRegimeWithClaudeAsync(
        Quote vixQuote, TechnicalIndicators spyIndicators, CancellationToken cancellationToken)
    {
        var spyVs50dma = spyIndicators.DistanceFrom50DMA ?? 0m;
        var spyVs200dma = spyIndicators.SMA200.HasValue && spyIndicators.SMA200.Value > 0
            ? ((spyIndicators.SMA20 ?? 0m) - spyIndicators.SMA200.Value) / spyIndicators.SMA200.Value * 100m
            : 0m;

        var userPrompt = $@"Analyze current market conditions:
- VIX: {vixQuote.Last:F1}
- SPY vs 50-DMA: {spyVs50dma:F1}%
- SPY vs 200-DMA: {spyVs200dma:F1}%
- Advance/Decline Ratio: N/A
- % Stocks above 200-DMA: N/A

Classify the regime and recommend a risk multiplier (0.5 = half risk, 1.0 = full risk).";

        var request = new AIAnalysisRequest
        {
            StrategyId = "regime-detection",
            SystemPrompt = @"You are a market analyst assistant for an automated trading system. Your role is to assess current market conditions and provide a regime classification.

Respond ONLY with valid JSON in this exact format:
{
    ""regime"": ""RiskOn|Cautious|RiskOff|Recovery"",
    ""riskMultiplier"": 0.5-1.0,
    ""rationale"": ""Brief explanation"",
    ""keyFactors"": [""factor1"", ""factor2""]
}",
            UserPrompt = userPrompt,
            MaxTokens = 500,
            JsonSchema = RegimeResponseSchema
        };

        var response = await _claudeService!.AnalyzeAsync<ClaudeRegimeResponse>(request, cancellationToken);

        var regime = response.Regime?.ToLowerInvariant() switch
        {
            "riskon" => RegimeType.RiskOn,
            "cautious" => RegimeType.Cautious,
            "riskoff" => RegimeType.RiskOff,
            "recovery" => RegimeType.Recovery,
            _ => (RegimeType?)null
        };

        if (regime == null)
        {
            _logger.LogWarning("Claude returned unrecognized regime: {Regime}", response.Regime);
            return null;
        }

        // Bound the AI-supplied multiplier to [Min, Max] before it can influence position
        // sizing. Absent values default to 1.0 (in range, no warn); out-of-range values
        // clamp to the nearest bound and emit a Warning (fail-closed: never riskier).
        // NOTE: this clamp applies ONLY to the AI path; the rule-based multiplier table is untouched.
        var rawMultiplier = (decimal)(response.RiskMultiplier ?? 1.0);
        var clampedMultiplier = Math.Clamp(rawMultiplier, MinRiskMultiplier, MaxRiskMultiplier);
        if (clampedMultiplier != rawMultiplier)
        {
            _logger.LogWarning(
                "Claude RiskMultiplier {Raw} out of bounds [{Min},{Max}]; clamped to {Clamped}",
                rawMultiplier, MinRiskMultiplier, MaxRiskMultiplier, clampedMultiplier);
        }

        return new MarketRegime
        {
            VIX = vixQuote.Last,
            SPYPrice = spyIndicators.SMA20 ?? 0,
            SPY50DMA = spyIndicators.SMA50 ?? 0,
            SPY200DMA = spyIndicators.SMA200 ?? 0,
            SPYDistanceFrom50DMA = spyVs50dma,
            Regime = regime.Value,
            RiskMultiplier = clampedMultiplier,
            Rationale = response.Rationale,
            Source = RegimeSource.Claude,
            Timestamp = DateTime.UtcNow
        };
    }

    // Internal (not private) so unit tests can construct and mock AnalyzeAsync&lt;ClaudeRegimeResponse&gt;.
    // The Strategies csproj exposes internals to TradingSystem.Tests via InternalsVisibleTo.
    internal class ClaudeRegimeResponse
    {
        public string? Regime { get; set; }
        public double? RiskMultiplier { get; set; }
        public string? Rationale { get; set; }
        public List<string>? KeyFactors { get; set; }
    }

    private static RegimeType DetermineRegime(decimal vix, TechnicalIndicators spyIndicators)
    {
        if (vix > 35) return RegimeType.RiskOff;
        if (vix > 25) return RegimeType.Cautious;
        if (spyIndicators.Above50DMA == false && spyIndicators.Above200DMA == false)
            return RegimeType.RiskOff;
        if (spyIndicators.Above50DMA == false)
            return RegimeType.Cautious;
        return RegimeType.RiskOn;
    }
}
