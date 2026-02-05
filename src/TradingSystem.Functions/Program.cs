using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
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
        
        // Application Insights
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        
        // Core services
        // TODO: Register implementations as we build them
        // services.AddSingleton<IBrokerService, IBKRBrokerService>();
        // services.AddSingleton<IMarketDataService, MarketDataService>();
        // services.AddSingleton<IRiskManager, RiskManager>();
        // services.AddSingleton<IExecutionService, ExecutionService>();
        
        // Repositories (Cosmos DB)
        // TODO: Register repository implementations
        // services.AddSingleton<ITradeRepository, CosmosTradeRepository>();
        // services.AddSingleton<ISignalRepository, CosmosSignalRepository>();
        // services.AddSingleton<IOrderRepository, CosmosOrderRepository>();
        
        // Strategies
        // TODO: Register strategy implementations
        // services.AddSingleton<IStrategy, IncomeMonthlyReinvestStrategy>();
        // services.AddSingleton<IStrategy, MomentumBreakoutStrategy>();
        
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
