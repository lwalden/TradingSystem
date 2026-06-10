using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Functions;

/// <summary>
/// Week-10 rich daily digest to the same Discord webhook channel the risk alerts use
/// (S4-003 / B-004) — same <see cref="DiscordConfig"/>, no new secret. Reuses the S3-004
/// hardening verbatim via <see cref="DiscordWebhookGuard"/>: https-only Discord host
/// allow-list, token-redacted logging ({scheme}://{host} only), bounded 429/Retry-After
/// retry with an injectable delay (zero-wait in tests), and the Enabled==false skip
/// (one-time ctor Information notice, per-call Debug skip — B-006 pattern).
///
/// The report renders today's <see cref="DailySnapshot"/> plus the day's
/// <see cref="ITradeRepository"/> fills: executed trades, realized/unrealized P&amp;L, open
/// positions, and market regime. Per ADR-023 the cost section carries DISTINCT platform
/// (Azure+Polygon+Claude — tracked against the $100/mo ceiling outside snapshots) and
/// brokerage (snapshot commissions + activity-based forecast) fields — never conflated.
/// When the optional S4-002 <see cref="ISleeveReadinessScorecardService"/> is wired, a
/// readiness embed is appended on the weekly cadence day only (<see cref="ReportingConfig"/>,
/// default Friday — S5-002, best-effort: its failure never drops the core report);
/// an undefined profit factor renders as "∞ (no losses)", never the 999 sentinel.
/// Delivery failure degrades to scrubbed logs and returns — a missed report is never an
/// operational stop (contrast: dropped risk alerts log the louder AlertDropped signal).
/// </summary>
public class DiscordDailyReportService : IDailyReportService
{
    // Retries are bounded by BOTH a retry count and a cumulative wait budget (S3-004 pattern).
    private const int MaxRetries = 3;
    private const double MaxTotalWaitSeconds = 10.0;

    // Assumed trading days per month for the naive activity-based brokerage forecast (ADR-023).
    private const int TradingDaysPerMonth = 21;

    // Cap on individual fill lines rendered into the trades embed (Discord field limit is 1024
    // chars; the digest is a summary, not a full journal).
    private const int MaxRenderedFills = 10;

    private readonly HttpClient _httpClient;
    private readonly DiscordConfig _config;
    private readonly ISnapshotRepository _snapshotRepository;
    private readonly ITradeRepository _tradeRepository;
    private readonly ILogger<DiscordDailyReportService> _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ISleeveReadinessScorecardService? _scorecardService;
    private readonly ReportingConfig _reporting;

    public DiscordDailyReportService(
        IHttpClientFactory httpClientFactory,
        IOptions<DiscordConfig> config,
        ISnapshotRepository snapshotRepository,
        ITradeRepository tradeRepository,
        ILogger<DiscordDailyReportService> logger,
        ISleeveReadinessScorecardService? scorecardService = null,
        IOptions<ReportingConfig>? reportingConfig = null)
        : this(httpClientFactory, config, snapshotRepository, tradeRepository, logger,
               Task.Delay, scorecardService, reportingConfig)
    {
    }

    // Internal ctor: the delay is injectable so tests run with zero real wait. Production uses
    // Task.Delay via the public ctor above.
    internal DiscordDailyReportService(
        IHttpClientFactory httpClientFactory,
        IOptions<DiscordConfig> config,
        ISnapshotRepository snapshotRepository,
        ITradeRepository tradeRepository,
        ILogger<DiscordDailyReportService> logger,
        Func<TimeSpan, CancellationToken, Task> delay,
        ISleeveReadinessScorecardService? scorecardService = null,
        IOptions<ReportingConfig>? reportingConfig = null)
    {
        _httpClient = httpClientFactory.CreateClient("DiscordDailyReport");
        _config = config.Value;
        _snapshotRepository = snapshotRepository;
        _tradeRepository = tradeRepository;
        _logger = logger;
        _delay = delay;
        _scorecardService = scorecardService;
        // S5-002 (Default D7): optional so existing construction sites compile; unconfigured
        // deployments get the locked default (Friday scorecard day).
        _reporting = reportingConfig?.Value ?? new ReportingConfig();

        if (!_config.Enabled)
        {
            // ONE-TIME Information-level notice at construction (B-006 / review S4-005 pattern):
            // Program.cs sets the minimum log level to Information, so without this the disabled
            // state of the reporting path would be invisible; the per-call skip stays at Debug.
            _logger.LogInformation(
                "Discord daily reports are disabled (Enabled=false); daily reports will NOT be delivered until enabled.");
        }
    }

    public async Task SendDailyReportAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
        {
            // Debug, not Information: fires on every scheduled run while reporting is disabled
            // (B-006 — the one-time ctor notice above is the durable signal).
            _logger.LogDebug("Discord daily reports are disabled; skipping report for {Date}", date.Date);
            return;
        }

        if (string.IsNullOrWhiteSpace(_config.WebhookUrl))
        {
            _logger.LogWarning("Discord webhook URL is not configured; cannot send daily report for {Date}", date.Date);
            return;
        }

        // Host allow-list + scheme check. On rejection only a redacted {scheme}://{host} (or an
        // <empty>/<malformed> sentinel) is logged — never the token-bearing path.
        if (!DiscordWebhookGuard.TryValidateWebhook(_config.WebhookUrl, out var redacted))
        {
            _logger.LogWarning(
                "Discord webhook URL is not an allowed https Discord endpoint ({Redacted}); skipping daily report for {Date}",
                redacted,
                date.Date);
            return;
        }

        var day = date.Date;
        var snapshot = await _snapshotRepository.GetSnapshotAsync(day, cancellationToken);
        if (snapshot == null)
        {
            _logger.LogWarning("No daily snapshot found for {Date}; skipping daily report", day);
            return;
        }

        var trades = await _tradeRepository.GetByDateRangeAsync(day, day.AddDays(1), cancellationToken)
                     ?? new List<Trade>();

        var embeds = new List<Embed>
        {
            BuildSummaryEmbed(day, snapshot, trades)
        };

        // S5-002 weekly cadence (Default D7, locked decision 5): the readiness scorecard is a
        // weekly section — appended only on the configured day (default Friday). On every
        // other day the scorecard service is not even consulted; the core digest is unchanged.
        if (day.DayOfWeek == _reporting.WeeklyScorecardDay)
        {
            var readiness = await TryBuildReadinessEmbedAsync(day, cancellationToken);
            if (readiness != null)
            {
                embeds.Add(readiness);
            }
        }

        var payload = new
        {
            username = _config.Username,
            // Disable mention parsing so report content containing @everyone/@here can't ping.
            allowed_mentions = new { parse = Array.Empty<string>() },
            embeds = embeds.ToArray()
        };

        await PostWithRetryAsync(payload, day, cancellationToken);
    }

    private static Embed BuildSummaryEmbed(DateTime day, DailySnapshot snapshot, List<Trade> trades)
    {
        // Mobile-first field order (review S4-003): the day's NEWS leads (P&L, regime), then
        // detail, then costs; net liquidation is context — it renders LAST (added below, after
        // the optional fills field, so it stays last on trade days too).
        var fields = new List<EmbedField>
        {
            new("Daily P&L", $"{Money(snapshot.DailyPnL)} ({Percent(snapshot.DailyPnLPercent)})", true),
            new("Market Regime", snapshot.MarketRegime.ToString(), true),
            new("Realized P&L", Money(snapshot.RealizedPnL), true),
            new("Unrealized P&L", Money(snapshot.UnrealizedPnL), true),
            new("Trades Executed", trades.Count.ToString(CultureInfo.InvariantCulture), true),
            new("Open Positions", snapshot.OpenPositions.ToString(CultureInfo.InvariantCulture), true),
            // ADR-023: platform and brokerage costs are DISTINCT fields — never conflated.
            // Platform spend (Azure+Polygon+Claude) is tracked against the ceiling outside the
            // snapshot store, so this field carries the scope/ceiling, not a fabricated number.
            new("Platform Costs", "Tracked separately (≤ $100/mo ceiling, ADR-023)", false),
            new("Brokerage Costs",
                $"Commissions today: {Money(snapshot.CommissionsPaid)} | " +
                $"Monthly forecast ({TradingDaysPerMonth} trading days at today's pace): ~{Money(snapshot.CommissionsPaid * TradingDaysPerMonth)}",
                false)
        };

        if (trades.Count > 0)
        {
            var lines = trades
                .Take(MaxRenderedFills)
                .Select(t =>
                {
                    var line = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{t.Action} {t.Quantity:0.##} {t.Symbol} @ {t.EntryPrice:0.00}");
                    return t.RealizedPnL.HasValue ? $"{line} (P&L {Money(t.RealizedPnL.Value)})" : line;
                });
            var overflow = trades.Count > MaxRenderedFills
                ? $"{Environment.NewLine}... and {trades.Count - MaxRenderedFills} more"
                : string.Empty;
            fields.Add(new EmbedField("Fills", string.Join(Environment.NewLine, lines) + overflow, false));
        }

        // Context, not news — deliberately the LAST field (after the optional fills above).
        fields.Add(new EmbedField("Net Liquidation", Money(snapshot.NetLiquidationValue), true));

        // First-read mobile text: the description carries the day's signal (direction, magnitude,
        // activity) before any field renders. The Up/Down word carries the sign, so both the
        // dollar amount and the percent render as magnitudes.
        var direction = snapshot.DailyPnL >= 0 ? "Up" : "Down";
        var outcome = $"{direction} {Money(Math.Abs(snapshot.DailyPnL))} ({Percent(Math.Abs(snapshot.DailyPnLPercent))})";

        return new Embed(
            title: $"Daily Report — {day.ToString("ddd MMM d, yyyy", CultureInfo.InvariantCulture)}",
            description: trades.Count == 0
                ? $"{outcome} — no trades — regime: {snapshot.MarketRegime}."
                : $"{outcome} — {trades.Count.ToString(CultureInfo.InvariantCulture)} trade(s)",
            color: snapshot.DailyPnL >= 0 ? 3066993 /* green */ : 15158332 /* red */,
            // The report is ABOUT this trading day — stamping the day (not wall-clock now) keeps
            // the payload deterministic for a given snapshot/trade set.
            timestamp: day.ToString("O", CultureInfo.InvariantCulture),
            fields: fields.ToArray());
    }

    // Best-effort readiness section (optional S4-002 dependency): a scorecard failure must never
    // drop the core daily report, so errors degrade to a warning and the section is omitted.
    private async Task<Embed?> TryBuildReadinessEmbedAsync(DateTime day, CancellationToken cancellationToken)
    {
        if (_scorecardService == null)
        {
            return null;
        }

        try
        {
            var scorecards = await _scorecardService.GenerateAsync(day, cancellationToken);
            if (scorecards == null || scorecards.Count == 0)
            {
                return null;
            }

            var fields = scorecards.Select(sc => new EmbedField(
                $"{sc.SleeveName} — {RenderReadiness(sc.Readiness)}",
                // An undefined profit factor (no losing trades) renders as "∞ (no losses)" —
                // NEVER the raw gate-passing sentinel (S4-002 renderer contract).
                $"PF: {RenderProfitFactor(sc)} | Confidence: {Percent(sc.Confidence)} | " +
                $"Closed trades: {sc.ClosedTradeCount.ToString(CultureInfo.InvariantCulture)}" +
                (sc.MeetsMinimumCapital ? string.Empty : " | below capital minimum"),
                false)).ToArray();

            return new Embed(
                title: "Sleeve Readiness (paper-validation gate)",
                // First-read mobile text: per-sleeve status line (e.g. "Income: Ready | Options:
                // Not Ready"); the recommendation-only disclaimer lives in the footer.
                description: string.Join(" | ", scorecards.Select(sc =>
                    $"{sc.SleeveName}: {RenderReadiness(sc.Readiness)}")),
                color: 3447003 /* blue */,
                timestamp: day.ToString("O", CultureInfo.InvariantCulture),
                fields: fields,
                footer: new EmbedFooter("Recommendation-only — never changes mode, risk, or orders."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Sleeve readiness section omitted from daily report — scorecard generation failed. ErrorType={ErrorType}",
                ex.GetType().Name);
            return null;
        }
    }

    private static string RenderProfitFactor(SleeveReadinessScorecard scorecard) =>
        scorecard.IsProfitFactorUndefined
            ? "∞ (no losses)"
            : scorecard.Evaluation.ActualMetrics.ProfitFactor.ToString("0.##", CultureInfo.InvariantCulture);

    // Human-readable readiness state for embed copy ("NotReady" → "Not Ready").
    private static string RenderReadiness(SleeveReadinessState state) => state switch
    {
        SleeveReadinessState.Ready => "Ready",
        SleeveReadinessState.NotReady => "Not Ready",
        SleeveReadinessState.InsufficientData => "Insufficient Data",
        _ => state.ToString()
    };

    // POSTs the payload, honoring a 429 Retry-After with a bounded backoff (count + cumulative
    // wait budget). Degrades to a scrubbed log and returns (no throw) on non-2xx exhaustion or a
    // transport failure — a missed report is never an operational stop. Status code / exception
    // type only is logged — never the webhook URL or token.
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
                    _logger.LogInformation("Sent Discord daily report for {Date}", day);
                    return;
                }

                var remaining = MaxTotalWaitSeconds - (DateTime.UtcNow - start).TotalSeconds;
                if ((int)response.StatusCode != 429 || attempt >= MaxRetries || remaining <= 0)
                {
                    // Terminal failure: non-429, retry count exhausted, or wait budget exhausted.
                    // The report is informational — log and move on (token-free diagnostics only).
                    _logger.LogError(
                        "Daily report NOT delivered and will not be retried. StatusCode={StatusCode}, Attempts={Attempt}/{MaxRetries}, Date={Date}",
                        (int)response.StatusCode,
                        attempt + 1,
                        MaxRetries,
                        day);
                    return;
                }

                // A Retry-After of 0 is a valid "retry now"; keep the wait non-negative and
                // inside the remaining budget.
                var wait = Math.Max(0, Math.Min(DiscordWebhookGuard.RetryAfterSeconds(response), remaining));

                _logger.LogWarning(
                    "Discord rate-limited (429); retrying daily report for {Date} after {WaitSeconds:F2}s (attempt {Attempt}/{Max})",
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
            catch (HttpRequestException ex)
            {
                // Transport failure. Log only token-free diagnostics: exception type, the nullable
                // HTTP status, and the inner exception type. NEVER log ex.Message / ex.ToString()
                // — those can echo the token-bearing request URI.
                _logger.LogError(
                    "Daily report NOT delivered and will not be retried — transport error. ErrorType={ErrorType}, StatusCode={StatusCode}, InnerErrorType={InnerErrorType}, Date={Date}",
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

    private static string Percent(decimal ratio) =>
        ratio.ToString("P2", CultureInfo.InvariantCulture);

    // Discord embed shapes (lowercase property names match the webhook JSON contract). A null
    // footer is omitted from the payload rather than serialized as "footer": null.
    private sealed record Embed(
        string title,
        string description,
        int color,
        string timestamp,
        EmbedField[] fields,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        EmbedFooter? footer = null);

    private sealed record EmbedField(string name, string value, bool inline);

    private sealed record EmbedFooter(string text);
}
