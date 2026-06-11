using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Functions;

/// <summary>
/// S6-001 (Default D6): renders the monthly income reinvest plan as a digest-class NEUTRAL
/// embed — deliberately NOT risk red or operational orange, because this message is routine
/// evidence, not an alarm. Same webhook/config as the other Discord senders (no new secret)
/// but its OWN named client ("DiscordIncomeReport", 8s timeout in Program.cs) so the senders'
/// handlers/telemetry stay distinguishable. Reuses the S3-004 hardening verbatim via
/// <see cref="DiscordWebhookGuard"/>: https-only Discord host allow-list, token-redacted
/// logging ({scheme}://{host} only), bounded 429/Retry-After retry with an injectable delay
/// (zero-wait in tests), and the Enabled==false skip (one-time ctor Information notice,
/// per-call Debug skip — B-006 pattern).
///
/// Content contract: the posture line (recommendation-only vs orders-placed — locked
/// decision 1), the D4 available-cash input labeled as an estimate, sleeve total value,
/// per-category drift, and the proposed buys (capped render). An empty plan renders an
/// explicit "No buys proposed" line (Default D8 — proof the timer ran). No secrets and no
/// exception messages ever appear in any embed or log. Delivery failure degrades to a loud
/// AlertDropped=true log and returns — a missed report is never an operational stop.
/// </summary>
public class DiscordIncomeReportService : IIncomeReportService
{
    // Retries are bounded by BOTH a retry count and a cumulative wait budget (S3-004 pattern).
    private const int MaxRetries = 3;
    private const double MaxTotalWaitSeconds = 10.0;

    // Cap on rendered buy lines (Discord field limit is 1024 chars; the report is a digest).
    private const int MaxRenderedBuys = 10;

    // Digest-class neutral grey — never risk red (15158332) or ops orange (15105570).
    private const int NeutralColor = 9807270;

    private readonly HttpClient _httpClient;
    private readonly DiscordConfig _config;
    private readonly IncomeSleeveConfig _sleeveConfig;
    private readonly ILogger<DiscordIncomeReportService> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public DiscordIncomeReportService(
        IHttpClientFactory httpClientFactory,
        IOptions<DiscordConfig> config,
        IOptions<IncomeSleeveConfig> sleeveConfig,
        ILogger<DiscordIncomeReportService> logger)
        : this(httpClientFactory, config, sleeveConfig, logger, Task.Delay)
    {
    }

    // Internal ctor: the delay is injectable so tests run with zero real wait.
    internal DiscordIncomeReportService(
        IHttpClientFactory httpClientFactory,
        IOptions<DiscordConfig> config,
        IOptions<IncomeSleeveConfig> sleeveConfig,
        ILogger<DiscordIncomeReportService> logger,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _httpClient = httpClientFactory.CreateClient("DiscordIncomeReport");
        _config = config.Value;
        _sleeveConfig = sleeveConfig.Value;
        _logger = logger;
        _delay = delay;

        if (!_config.Enabled)
        {
            // ONE-TIME Information-level notice at construction (B-006 pattern): without this
            // the disabled state of the reinvest report path would be invisible at the
            // Information minimum level; the per-call skip stays at Debug.
            _logger.LogInformation(
                "Discord income reinvest reports are disabled (Enabled=false); reinvest plan reports will NOT be delivered until enabled.");
        }
    }

    public async Task SendReinvestmentPlanReportAsync(
        ReinvestmentPlan plan,
        IncomeSleeveState state,
        int ordersPlaced,
        CancellationToken cancellationToken = default)
    {
        var day = plan.PlanDate.Date;

        if (!_config.Enabled)
        {
            _logger.LogDebug(
                "Discord income reinvest reports are disabled; skipping report for {Date}", day);
            return;
        }

        if (string.IsNullOrWhiteSpace(_config.WebhookUrl))
        {
            _logger.LogWarning(
                "Discord webhook URL is not configured; cannot send income reinvest report for {Date}", day);
            return;
        }

        // Host allow-list + scheme check. On rejection only a redacted {scheme}://{host} (or
        // an <empty>/<malformed> sentinel) is logged — never the token-bearing path.
        if (!DiscordWebhookGuard.TryValidateWebhook(_config.WebhookUrl, out var redacted))
        {
            _logger.LogWarning(
                "Discord webhook URL is not an allowed https Discord endpoint ({Redacted}); skipping income reinvest report for {Date}",
                redacted,
                day);
            return;
        }

        var payload = new
        {
            username = _config.Username,
            // Disable mention parsing so report content can never ping @everyone/@here.
            allowed_mentions = new { parse = Array.Empty<string>() },
            embeds = new[] { BuildPlanEmbed(plan, state, ordersPlaced) }
        };

        await PostWithRetryAsync(payload, day, cancellationToken);
    }

    private Embed BuildPlanEmbed(ReinvestmentPlan plan, IncomeSleeveState state, int ordersPlaced)
    {
        // Posture leads (locked decision 1): the owner's first read must answer "did this
        // place orders?". The exact flag key renders so the runbook check is unambiguous.
        var posture = _sleeveConfig.OrderPlacementEnabled
            ? $"Order placement ENABLED — {ordersPlaced.ToString(CultureInfo.InvariantCulture)} paper order(s) placed (IncomeSleeve:OrderPlacementEnabled=true)."
            : "Recommendation-only; IncomeSleeve:OrderPlacementEnabled=false. NO orders were placed.";

        var fields = new List<EmbedField>
        {
            new("Posture", posture, false),
            // Default D4: the cash input is a heuristic estimate (TotalCashValue ×
            // IncomeTargetPercent) — labeled so nobody mistakes it for dividend tracking.
            new("Available Cash (estimate)",
                $"{Money(plan.AvailableCash)} — TotalCashValue × IncomeTargetPercent (estimate; dividend/interest tracking not yet wired)",
                true),
            new("Sleeve Total Value", Money(state.TotalValue), true)
        };

        if (state.CategoryDrift.Count > 0)
        {
            var driftLines = state.CategoryDrift
                .OrderBy(kv => kv.Value)
                .Select(kv => $"{kv.Key}: {kv.Value.ToString("+0.0%;-0.0%;0.0%", CultureInfo.InvariantCulture)}");
            fields.Add(new EmbedField("Category Drift (current − target)",
                string.Join(Environment.NewLine, driftLines), false));
        }

        if (plan.ProposedBuys.Count == 0)
        {
            // Default D8: an empty plan is a legitimate outcome (e.g. an unfunded sleeve) —
            // say so explicitly; this message existing at all is proof the timer ran.
            fields.Add(new EmbedField("Proposed Buys", "No buys proposed.", false));
        }
        else
        {
            var lines = plan.ProposedBuys
                .Take(MaxRenderedBuys)
                .Select(b => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{b.Symbol} ({b.Category}): {Money(b.Amount)} — {b.Rationale}"));
            var overflow = plan.ProposedBuys.Count > MaxRenderedBuys
                ? $"{Environment.NewLine}... and {(plan.ProposedBuys.Count - MaxRenderedBuys).ToString(CultureInfo.InvariantCulture)} more"
                : string.Empty;
            fields.Add(new EmbedField("Proposed Buys",
                string.Join(Environment.NewLine, lines) + overflow, false));
        }

        var description = plan.ProposedBuys.Count == 0
            ? "No buys proposed — sleeve is unfunded or fully on-target."
            : $"{plan.ProposedBuys.Count.ToString(CultureInfo.InvariantCulture)} proposed buy(s) totaling {Money(plan.TotalProposedAmount)}.";

        return new Embed(
            title: $"Income Reinvest Plan — {plan.PlanDate.ToString("ddd MMM d, yyyy", CultureInfo.InvariantCulture)}",
            description: description,
            color: NeutralColor,
            timestamp: plan.PlanDate.Date.ToString("O", CultureInfo.InvariantCulture),
            fields: fields.ToArray());
    }

    // POSTs the payload, honoring a 429 Retry-After with a bounded backoff (count + cumulative
    // wait budget). Degrades to a loud AlertDropped=true log and returns (no throw) on non-2xx
    // exhaustion, timeout, or transport failure. Status code / exception TYPE only is logged —
    // never the webhook URL, token, or an exception message (which can echo the request URI).
    private async Task PostWithRetryAsync(object payload, DateTime day, CancellationToken cancellationToken)
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
                    _logger.LogInformation("Sent Discord income reinvest report for {Date}", day);
                    return;
                }

                var remaining = MaxTotalWaitSeconds - (DateTime.UtcNow - start).TotalSeconds;
                if ((int)response.StatusCode != 429 || attempt >= MaxRetries || remaining <= 0)
                {
                    // Terminal failure: the report is gone and will NOT be retried — make it
                    // loud and log-searchable (AlertDropped=true) so the 2026-07-01 run-record
                    // check ("did the plan report arrive?") has a definitive negative signal.
                    _logger.LogError(
                        "Income reinvest report NOT delivered and will not be retried. AlertDropped={AlertDropped}, StatusCode={StatusCode}, Attempts={Attempt}/{MaxRetries}, Date={Date}",
                        true,
                        (int)response.StatusCode,
                        attempt + 1,
                        MaxRetries,
                        day);
                    return;
                }

                var wait = Math.Max(0, Math.Min(DiscordWebhookGuard.RetryAfterSeconds(response), remaining));
                _logger.LogWarning(
                    "Discord rate-limited (429); retrying income reinvest report for {Date} after {WaitSeconds:F2}s (attempt {Attempt}/{Max})",
                    day,
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
                // HttpClient timeout (the 8s named-client bound): the report never reached
                // Discord and will NOT be retried. Type names only.
                _logger.LogError(
                    "Income reinvest report NOT delivered and will not be retried — send timed out. AlertDropped={AlertDropped}, ErrorType={ErrorType}, InnerErrorType={InnerErrorType}, Date={Date}",
                    true,
                    ex.GetType().Name,
                    ex.InnerException?.GetType().Name,
                    day);
                return;
            }
            catch (HttpRequestException ex)
            {
                // Transport failure. NEVER log ex.Message / ex.ToString() — those can echo
                // the token-bearing request URI.
                _logger.LogError(
                    "Income reinvest report NOT delivered and will not be retried — transport error. AlertDropped={AlertDropped}, ErrorType={ErrorType}, StatusCode={StatusCode}, InnerErrorType={InnerErrorType}, Date={Date}",
                    true,
                    ex.GetType().Name,
                    ex.StatusCode,
                    ex.InnerException?.GetType().Name,
                    day);
                return;
            }
        }
    }

    private static string Money(decimal value) =>
        value < 0
            ? string.Create(CultureInfo.InvariantCulture, $"-${Math.Abs(value):N2}")
            : string.Create(CultureInfo.InvariantCulture, $"${value:N2}");

    // Discord embed shapes (lowercase property names match the webhook JSON contract).
    private sealed record Embed(
        string title,
        string description,
        int color,
        string timestamp,
        EmbedField[] fields);

    private sealed record EmbedField(string name, string value, bool inline);
}
