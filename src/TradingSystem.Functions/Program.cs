using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingSystem.Brokers.IBKR;
using TradingSystem.Brokers.IBKR.Services;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Services;
using TradingSystem.MarketData.Polygon;
using TradingSystem.MarketData.Polygon.Services;
using TradingSystem.Storage;
using TradingSystem.Storage.Repositories;
using TradingSystem.AI.Services;
using TradingSystem.Strategies.Options;
using TradingSystem.Strategies.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true);
        config.AddEnvironmentVariables();
        
        // Add Azure Key Vault in production
        var builtConfig = config.Build();
        var keyVaultUri = builtConfig["KeyVaultUri"];
        if (!string.IsNullOrEmpty(keyVaultUri))
        {
            config.AddAzureKeyVault(
                new Uri(keyVaultUri),
                new Azure.Identity.DefaultAzureCredential());
        }
    })
    .ConfigureServices((context, services) =>
    {
        // Configuration
        services.Configure<TradingSystemConfig>(
            context.Configuration.GetSection("TradingSystem"));
        services.Configure<TacticalConfig>(
            context.Configuration.GetSection("TradingSystem:Tactical"));
        services.Configure<IBKRConfig>(
            context.Configuration.GetSection("IBKR"));
        services.Configure<LocalStorageConfig>(
            context.Configuration.GetSection("LocalStorage"));
        services.Configure<PolygonConfig>(
            context.Configuration.GetSection("Polygon"));
        services.Configure<DiscordConfig>(
            context.Configuration.GetSection("Discord"));
        
        // Application Insights
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        
        // Core services
        services.AddSingleton<IBrokerService, IBKRBrokerService>();
        services.AddSingleton<IMarketDataService, CachingMarketDataService>();
        services.AddHttpClient("DiscordRiskAlerts");
        services.AddSingleton<IRiskAlertService, TradingSystem.Functions.DiscordRiskAlertService>();
        // S4-003: daily digest reuses the SAME webhook/config as risk alerts (no new secret) but
        // gets its own named client so the two senders' handlers/telemetry stay distinguishable.
        services.AddHttpClient("DiscordDailyReport");
        services.AddSingleton<IDailyReportService, TradingSystem.Functions.DiscordDailyReportService>();
        services.AddSingleton<IRiskManager, RiskManager>();
        // S5-001: end-of-day pipeline — delegates the sync/stop-check/base-snapshot spine to
        // RiskManager (alert-only, locked decision 4) and enriches today's snapshot best-effort.
        services.AddSingleton<IEndOfDayService, TradingSystem.Functions.EndOfDayService>();
        services.AddSingleton<IExecutionService, SimpleExecutionService>();
        services.AddSingleton<OptionsExecutionService>();

        // Repositories (local JSON storage for now)
        services.AddSingleton<IOrderRepository, JsonOrderRepository>();
        services.AddSingleton<ISignalRepository, JsonSignalRepository>();
        services.AddSingleton<ISnapshotRepository, JsonSnapshotRepository>();
        services.AddSingleton<IOptionsPositionRepository, JsonOptionsPositionRepository>();
        services.AddSingleton<ITradeRepository, JsonTradeRepository>();
        services.AddSingleton<IConfigRepository, JsonConfigRepository>();

        // S4-002 readiness scorecards (read-only, recommendation-only) — consumed by the
        // S4-003 daily report's optional readiness section.
        services.AddSingleton<ISleeveReadinessScorecardService, SleeveReadinessScorecardService>();

        // External data clients/services
        services.AddHttpClient<PolygonApiClient>();
        services.AddSingleton<ICalendarService, PolygonCalendarService>();

        // Options strategy services
        services.AddSingleton<OptionsScreeningService>();
        services.AddSingleton<OptionsLifecycleRules>();
        services.AddSingleton<OptionsPositionGrouper>();
        services.AddSingleton<OptionsCandidateConverter>();
        services.AddSingleton<OptionsPositionSizer>();
        services.AddSingleton<OptionsSleeveManager>();
        
        // AI Service — Claude regime detection with rule-based fallback (ADR-003, ADR-012).
        // Registration (config bind + gated client wiring, incl. the named ClaudeGateway client)
        // is centralized in ClaudeServiceRegistration so it is unit-testable (S2-004, ADR-029).
        ClaudeServiceRegistration.Add(services, context.Configuration);
        
        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
    })
    .Build();

host.Run();
