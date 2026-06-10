using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TradingSystem.Tests.Functions;

/// <summary>
/// S5-004 (review fix-now): IHttpClientFactory's LogicalHandler/ClientHandler loggers echo the
/// full request URI at Information on every send. For the Discord named clients that URI is the
/// token-bearing webhook URL, so Program.cs adds a category filter
/// (<c>AddFilter("System.Net.Http.HttpClient", LogLevel.Warning)</c>) to keep it out of
/// console/App Insights logs. These tests pin the filter's category-prefix semantics: if the
/// filter line is removed from the logging config, the same configuration built here would have
/// Information enabled for the LogicalHandler category and the assertion documents what breaks.
/// </summary>
public class HttpClientLoggingFilterTests
{
    /// <summary>Builds a logger factory with the same logging configuration as Program.cs.</summary>
    private static ILoggerFactory BuildProgramEquivalentLoggerFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            // Stand-in for AddConsole(): filter rules only apply against registered providers,
            // and a real console provider would pollute test output.
            builder.Services.AddSingleton<ILoggerProvider, AlwaysEnabledLoggerProvider>();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        });
        return services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
    }

    /// <summary>Provider whose loggers accept every level, so IsEnabled reflects filter rules only.</summary>
    private sealed class AlwaysEnabledLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new NoopLogger();
        public void Dispose() { }

        private sealed class NoopLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter) { }
        }
    }

    [Theory]
    [InlineData("System.Net.Http.HttpClient.DiscordRiskAlerts.LogicalHandler")]
    [InlineData("System.Net.Http.HttpClient.DiscordDailyReport.LogicalHandler")]
    [InlineData("System.Net.Http.HttpClient.DiscordRiskAlerts.ClientHandler")]
    public void HttpClientFactoryCategories_HaveInformationDisabled(string category)
    {
        using var factory = BuildProgramEquivalentLoggerFactory();
        var logger = factory.CreateLogger(category);

        // The webhook-URL echo is logged at Information — it must be filtered out…
        Assert.False(logger.IsEnabled(LogLevel.Information));
        // …while Warning+ stays visible for genuine HTTP-layer problems.
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }

    [Fact]
    public void ApplicationCategories_StillLogAtInformation()
    {
        using var factory = BuildProgramEquivalentLoggerFactory();
        var logger = factory.CreateLogger("TradingSystem.Functions.DiscordRiskAlertService");

        Assert.True(logger.IsEnabled(LogLevel.Information));
    }
}
