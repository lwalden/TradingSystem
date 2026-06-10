using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;

namespace TradingSystem.Functions;

/// <summary>
/// Sends risk-stop alerts to the existing active bots-repo Discord webhook channel using the
/// raw-webhook pattern ported from <c>bots/core/notify/discord.py</c> (S3-004): an https-only host
/// allow-list (<c>discord.com</c>/<c>discordapp.com</c> or a subdomain), webhook-token redaction in
/// logs (only <c>{scheme}://{host}</c> or an <c>&lt;empty&gt;</c>/<c>&lt;malformed&gt;</c> sentinel
/// ever appears; status code / exception type only on failure), and a bounded 429/Retry-After retry
/// (Retry-After → X-RateLimit-Reset-After → base backoff, per-wait clamp 60s, bounded by
/// <see cref="MaxRetries"/> and a cumulative <see cref="MaxTotalWaitSeconds"/> budget; exhaustion or
/// transport failure degrades to a scrubbed log and returns — no throw — per ADR-025). The delay is
/// injectable so tests run zero-wait. The webhook URL stays config/Key-Vault sourced, never
/// hard-coded.
/// </summary>
public class DiscordRiskAlertService : IRiskAlertService
{
    // Webhooks must be on Discord's domain over https; the path segment carries the secret token.
    private static readonly string[] AllowedWebhookHosts = { "discord.com", "discordapp.com" };

    // Fallback backoff (seconds) when a 429 carries no usable Retry-After / reset header.
    private const double BaseBackoffSeconds = 1.0;

    // Sane upper bound on a single 429 wait, so a hostile/huge Retry-After can never request an
    // unbounded sleep even if the cumulative budget were misconfigured high.
    private const double MaxRetryAfterSeconds = 60.0;

    // Retries are bounded by BOTH a retry count and a cumulative wait budget.
    private const int MaxRetries = 3;
    private const double MaxTotalWaitSeconds = 10.0;

    private readonly HttpClient _httpClient;
    private readonly DiscordConfig _config;
    private readonly ILogger<DiscordRiskAlertService> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public DiscordRiskAlertService(
        IHttpClientFactory httpClientFactory,
        IOptions<DiscordConfig> config,
        ILogger<DiscordRiskAlertService> logger)
        : this(httpClientFactory, config, logger, Task.Delay)
    {
    }

    // Internal ctor: the delay is injectable so tests run with zero real wait. Production uses
    // Task.Delay via the public ctor above.
    internal DiscordRiskAlertService(
        IHttpClientFactory httpClientFactory,
        IOptions<DiscordConfig> config,
        ILogger<DiscordRiskAlertService> logger,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _httpClient = httpClientFactory.CreateClient("DiscordRiskAlerts");
        _config = config.Value;
        _logger = logger;
        _delay = delay;
    }

    public Task SendDailyStopTriggeredAsync(RiskMetrics metrics, CancellationToken cancellationToken = default)
    {
        return SendAlertAsync(
            "Daily Risk Stop Triggered",
            $"Daily P&L {metrics.DailyPnLPercent:P2} breached stop threshold.",
            metrics,
            cancellationToken);
    }

    public Task SendWeeklyStopTriggeredAsync(RiskMetrics metrics, CancellationToken cancellationToken = default)
    {
        return SendAlertAsync(
            "Weekly Risk Stop Triggered",
            $"Weekly P&L {metrics.WeeklyPnLPercent:P2} breached stop threshold.",
            metrics,
            cancellationToken);
    }

    public Task SendDrawdownHaltTriggeredAsync(RiskMetrics metrics, CancellationToken cancellationToken = default)
    {
        return SendAlertAsync(
            "Drawdown Halt Triggered",
            $"Current drawdown {metrics.CurrentDrawdown:P2} breached drawdown halt threshold.",
            metrics,
            cancellationToken);
    }

    private async Task SendAlertAsync(
        string title,
        string description,
        RiskMetrics metrics,
        CancellationToken cancellationToken)
    {
        if (!_config.Enabled)
        {
            // Debug, not Information: this branch fires on every risk-check cycle while alerts
            // are disabled, so an Info-level entry per cycle is pure log noise (B-006).
            _logger.LogDebug("Discord risk alerts are disabled; skipping alert: {Title}", title);
            return;
        }

        if (string.IsNullOrWhiteSpace(_config.WebhookUrl))
        {
            _logger.LogWarning("Discord webhook URL is not configured; cannot send alert: {Title}", title);
            return;
        }

        // Host allow-list + scheme check. On rejection only a redacted {scheme}://{host} (or an
        // <empty>/<malformed> sentinel) is logged — never the token-bearing path.
        if (!TryValidateWebhook(_config.WebhookUrl, out var redacted))
        {
            _logger.LogWarning(
                "Discord webhook URL is not an allowed https Discord endpoint ({Redacted}); skipping alert: {Title}",
                redacted,
                title);
            return;
        }

        var payload = new
        {
            username = _config.Username,
            // Disable mention parsing so alert content containing @everyone/@here can't ping.
            allowed_mentions = new { parse = Array.Empty<string>() },
            embeds = new[]
            {
                new
                {
                    title,
                    description,
                    color = 15158332,
                    timestamp = DateTime.UtcNow.ToString("O"),
                    fields = new[]
                    {
                        new { name = "Daily P&L", value = $"{metrics.DailyPnLPercent:P2}", inline = true },
                        new { name = "Weekly P&L", value = $"{metrics.WeeklyPnLPercent:P2}", inline = true },
                        new { name = "Drawdown", value = $"{metrics.CurrentDrawdown:P2}", inline = true },
                        new { name = "Open Positions", value = metrics.OpenPositionCount.ToString(), inline = true }
                    }
                }
            }
        };

        await PostWithRetryAsync(payload, title, cancellationToken);
    }

    // POSTs the payload, honoring a 429 Retry-After with a bounded backoff. Degrades to a scrubbed
    // log and returns (no throw) on non-2xx exhaustion or a transport failure, per ADR-025. Status
    // code / exception type only is logged — never the webhook URL or token.
    private async Task PostWithRetryAsync(object payload, string title, CancellationToken cancellationToken)
    {
        var start = DateTime.UtcNow;
        var attempt = 0;
        while (true)
        {
            try
            {
                using var response = await _httpClient.PostAsJsonAsync(_config.WebhookUrl, payload, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Sent Discord risk alert: {Title}", title);
                    return;
                }

                if ((int)response.StatusCode != 429 || attempt >= MaxRetries)
                {
                    // Terminal failure: a non-429 4xx/5xx, or a 429 that has exhausted the retry
                    // count. The alert is gone and will NOT be retried — make this loud and
                    // log-searchable (AlertDropped=true) so a missed STOP/HALT alert surfaces.
                    _logger.LogError(
                        "Risk alert NOT delivered and will not be retried — verify the risk stop was acted on manually. AlertDropped={AlertDropped}, StatusCode={StatusCode}, Attempts={Attempt}/{MaxRetries}, Alert={Title}",
                        true,
                        (int)response.StatusCode,
                        attempt + 1,
                        MaxRetries,
                        title);
                    return;
                }

                var remaining = MaxTotalWaitSeconds - (DateTime.UtcNow - start).TotalSeconds;
                if (remaining <= 0)
                {
                    // Cumulative wait budget exhausted before the retry count was. Same outcome:
                    // the alert is dropped permanently — emit the loud, searchable signal.
                    _logger.LogError(
                        "Risk alert NOT delivered and will not be retried — retry budget exhausted; verify the risk stop was acted on manually. AlertDropped={AlertDropped}, StatusCode={StatusCode}, Attempts={Attempt}/{MaxRetries}, Alert={Title}",
                        true,
                        (int)response.StatusCode,
                        attempt + 1,
                        MaxRetries,
                        title);
                    return;
                }

                // remaining > 0 is guaranteed here (we returned above otherwise). A Retry-After of
                // 0 is a valid "retry now", so a zero wait still makes progress; keep it
                // non-negative and proceed to retry.
                var wait = Math.Max(0, Math.Min(RetryAfterSeconds(response), remaining));

                _logger.LogWarning(
                    "Discord rate-limited (429); retrying alert {Title} after {WaitSeconds:F2}s (attempt {Attempt}/{Max})",
                    title,
                    wait,
                    attempt + 1,
                    MaxRetries);
                await _delay(TimeSpan.FromSeconds(wait), cancellationToken);
                attempt++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Caller cancelled — propagate cancellation, it carries no token.
                throw;
            }
            catch (HttpRequestException ex)
            {
                // Transport failure: the alert never reached Discord and will NOT be retried —
                // make it loud and searchable (AlertDropped=true). Log only token-free diagnostics:
                // exception type, the nullable HTTP status, and the inner exception type. NEVER log
                // ex.Message / ex.ToString() — those can echo the token-bearing request URI.
                _logger.LogError(
                    "Risk alert NOT delivered and will not be retried — transport error; verify the risk stop was acted on manually. AlertDropped={AlertDropped}, ErrorType={ErrorType}, StatusCode={StatusCode}, InnerErrorType={InnerErrorType}, Alert={Title}",
                    true,
                    ex.GetType().Name,
                    ex.StatusCode,
                    ex.InnerException?.GetType().Name,
                    title);
                return;
            }
        }
    }

    // Validates the webhook URL against the host allow-list and https scheme. On success
    // <paramref name="redactedForLog"/> is {scheme}://{host}; on failure it is the same redacted
    // form (or an <empty>/<malformed> sentinel) so the token in the path can never reach a log.
    private static bool TryValidateWebhook(string url, out string redactedForLog)
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

    // Seconds to wait for a 429, tolerant of missing/garbage headers. Prefers Retry-After
    // (seconds), falls back to Discord's X-RateLimit-Reset-After (float seconds), then the base
    // backoff. Any unparseable value degrades to the base backoff. The result is clamped to
    // MaxRetryAfterSeconds so an adversarial/huge header can never request an unbounded wait.
    private static double RetryAfterSeconds(HttpResponseMessage response)
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
