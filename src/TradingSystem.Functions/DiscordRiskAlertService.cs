using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;

namespace TradingSystem.Functions;

/// <summary>
/// Sends risk-stop alerts to Discord via webhook.
/// </summary>
public class DiscordRiskAlertService : IRiskAlertService
{
    private readonly HttpClient _httpClient;
    private readonly DiscordConfig _config;
    private readonly ILogger<DiscordRiskAlertService> _logger;

    public DiscordRiskAlertService(
        IHttpClientFactory httpClientFactory,
        IOptions<DiscordConfig> config,
        ILogger<DiscordRiskAlertService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("DiscordRiskAlerts");
        _config = config.Value;
        _logger = logger;
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
            _logger.LogInformation("Discord risk alerts are disabled; skipping alert: {Title}", title);
            return;
        }

        if (string.IsNullOrWhiteSpace(_config.WebhookUrl))
        {
            _logger.LogWarning("Discord webhook URL is not configured; cannot send alert: {Title}", title);
            return;
        }

        var payload = new
        {
            username = _config.Username,
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

        var response = await _httpClient.PostAsJsonAsync(_config.WebhookUrl, payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Failed to send Discord risk alert. StatusCode={StatusCode}, Body={Body}",
                response.StatusCode,
                body);
            return;
        }

        _logger.LogInformation("Sent Discord risk alert: {Title}", title);
    }
}
