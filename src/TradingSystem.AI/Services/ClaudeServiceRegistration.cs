using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;

namespace TradingSystem.AI.Services;

/// <summary>
/// Centralizes the Claude AI DI wiring so it can be exercised by tests independently of
/// the Functions host's top-level <c>Program.cs</c>.
///
/// Registration is fail-closed and gated: <see cref="IClaudeService"/> and its HTTP clients are
/// registered only when at least one credential is configured — either a direct Anthropic
/// <c>Claude:ApiKey</c> OR a gateway <c>Claude:GatewayApiKey</c>. Gateway-only mode (no direct key)
/// still registers so the subscription-priced path is available; if that path later falls back to
/// the metered direct API without a key, the call returns 401 → <c>EnsureSuccessStatusCode</c>
/// throws → the caller's regime path falls back to deterministic rules (NOT fail-open).
/// </summary>
public static class ClaudeServiceRegistration
{
    /// <summary>
    /// Registers <see cref="IClaudeService"/> with both HTTP legs:
    /// the direct Anthropic typed client (60s timeout, unchanged) and the named
    /// <see cref="ClaudeService.GatewayClientName"/> client whose base address and gateway timeout
    /// are bound from the <c>Claude</c> configuration section. No-op when no key is configured.
    /// </summary>
    public static void Add(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ClaudeConfig>(configuration.GetSection("Claude"));

        var directKey = configuration["Claude:ApiKey"];
        var gatewayKey = configuration["Claude:GatewayApiKey"];

        // Gate: register only when at least one credential is present. Gateway-only mode counts.
        if (string.IsNullOrEmpty(directKey) && string.IsNullOrEmpty(gatewayKey))
            return;

        var config = new ClaudeConfig();
        configuration.GetSection("Claude").Bind(config);

        // Named gateway client — separate HTTP target, gateway timeout so a hung gateway falls back
        // fast. Base/timeout bound from config so they track ADR-029 without code edits.
        // B-008: the timeout is clamped to ClaudeConfig.MaxGatewayTimeoutSeconds with a warning, so
        // a config typo (e.g. 3500 for 35) degrades loudly to a bounded wait instead of parking the
        // gateway leg for minutes — or crashing the host, which is why this clamps rather than throws.
        services.AddHttpClient(ClaudeService.GatewayClientName, (sp, c) =>
        {
            if (config.GatewayTimeoutSeconds > ClaudeConfig.MaxGatewayTimeoutSeconds)
            {
                sp.GetService<ILoggerFactory>()
                    ?.CreateLogger(typeof(ClaudeServiceRegistration))
                    .LogWarning(
                        "Claude:GatewayTimeoutSeconds={Configured} exceeds the upper bound; clamping to {Max}s. Fix the configuration value.",
                        config.GatewayTimeoutSeconds,
                        ClaudeConfig.MaxGatewayTimeoutSeconds);
            }

            c.BaseAddress = new Uri(config.GatewayBaseUrl);
            c.Timeout = TimeSpan.FromSeconds(config.ClampedGatewayTimeoutSeconds);
        });

        // Direct Anthropic typed client — 60s default timeout is intentional and unchanged.
        services.AddHttpClient<IClaudeService, ClaudeService>();
    }
}
