using System.Net;
using System.Text;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingSystem.MarketData.Polygon.Services;
using TradingSystem.MarketData.Polygon;
using TradingSystem.MarketData.Polygon.Models;
using TradingSystem.Core.Models;

namespace TradingSystem.Tests.Calendar;

public class PolygonCalendarServiceTests
{
    private class MockHttpHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public MockHttpHandler(HttpResponseMessage response) => _response = response;

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_response);
        }
    }

    private PolygonCalendarService CreateService(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var config = Microsoft.Extensions.Options.Options.Create(new PolygonConfig
        {
            ApiKey = "test-api-key",
            BaseUrl = "https://api.polygon.io",
            MaxRequestsPerMinute = 5,
            EarningsLookbackDays = 7,
            EarningsLookforwardDays = 30
        });

        var apiClientLogger = new NullLogger<PolygonApiClient>();
        var apiClient = new PolygonApiClient(httpClient, config, apiClientLogger);

        var serviceLogger = new NullLogger<PolygonCalendarService>();
        return new PolygonCalendarService(apiClient, config, serviceLogger);
    }

    [Fact]
    public async Task GetEarningsCalendarAsync_ReturnsEvents()
    {
        // Arrange
        var jsonResponse = @"{
            ""status"": ""OK"",
            ""request_id"": ""abc123"",
            ""count"": 2,
            ""results"": [
                {
                    ""ticker"": ""AAPL"",
                    ""company_name"": ""Apple Inc"",
                    ""date"": ""2026-02-15"",
                    ""time"": ""21:30:00"",
                    ""estimated_eps"": 2.35,
                    ""actual_eps"": null
                },
                {
                    ""ticker"": ""MSFT"",
                    ""company_name"": ""Microsoft Corporation"",
                    ""date"": ""2026-02-20"",
                    ""time"": ""12:00:00"",
                    ""estimated_eps"": 3.10,
                    ""actual_eps"": null
                }
            ]
        }";

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };
        var handler = new MockHttpHandler(response);
        var service = CreateService(handler);

        var startDate = new DateTime(2026, 2, 10);
        var endDate = new DateTime(2026, 2, 25);

        // Act
        var events = await service.GetEarningsCalendarAsync(startDate, endDate);

        // Assert
        Assert.NotNull(events);
        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.Symbol == "AAPL");
        Assert.Contains(events, e => e.Symbol == "MSFT");

        var appleEvent = events.First(e => e.Symbol == "AAPL");
        Assert.Equal(new DateTime(2026, 2, 15), appleEvent.Date);
        Assert.Equal(2.35m, appleEvent.EstimatedEPS);
        Assert.Null(appleEvent.ActualEPS);
    }

    [Fact]
    public async Task GetEarningsCalendarAsync_EmptyResults_ReturnsEmptyList()
    {
        // Arrange
        var jsonResponse = @"{
            ""status"": ""OK"",
            ""request_id"": ""abc123"",
            ""count"": 0,
            ""results"": []
        }";

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };
        var handler = new MockHttpHandler(response);
        var service = CreateService(handler);

        var startDate = new DateTime(2026, 2, 10);
        var endDate = new DateTime(2026, 2, 25);

        // Act
        var events = await service.GetEarningsCalendarAsync(startDate, endDate);

        // Assert
        Assert.NotNull(events);
        Assert.Empty(events);
    }

    [Fact]
    public async Task GetEarningsCalendarAsync_CachesResults()
    {
        // Arrange
        var jsonResponse = @"{
            ""status"": ""OK"",
            ""request_id"": ""abc123"",
            ""count"": 1,
            ""results"": [
                {
                    ""ticker"": ""AAPL"",
                    ""company_name"": ""Apple Inc"",
                    ""date"": ""2026-02-15"",
                    ""time"": ""21:30:00"",
                    ""estimated_eps"": 2.35,
                    ""actual_eps"": null
                }
            ]
        }";

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };
        var handler = new MockHttpHandler(response);
        var service = CreateService(handler);

        var startDate = new DateTime(2026, 2, 10);
        var endDate = new DateTime(2026, 2, 25);

        // Act
        var firstCall = await service.GetEarningsCalendarAsync(startDate, endDate);
        var secondCall = await service.GetEarningsCalendarAsync(startDate, endDate);

        // Assert
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(firstCall.Count, secondCall.Count);
    }

    [Fact]
    public async Task GetEarningsCalendarAsync_ParsesTimingCorrectly()
    {
        // Arrange
        var jsonResponse = @"{
            ""status"": ""OK"",
            ""request_id"": ""abc123"",
            ""count"": 3,
            ""results"": [
                {
                    ""ticker"": ""AAPL"",
                    ""company_name"": ""Apple Inc"",
                    ""date"": ""2026-02-15"",
                    ""time"": ""21:30:00"",
                    ""estimated_eps"": 2.35,
                    ""actual_eps"": null
                },
                {
                    ""ticker"": ""MSFT"",
                    ""company_name"": ""Microsoft Corporation"",
                    ""date"": ""2026-02-20"",
                    ""time"": ""12:00:00"",
                    ""estimated_eps"": 3.10,
                    ""actual_eps"": null
                },
                {
                    ""ticker"": ""GOOGL"",
                    ""company_name"": ""Alphabet Inc"",
                    ""date"": ""2026-02-22"",
                    ""time"": """",
                    ""estimated_eps"": 1.50,
                    ""actual_eps"": null
                }
            ]
        }";

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };
        var handler = new MockHttpHandler(response);
        var service = CreateService(handler);

        var startDate = new DateTime(2026, 2, 10);
        var endDate = new DateTime(2026, 2, 25);

        // Act
        var events = await service.GetEarningsCalendarAsync(startDate, endDate);

        // Assert
        var appleEvent = events.First(e => e.Symbol == "AAPL");
        Assert.Equal(EarningsTiming.AfterMarketClose, appleEvent.Timing);

        var msftEvent = events.First(e => e.Symbol == "MSFT");
        Assert.Equal(EarningsTiming.BeforeMarketOpen, msftEvent.Timing);

        var googlEvent = events.First(e => e.Symbol == "GOOGL");
        Assert.Equal(EarningsTiming.Unknown, googlEvent.Timing);
    }

    [Fact]
    public async Task IsInNoTradeWindowAsync_InWindow_ReturnsTrue()
    {
        // Arrange
        var jsonResponse = @"{
            ""status"": ""OK"",
            ""request_id"": ""abc123"",
            ""count"": 1,
            ""results"": [
                {
                    ""ticker"": ""AAPL"",
                    ""company_name"": ""Apple Inc"",
                    ""date"": ""2026-02-15"",
                    ""time"": ""21:30:00"",
                    ""estimated_eps"": 2.35,
                    ""actual_eps"": null
                }
            ]
        }";

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };
        var handler = new MockHttpHandler(response);
        var service = CreateService(handler);

        // Earnings on 2/15, no-trade window is 2/13 - 2/16
        var checkDate = new DateTime(2026, 2, 14);

        // Act
        var isInWindow = await service.IsInNoTradeWindowAsync("AAPL", checkDate);

        // Assert
        Assert.True(isInWindow);
    }

    [Fact]
    public async Task IsInNoTradeWindowAsync_OutsideWindow_ReturnsFalse()
    {
        // Arrange
        var jsonResponse = @"{
            ""status"": ""OK"",
            ""request_id"": ""abc123"",
            ""count"": 1,
            ""results"": [
                {
                    ""ticker"": ""AAPL"",
                    ""company_name"": ""Apple Inc"",
                    ""date"": ""2026-02-15"",
                    ""time"": ""21:30:00"",
                    ""estimated_eps"": 2.35,
                    ""actual_eps"": null
                }
            ]
        }";

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };
        var handler = new MockHttpHandler(response);
        var service = CreateService(handler);

        // Earnings on 2/15, no-trade window is 2/13 - 2/16
        var checkDate = new DateTime(2026, 2, 20);

        // Act
        var isInWindow = await service.IsInNoTradeWindowAsync("AAPL", checkDate);

        // Assert
        Assert.False(isInWindow);
    }

    [Fact]
    public async Task GetSymbolsInNoTradeWindowAsync_FiltersCorrectly()
    {
        // Arrange
        var jsonResponse = @"{
            ""status"": ""OK"",
            ""request_id"": ""abc123"",
            ""count"": 3,
            ""results"": [
                {
                    ""ticker"": ""AAPL"",
                    ""company_name"": ""Apple Inc"",
                    ""date"": ""2026-02-15"",
                    ""time"": ""21:30:00"",
                    ""estimated_eps"": 2.35,
                    ""actual_eps"": null
                },
                {
                    ""ticker"": ""MSFT"",
                    ""company_name"": ""Microsoft Corporation"",
                    ""date"": ""2026-02-25"",
                    ""time"": ""12:00:00"",
                    ""estimated_eps"": 3.10,
                    ""actual_eps"": null
                },
                {
                    ""ticker"": ""GOOGL"",
                    ""company_name"": ""Alphabet Inc"",
                    ""date"": ""2026-03-01"",
                    ""time"": ""21:30:00"",
                    ""estimated_eps"": 1.50,
                    ""actual_eps"": null
                }
            ]
        }";

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };
        var handler = new MockHttpHandler(response);
        var service = CreateService(handler);

        // Check date 2/14 - only AAPL should be in window (2/15 earnings, window 2/13-2/16)
        var checkDate = new DateTime(2026, 2, 14);
        var symbols = new List<string> { "AAPL", "MSFT", "GOOGL" };

        // Act
        var inWindow = await service.GetSymbolsInNoTradeWindowAsync(symbols, checkDate);

        // Assert
        Assert.Single(inWindow);
        Assert.Contains("AAPL", inWindow);
        Assert.DoesNotContain("MSFT", inWindow);
        Assert.DoesNotContain("GOOGL", inWindow);
    }

    [Fact]
    public async Task GetDividendCalendarAsync_ReturnsEmptyList()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        var handler = new MockHttpHandler(response);
        var service = CreateService(handler);

        var startDate = new DateTime(2026, 2, 10);
        var endDate = new DateTime(2026, 2, 25);

        // Act
        var events = await service.GetDividendCalendarAsync(startDate, endDate);

        // Assert
        Assert.NotNull(events);
        Assert.Empty(events);
    }

    [Fact]
    public async Task GetMacroCalendarAsync_ReturnsEmptyList()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        var handler = new MockHttpHandler(response);
        var service = CreateService(handler);

        var startDate = new DateTime(2026, 2, 10);
        var endDate = new DateTime(2026, 2, 25);

        // Act
        var events = await service.GetMacroCalendarAsync(startDate, endDate);

        // Assert
        Assert.NotNull(events);
        Assert.Empty(events);
    }

    [Fact]
    public async Task GetEarningsCalendarAsync_FiltersBySymbol()
    {
        // Arrange
        var jsonResponse = @"{
            ""status"": ""OK"",
            ""request_id"": ""abc123"",
            ""count"": 1,
            ""results"": [
                {
                    ""ticker"": ""AAPL"",
                    ""company_name"": ""Apple Inc"",
                    ""date"": ""2026-02-15"",
                    ""time"": ""21:30:00"",
                    ""estimated_eps"": 2.35,
                    ""actual_eps"": null
                }
            ]
        }";

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        };
        var handler = new MockHttpHandler(response);
        var service = CreateService(handler);

        var startDate = new DateTime(2026, 2, 10);
        var endDate = new DateTime(2026, 2, 25);
        var symbols = new List<string> { "AAPL" };

        // Act
        var events = await service.GetEarningsCalendarAsync(startDate, endDate, symbols);

        // Assert
        Assert.NotNull(events);
        Assert.Single(events);
        Assert.Equal("AAPL", events[0].Symbol);
    }
}
