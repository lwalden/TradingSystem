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
        // S5-004r: OptionsSleeveManager takes the bare TacticalConfig (its siblings take
        // IOptions<TacticalConfig>), so the bare type must be resolvable or the worker dies
        // at DI validation on boot. This was masked by the AI-package TypeLoadException —
        // the unit suite never builds the full host graph, only the real `func start` does.
        services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TacticalConfig>>().Value);
        services.Configure<IBKRConfig>(
            context.Configuration.GetSection("IBKR"));
        services.Configure<LocalStorageConfig>(
            context.Configuration.GetSection("LocalStorage"));
        services.Configure<PolygonConfig>(
            context.Configuration.GetSection("Polygon"));
        services.Configure<DiscordConfig>(
            context.Configuration.GetSection("Discord"));
        // S5-002: reporting cadence (weekly readiness-scorecard day, default Friday — locked
        // decision 5). Operational/observability config only — never risk parameters.
        services.Configure<ReportingConfig>(
            context.Configuration.GetSection("Reporting"));
        // S6-001 (Default D5): owner gate for the monthly reinvest timer. OrderPlacementEnabled
        // defaults FALSE (locked decision 1: recommendation-only — plan + report, NO orders).
        // Operational gate config, NOT risk config; injected as IOptions<IncomeSleeveConfig>
        // only — deliberately no bare-type registration of this config.
        services.Configure<IncomeSleeveConfig>(
            context.Configuration.GetSection("IncomeSleeve"));
        
        // Application Insights — registration is inert until APPLICATIONINSIGHTS_CONNECTION_STRING
        // is set (it is NOT set in local.settings.json or any deployed config today): with no
        // connection string the SDK builds a disabled TelemetryConfiguration and sends nothing.
        // Before ever setting that variable, the discord.com URI-redaction gate in DECISIONS.md
        // (Known Debt KD-005/KD-006) must be satisfied — AI 2.x dependency telemetry records full
        // request URLs, which for the Discord named clients are token-bearing webhook URLs.
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        
        // Core services
        services.AddSingleton<IBrokerService, IBKRBrokerService>();
        services.AddSingleton<IMarketDataService, CachingMarketDataService>();
        // 8s send bound: an alert POST must never hang an orchestration run. A timeout surfaces
        // as OperationCanceledException and is swallowed by the service's no-throw contract.
        services.AddHttpClient("DiscordRiskAlerts", c => c.Timeout = TimeSpan.FromSeconds(8));
        // S5-003 (Default D5): ONE DiscordRiskAlertService instance serves BOTH alert interfaces
        // — same webhook/config/named client/S3-004 hardening. Risk stops render red with
        // metrics fields; operational (connect/orchestration failure) alerts render orange.
        services.AddSingleton<TradingSystem.Functions.DiscordRiskAlertService>();
        services.AddSingleton<IRiskAlertService>(sp =>
            sp.GetRequiredService<TradingSystem.Functions.DiscordRiskAlertService>());
        services.AddSingleton<IOperationalAlertService>(sp =>
            sp.GetRequiredService<TradingSystem.Functions.DiscordRiskAlertService>());
        // S4-003: daily digest reuses the SAME webhook/config as risk alerts (no new secret) but
        // gets its own named client so the two senders' handlers/telemetry stay distinguishable.
        // S5-002: same 8s bound as DiscordRiskAlerts above — this POST now sits on the EOD timer
        // path, and a hung report send must never stall the run (timeout surfaces as
        // OperationCanceledException and is swallowed by the report's no-throw contract).
        services.AddHttpClient("DiscordDailyReport", c => c.Timeout = TimeSpan.FromSeconds(8));
        services.AddSingleton<IDailyReportService, TradingSystem.Functions.DiscordDailyReportService>();
        services.AddSingleton<IRiskManager, RiskManager>();
        // S5-001: end-of-day pipeline — delegates the sync/stop-check/base-snapshot spine to
        // RiskManager (alert-only, locked decision 4) and enriches today's snapshot best-effort.
        // S5-002: the registered IDailyReportService above flows into EndOfDayService's optional
        // ctor parameter — the daily report goes out after snapshot persistence, in its own
        // try/catch (report failure never fails the EOD run; degraded runs send no report).
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
        
        // S6-001 income sleeve wiring. IncomeSleeveManager's ctor takes BARE
        // IncomeConfig/ExecutionConfig/IncomeUniverse — bare-type resolution is exactly the
        // S5-004r boot-crash class (DI validation kills the worker at startup if any of these
        // is unresolvable, and the unit suite never builds the full host graph). The factory
        // delegates below make them resolvable from the one bound TradingSystemConfig; the
        // host-boot smoke (tools/ci/host-boot-smoke.sh, S6-002) is the gate that proves it.
        services.AddSingleton<IncomeUniverse>();
        services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TradingSystemConfig>>().Value.Income);
        services.AddSingleton(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TradingSystemConfig>>().Value.Execution);
        services.AddSingleton<TradingSystem.Strategies.Income.IncomeSleeveManager>();
        // Thin-timer pipeline (Default D2) + digest-class plan report (Default D6). The report
        // reuses the SAME webhook/config as the other Discord senders (no new secret) but gets
        // its own named client so the senders stay distinguishable. 8s send bound: a report
        // POST must never hang the reinvest run (constraint 4 — timeout surfaces as
        // OperationCanceledException and is swallowed by the report's no-throw contract).
        services.AddHttpClient("DiscordIncomeReport", c => c.Timeout = TimeSpan.FromSeconds(8));
        services.AddSingleton<IIncomeReportService, TradingSystem.Functions.DiscordIncomeReportService>();
        services.AddSingleton<IIncomeReinvestService, TradingSystem.Functions.IncomeReinvestService>();

        // AI Service — Claude regime detection with rule-based fallback (ADR-003, ADR-012).
        // Registration (config bind + gated client wiring, incl. the named ClaudeGateway client)
        // is centralized in ClaudeServiceRegistration so it is unit-testable (S2-004, ADR-029).
        ClaudeServiceRegistration.Add(services, context.Configuration);
        
        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
            // S5-004 (review): IHttpClientFactory's LogicalHandler logs the full request URI at
            // Information on every send — for Discord named clients that URI IS the token-bearing
            // webhook URL. Warning+ for every "System.Net.Http.HttpClient.*" ILogger category
            // keeps the secret out of console logs and out of App Insights *log* telemetry while
            // preserving error visibility. Scope note (S5-004r review): this filter governs only
            // ILogger output — it does NOT cover App Insights *dependency* telemetry, which the
            // AI 2.x SDK emits with the full request URL outside the logging pipeline. That path
            // is dormant solely because APPLICATIONINSIGHTS_CONNECTION_STRING is unset (see the
            // enablement-gate comment at the App Insights registration above and DECISIONS.md
            // Known Debt).
            builder.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        });
    })
    .Build();

host.Run();
