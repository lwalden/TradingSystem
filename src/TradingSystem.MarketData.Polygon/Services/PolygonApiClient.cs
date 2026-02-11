using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.MarketData.Polygon.Models;

namespace TradingSystem.MarketData.Polygon.Services;

/// <summary>
/// HTTP client for Polygon.io REST API with rate limiting.
/// Starter plan: 5 requests/minute.
/// </summary>
public class PolygonApiClient
{
    private readonly HttpClient _httpClient;
    private readonly PolygonConfig _config;
    private readonly ILogger<PolygonApiClient> _logger;
    private readonly SemaphoreSlim _rateLimiter;
    private DateTime _windowStart = DateTime.UtcNow;
    private int _requestsInWindow;
    private readonly object _rateLock = new();

    public PolygonApiClient(
        HttpClient httpClient,
        IOptions<PolygonConfig> config,
        ILogger<PolygonApiClient> logger)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;
        _rateLimiter = new SemaphoreSlim(1, 1);

        _httpClient.BaseAddress = new Uri(_config.BaseUrl);
    }

    internal async Task<PolygonEarningsResponse> GetEarningsAsync(
        DateTime startDate, DateTime endDate,
        IEnumerable<string>? symbols = null,
        CancellationToken ct = default)
    {
        await EnforceRateLimitAsync(ct);

        var url = $"/benzinga/v1/earnings?date.gte={startDate:yyyy-MM-dd}&date.lte={endDate:yyyy-MM-dd}&limit=1000&apiKey={_config.ApiKey}";
        if (symbols != null)
        {
            var tickerList = string.Join(",", symbols);
            url += $"&ticker.any_of={tickerList}";
        }

        _logger.LogDebug("Polygon.io GET {Url}", url.Replace(_config.ApiKey, "***"));

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PolygonEarningsResponse>(cancellationToken: ct);
        return result ?? new PolygonEarningsResponse();
    }

    private async Task EnforceRateLimitAsync(CancellationToken ct)
    {
        await _rateLimiter.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            if ((now - _windowStart).TotalSeconds >= 60)
            {
                _windowStart = now;
                _requestsInWindow = 0;
            }

            if (_requestsInWindow >= _config.MaxRequestsPerMinute)
            {
                var waitTime = 60 - (now - _windowStart).TotalSeconds;
                if (waitTime > 0)
                {
                    _logger.LogDebug("Rate limit reached, waiting {WaitSeconds:F1}s", waitTime);
                    await Task.Delay(TimeSpan.FromSeconds(waitTime), ct);
                }
                _windowStart = DateTime.UtcNow;
                _requestsInWindow = 0;
            }

            _requestsInWindow++;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }
}
