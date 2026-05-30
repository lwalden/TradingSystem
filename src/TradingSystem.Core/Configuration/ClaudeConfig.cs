namespace TradingSystem.Core.Configuration;

/// <summary>
/// Configuration for the Claude AI integration (direct Anthropic API + local Claude Gateway).
/// Bound from the "Claude" configuration section.
/// </summary>
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

    // Master switch for the metered per-token direct-Anthropic-API path. Default OFF: in the
    // default posture the local gateway is the only AI path, and a gateway miss fails safe to
    // deterministic rules with no metered call and no daily-cap consumption. The ApiKey slot
    // stays so the path can be enabled (set this true) without re-plumbing credentials. When ON,
    // the S2-002 MaxDirectApiCallsPerDay fail-closed cap governs the metered path unchanged.
    public bool DirectApiFallbackEnabled { get; set; } = false;

    // Daily soft cap on metered direct-API (per-token) calls. When the gateway is unavailable
    // and this many direct calls have already been made today, ClaudeService refuses further
    // metered calls and fails closed (returns no content) so callers fall back to rules.
    public int MaxDirectApiCallsPerDay { get; set; } = 50;

    // Base address of the local Claude Gateway (loopback-only, plaintext HTTP — see ADR-029).
    public string GatewayBaseUrl { get; set; } = "http://localhost:3131/";

    // Per-request timeout (seconds) for the gateway leg. Sized to cover Claude CLI cold-start for
    // the ~1/day regime call (tiny prompt, MaxTokens=500); the gateway INTEGRATION guide recommends
    // a client timeout >=35s. A hung/slow gateway still falls back via the AnalyzeAsync seam — to
    // deterministic rules when DirectApiFallbackEnabled is off, or to the metered direct API when on.
    public int GatewayTimeoutSeconds { get; set; } = 35;
}
