using System.Globalization;

namespace TradingSystem.Functions;

/// <summary>
/// Shared S3-004 webhook hardening primitives used by every Discord webhook sender
/// (<see cref="DiscordRiskAlertService"/>, <see cref="DiscordDailyReportService"/>): the
/// https-only Discord host allow-list with token redaction, and the tolerant 429
/// Retry-After/X-RateLimit-Reset-After parser with a per-wait clamp. Pure functions only —
/// the retry LOOP (and its service-specific logging semantics) stays in each service so a
/// dropped risk alert can remain louder than a dropped report.
/// </summary>
internal static class DiscordWebhookGuard
{
    // Webhooks must be on Discord's domain over https; the path segment carries the secret token.
    private static readonly string[] AllowedWebhookHosts = { "discord.com", "discordapp.com" };

    // Fallback backoff (seconds) when a 429 carries no usable Retry-After / reset header.
    private const double BaseBackoffSeconds = 1.0;

    // Sane upper bound on a single 429 wait, so a hostile/huge Retry-After can never request an
    // unbounded sleep even if a cumulative budget were misconfigured high.
    private const double MaxRetryAfterSeconds = 60.0;

    /// <summary>
    /// Validates the webhook URL against the host allow-list and https scheme. On success
    /// <paramref name="redactedForLog"/> is {scheme}://{host}; on failure it is the same
    /// redacted form (or an &lt;empty&gt;/&lt;malformed&gt; sentinel) so the token in the path
    /// can never reach a log.
    /// </summary>
    public static bool TryValidateWebhook(string url, out string redactedForLog)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            redactedForLog = "<empty>";
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            redactedForLog = "<malformed>";
            return false;
        }

        // Redact to scheme+host only — the path segment carries the webhook secret token.
        redactedForLog = $"{uri.Scheme}://{uri.Host}";

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.Host;
        foreach (var allowed in AllowedWebhookHosts)
        {
            if (string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Seconds to wait for a 429, tolerant of missing/garbage headers. Prefers Retry-After
    /// (seconds), falls back to Discord's X-RateLimit-Reset-After (float seconds), then the base
    /// backoff. Any unparseable value degrades to the base backoff. The result is clamped to
    /// <see cref="MaxRetryAfterSeconds"/> so an adversarial/huge header can never request an
    /// unbounded wait.
    /// </summary>
    public static double RetryAfterSeconds(HttpResponseMessage response)
    {
        foreach (var name in new[] { "Retry-After", "X-RateLimit-Reset-After" })
        {
            if (!response.Headers.TryGetValues(name, out var values))
            {
                continue;
            }

            var raw = values.FirstOrDefault();
            if (raw == null)
            {
                continue;
            }

            if (double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                !double.IsNaN(value) &&
                value >= 0)
            {
                return Math.Min(value, MaxRetryAfterSeconds);
            }
        }

        return BaseBackoffSeconds;
    }
}
