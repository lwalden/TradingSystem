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
        
        // Application Insights
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        
        // Core services
        services.AddSingleton<IBrokerService, IBKRBrokerService>();
        services.AddSingleton<IMarketDataService, CachingMarketDataService>();
        services.AddSingleton<IRiskManager, RiskManager>();
        services.AddSingleton<IExecutionService, SimpleExecutionService>();
        services.AddSingleton<OptionsExecutionService>();

        // Repositories (local JSON storage for now)
        services.AddSingleton<IOrderRepository, JsonOrderRepository>();
        services.AddSingleton<ISignalRepository, JsonSignalRepository>();
        services.AddSingleton<IOptionsPositionRepository, JsonOptionsPositionRepository>();

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
        
        // AI Service
        // TODO: Register Claude service
        // services.AddSingleton<IClaudeService, ClaudeService>();
        
        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
    })
    .Build();

host.Run();
