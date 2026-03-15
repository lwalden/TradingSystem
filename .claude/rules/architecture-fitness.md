---
description: Architecture fitness rules — structural constraints for this project
---

# Architecture Fitness Rules
# AIAgentMinder-managed. Delete this file to opt out of architecture fitness enforcement.

## Layer Boundaries

The solution has six logical layers. Each layer may only reference layers listed after it.

| Layer | Project | May Reference |
|---|---|---|
| Orchestration | `TradingSystem.Functions` | all layers |
| Strategy | `TradingSystem.Strategies` | Core, AI, MarketData |
| Broker | `TradingSystem.Brokers.IBKR` | Core |
| AI | `TradingSystem.AI` | Core |
| MarketData | `TradingSystem.MarketData.Polygon` | Core |
| Storage | `TradingSystem.Storage` | Core |
| Core | `TradingSystem.Core` | nothing (no project references) |

**Violations to reject:**
- `TradingSystem.Core` must not reference any other project in the solution.
- `TradingSystem.Strategies` must not directly reference `TradingSystem.Brokers.IBKR` or `TradingSystem.Storage` — it depends only on interfaces defined in Core.
- `TradingSystem.Storage` must not reference `TradingSystem.Strategies` or `TradingSystem.Brokers.IBKR`.
- `TradingSystem.AI` must not reference `TradingSystem.Strategies`, `TradingSystem.Brokers.IBKR`, or `TradingSystem.Storage`.

---

## External API Calls

External HTTP calls are confined to dedicated integration projects. No other project may make HTTP calls.

| External Service | Allowed Location |
|---|---|
| Claude API | `TradingSystem.AI/Services/ClaudeService.cs` only |
| Polygon.io | `TradingSystem.MarketData.Polygon/Services/PolygonApiClient.cs` only |
| Discord webhooks | `TradingSystem.Functions/DiscordRiskAlertService.cs` only |
| IBKR TWS API | `TradingSystem.Brokers.IBKR/` only (via `EClientSocket`, not HTTP) |

**Rules:**
- `HttpClient` must not be instantiated with `new HttpClient()` — use `IHttpClientFactory` or `AddHttpClient<T>()` registration.
- No `HttpClient`, `WebClient`, or raw socket calls in `TradingSystem.Core`, `TradingSystem.Strategies`, or `TradingSystem.Storage`.
- Strategy classes must access market data via `IMarketDataService` (Core interface), not by calling `PolygonApiClient` directly.

---

## Interface-Based Design

Any class that crosses a project boundary must be abstracted behind an interface defined in `TradingSystem.Core/Interfaces/`.

**Required interface coverage:**
- All broker operations: `IBrokerService`
- All repository operations: `IOrderRepository`, `ISignalRepository`, `IOptionsPositionRepository`, `ISnapshotRepository`, `IConfigRepository`, `IIVHistoryRepository`
- All strategy classes: `IStrategy` (or `IAIStrategy`)
- Market data access from strategies: `IMarketDataService`
- Risk management: `IRiskManager`, `IRiskAlertService`
- AI analysis: `IClaudeService`

**Rule:** If a service in one project is consumed by another project, it must implement an interface from Core. Concrete types must not be referenced across project boundaries.

---

## Async/Await

All I/O operations must be async. Blocking on async code is prohibited.

**Rules:**
- Any method that calls a broker, repository, HTTP client, or file system must return `Task` or `Task<T>`.
- All public async methods must accept `CancellationToken cancellationToken` as a final parameter.
- Never use `.Result`, `.Wait()`, or `GetAwaiter().GetResult()` to block on a Task — this can deadlock in Azure Functions.
- `TaskCompletionSource` must be created with `TaskCreationOptions.RunContinuationsAsynchronously` to avoid deadlocks on the IBKR message pump thread.

---

## Configuration

Configuration must flow through `IOptions<T>`, not static fields or direct environment variable reads.

**Rules:**
- Configuration classes live in `TradingSystem.Core/Configuration/` (shared config) or alongside the project that owns them (e.g., `IBKRConfig.cs` in `TradingSystem.Brokers.IBKR`).
- Register configuration with `services.Configure<T>(context.Configuration.GetSection("..."))` in `Program.cs`.
- No `Environment.GetEnvironmentVariable()` calls in business logic — only in `Program.cs` bootstrap or configuration binding.
- Secrets (API keys, connection strings) must not appear in any committed file. Use `local.settings.json` (gitignored) for dev, Azure Key Vault for production.

---

## Repository Pattern

Data access must go through repository interfaces. Business logic must not read or write JSON files directly.

**Rules:**
- All `JsonFileStore` usage must be wrapped in a concrete repository class in `TradingSystem.Storage/Repositories/`.
- Repository implementations may not contain business logic — only serialization, querying, and persistence.
- Strategy and service classes must depend on `IXxxRepository` interfaces (from Core), not on `JsonXxxRepository` directly.
- Each repository handles exactly one entity type (one-to-one mapping: `IOrderRepository` ↔ `Order`).

---

## Test Isolation

Tests must be self-contained and independently runnable.

**Rules:**
- Test projects reference `TradingSystem.Core` and the project under test — not other implementation projects.
- No test file may import from another test file. Shared helpers belong in a `Helpers/` or `Fixtures/` folder within the test project.
- Unit tests must not make real network calls, write to the file system, or depend on external services. Use Moq to mock `IBrokerService`, `IMarketDataService`, and repository interfaces.
- Test method naming: `MethodName_Scenario_ExpectedOutcome` — e.g., `Score_CSP_HighIVAndGoodRoR_ScoresHigh`.
- Smoke tests (in `TradingSystem.SmokeTest`) are the only tests that may use real external dependencies, and only against sandbox/paper trading accounts.

---

## File Size and Responsibility

**Rules:**
- If a source file exceeds 400 lines, flag it for decomposition before adding more code. Large files typically contain more than one responsibility. (`IBKRBrokerService.cs` at ~824 lines is a known exception — see DECISIONS.md.)
- Strategy classes must not contain execution logic (placing orders). Execution belongs in `IExecutionService`.
- Orchestration logic (sequencing calls, error handling across services) belongs in `TradingSystem.Functions`, not in strategy or service classes.

---

## Trading Safety

These rules enforce the capital-preservation-first policy.

**Rules:**
- `TradingMode` (Live vs. Sandbox) must be read from `TradingSystemConfig` via `IOptions<TradingSystemConfig>` — never hard-coded or overridden in code.
- No code may switch `TradingMode` at runtime. Mode changes require a config update and redeployment.
- Risk parameters (`MaxDrawdownPercent`, `DailyStopLossPercent`, sleeve allocations) must not be modified by any automated path — only by human config change.
- Order placement must only occur from `IExecutionService` implementations, never directly from strategy classes or the orchestrator.

---

## Enforcement

When writing or reviewing code:

1. Check each constraint above before creating or modifying a file in scope.
2. If a constraint would be violated: explain the rule, show the compliant alternative, and implement the compliant version.
3. If there's a legitimate exception: document it in a code comment (`// Architecture exception: [reason]`) and note it in DECISIONS.md.
