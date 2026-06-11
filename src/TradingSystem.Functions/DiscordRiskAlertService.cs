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
///
/// S5-003 (Default D5): the SAME instance also implements <see cref="IOperationalAlertService"/>
/// — operational (connect/orchestration failure) alerts ride the identical transport and
/// hardening, but render warning-orange without the risk-metrics fields block. Risk-stop
/// payloads and logs are byte-identical to pre-S5-003.
/// </summary>
public class DiscordRiskAlertService : IRiskAlertService, IOperationalAlertService
{
    // Host allow-list / redaction and 429 header parsing live in DiscordWebhookGuard (S4-003
    // refactor — shared verbatim with DiscordDailyReportService, behavior unchanged).

    // Retries are bounded by BOTH a retry count and a cumulative wait budget.
    private const int MaxRetries = 3;
    private const double MaxTotalWaitSeconds = 10.0;

    // Embed colors: risk stops keep the original red; operational alerts are warning-orange
    // (Default D5) so an ops failure is visually distinct from a capital-preservation stop.
    private const int RiskStopColor = 15158332;
    private const int OperationalColor = 15105570;

    // Terminal-drop guidance per alert kind. The risk wording is unchanged (a missed stop alert
    // demands manual verification); the operational wording is softer — an ops alert is not a
    // capital-preservation stop, the run outcome itself is already in the worker logs.
    // Application Insights is OFF by design (KD-006), so triage points at the local logs.
    private const string RiskDroppedNote = "verify the risk stop was acted on manually";
    private const string OperationalDroppedNote =
        @"check the run outcome in the worker console or %LOCALAPPDATA%\TradingSystem\logs";

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

        if (!_config.Enabled)
        {
            // ONE-TIME Information-level notice at construction: Program.cs sets the minimum log
            // level to Information, so without this the disabled state of a capital-preservation
            // alert path would be invisible in Application Insights (the per-cycle skip below is
            // intentionally Debug per B-006 to avoid per-cycle noise).
            _logger.LogInformation(
                "Discord risk alerts are disabled (Enabled=false); risk alerts will NOT be delivered until enabled.");
        }
    }

    /// <summary>
    /// S5-003 operational alert path (Default D5): same webhook/config/client/hardening as risk
    /// stops, warning-orange embed, no metrics fields. Best-effort like every send on this
    /// service — delivery failure degrades to a (softer) loud AlertDropped=true log, no throw.
    /// </summary>
    public Task SendOperationalAlertAsync(string title, string description, CancellationToken cancellationToken = default)
    {
        return SendAlertAsync(title, description, metrics: null, cancellationToken);
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

    // metrics == null selects the operational rendering (orange, no fields block); a non-null
    // metrics keeps the original risk-stop rendering byte-identical (red + metrics fields).
    private async Task SendAlertAsync(
        string title,
        string description,
        RiskMetrics? metrics,
        CancellationToken cancellationToken)
    {
        var kind = metrics is null ? "operational" : "risk";

        if (!_config.Enabled)
        {
            // Debug, not Information: this branch fires on every risk-check cycle while alerts
            // are disabled, so an Info-level entry per cycle is pure log noise (B-006).
            _logger.LogDebug("Discord {AlertKind} alerts are disabled; skipping alert: {Title}", kind, title);
            return;
        }

        if (string.IsNullOrWhiteSpace(_config.WebhookUrl))
        {
            _logger.LogWarning("Discord webhook URL is not configured; cannot send alert: {Title}", title);
            return;
        }

        // Host allow-list + scheme check. On rejection only a redacted {scheme}://{host} (or an
        // <empty>/<malformed> sentinel) is logged — never the token-bearing path.
        if (!DiscordWebhookGuard.TryValidateWebhook(_config.WebhookUrl, out var redacted))
        {
            _logger.LogWarning(
                "Discord webhook URL is not an allowed https Discord endpoint ({Redacted}); skipping alert: {Title}",
                redacted,
                title);
            return;
        }

        // Property order (title, description, color, timestamp[, fields]) matches the original
        // anonymous type so the serialized risk-stop payload stays byte-identical; operational
        // embeds replace the metrics fields block with the description (no RiskMetrics on that
        // path) and use the warning-orange color.
        object embed = metrics is null
            ? new
            {
                title,
                description,
                color = OperationalColor,
                timestamp = DateTime.UtcNow.ToString("O")
            }
            : new
            {
                title,
                description,
                color = RiskStopColor,
                timestamp = DateTime.UtcNow.ToString("O"),
                fields = new[]
                {
                    new { name = "Daily P&L", value = $"{metrics.DailyPnLPercent:P2}", inline = true },
                    new { name = "Weekly P&L", value = $"{metrics.WeeklyPnLPercent:P2}", inline = true },
                    new { name = "Drawdown", value = $"{metrics.CurrentDrawdown:P2}", inline = true },
                    new { name = "Open Positions", value = metrics.OpenPositionCount.ToString(), inline = true }
                }
            };

        var payload = new
        {
            username = _config.Username,
            // Disable mention parsing so alert content containing @everyone/@here can't ping.
            allowed_mentions = new { parse = Array.Empty<string>() },
            embeds = new[] { embed }
        };

        await PostWithRetryAsync(
            payload,
            title,
            kind,
            metrics is null ? "Operational" : "Risk",
            metrics is null ? OperationalDroppedNote : RiskDroppedNote,
            cancellationToken);
    }

    // POSTs the payload, honoring a 429 Retry-After with a bounded backoff. Degrades to a scrubbed
    // log and returns (no throw) on non-2xx exhaustion or a transport failure, per ADR-025. Status
    // code / exception type only is logged — never the webhook URL or token. The kind/droppedNote
    // parameters keep the rendered risk-path log lines byte-identical to pre-S5-003 while giving
    // operational drops the softer (but still AlertDropped=true loud) wording.
    private async Task PostWithRetryAsync(
        object payload,
        string title,
        string kind,
        string kindCapitalized,
        string droppedNote,
        CancellationToken cancellationToken)
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
                    _logger.LogInformation("Sent Discord {AlertKind} alert: {Title}", kind, title);
                    return;
                }

                if ((int)response.StatusCode != 429 || attempt >= MaxRetries)
                {
                    // Terminal failure: a non-429 4xx/5xx, or a 429 that has exhausted the retry
                    // count. The alert is gone and will NOT be retried — make this loud and
                    // log-searchable (AlertDropped=true) so a missed STOP/HALT alert surfaces.
                    _logger.LogError(
                        "{AlertKind} alert NOT delivered and will not be retried — {DroppedNote}. AlertDropped={AlertDropped}, StatusCode={StatusCode}, Attempts={Attempt}/{MaxRetries}, Alert={Title}",
                        kindCapitalized,
                        droppedNote,
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
                        "{AlertKind} alert NOT delivered and will not be retried — retry budget exhausted; {DroppedNote}. AlertDropped={AlertDropped}, StatusCode={StatusCode}, Attempts={Attempt}/{MaxRetries}, Alert={Title}",
                        kindCapitalized,
                        droppedNote,
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
                var wait = Math.Max(0, Math.Min(DiscordWebhookGuard.RetryAfterSeconds(response), remaining));

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
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient timeout (the caller did NOT cancel): the alert never reached Discord
                // and will NOT be retried — same loud, searchable AlertDropped=true terminal shape
                // as a transport error. A timeout must never escape the no-throw alert contract
                // (ADR-025). Exception type names only — never ex.Message, which can echo the
                // token-bearing request URI.
                _logger.LogError(
                    "{AlertKind} alert NOT delivered and will not be retried — send timed out; {DroppedNote}. AlertDropped={AlertDropped}, ErrorType={ErrorType}, InnerErrorType={InnerErrorType}, Alert={Title}",
                    kindCapitalized,
                    droppedNote,
                    true,
                    ex.GetType().Name,
                    ex.InnerException?.GetType().Name,
                    title);
                return;
            }
            catch (HttpRequestException ex)
            {
                // Transport failure: the alert never reached Discord and will NOT be retried —
                // make it loud and searchable (AlertDropped=true). Log only token-free diagnostics:
                // exception type, the nullable HTTP status, and the inner exception type. NEVER log
                // ex.Message / ex.ToString() — those can echo the token-bearing request URI.
                _logger.LogError(
                    "{AlertKind} alert NOT delivered and will not be retried — transport error; {DroppedNote}. AlertDropped={AlertDropped}, ErrorType={ErrorType}, StatusCode={StatusCode}, InnerErrorType={InnerErrorType}, Alert={Title}",
                    kindCapitalized,
                    droppedNote,
                    true,
                    ex.GetType().Name,
                    ex.StatusCode,
                    ex.InnerException?.GetType().Name,
                    title);
                return;
            }
        }
    }

}
