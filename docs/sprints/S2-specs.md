# Sprint S2 (Phase-2B) — Implementation Specs

> **Status:** Draft for human approval gate.
> **Baseline:** branch `main` at PR #61 merge (`3bfe9a6`). Full suite **418 passing, 0 failing**.
> **Execution order:** S2-003 → S2-001 → S2-002 → S2-004 → S2-005 → S2-006.
> Each item ships as its own small PR off the latest `main`.

## Sprint-wide constraints (apply to every item)

- **TDD:** write the failing tests first, then implement until green. The full suite must stay green (418 + new tests). New tests live in `tests/TradingSystem.Tests/` (xUnit; per project MEMORY, add explicit `using Xunit;` — `ImplicitUsings` is on but xUnit is not in the global-usings set used by existing tests).
- **No change to deterministic trading or risk-engine behavior.** AI stays analysis-only and off the order path. `RiskManager.ValidateSignalAsync` is untouched. Rule-based regime math (`DetermineRegime`, the `regime switch` multiplier table) is unchanged.
- **Fail-closed:** when a guard trips (clamp, cost cap, gateway hang), the system drops to the free rule-based path, never to riskier behavior.
- **No secrets in code.** Conventional-commit messages. Config additions go through the existing `IOptions<ClaudeConfig>` binding pattern with sensible defaults.

## Grounding notes (verified against current code)

- `CachingMarketDataService` (`src/TradingSystem.Strategies/Services/CachingMarketDataService.cs`): the unbounded cast is **line 223** — `RiskMultiplier = (decimal)(response.RiskMultiplier ?? 1.0)`. The existing quote cache is a `ConcurrentDictionary<string,(Quote,DateTime)>` with `QuoteCacheDuration` (line 24); indicator/analytics caches are plain `ConcurrentDictionary` with no expiry. `GetIndicatorsBulkAsync` (lines 154-163) is a **sequential `foreach await`**. The `Rationale` is logged verbatim at line 79-81. `Source = "claude"` is set at line 225; the rule-path leaves `Source` at its default `"rules"`.
- `MomentumBreakoutStrategy.EvaluateAsync` consumes the multiplier at **line 70**: `SuggestedRiskAmount = NetLiquidationValue * RiskPerTradePercent * riskMultiplier`. An unbounded `2.0` would double risk — this is the concrete hazard S2-003 closes.
- `ClaudeService` (`src/TradingSystem.AI/Services/ClaudeService.cs`): gateway uses a `static readonly HttpClient _gatewayClient` (lines 21-25, base `http://localhost:3131/`, 60s timeout). The gateway→direct fallback logs **Info only** (line 53). `TryGatewayAsync` returns `null` when `GatewayApiKey` is empty (line 82-83). `_httpClient` (direct Anthropic) is the injected `HttpClient`.
- `ClaudeConfig` currently lives **inside** `src/TradingSystem.Core/Interfaces/IClaudeService.cs` (lines 14-21), same file as the `IClaudeService` interface. References: `Program.cs:84`, `ClaudeService.cs:17,29`.
- `IClaudeService.cs` in **`src/TradingSystem.AI/Services/`** is a 3-line shim with a misleading "re-exports" comment (it only `global using`s the Core namespace).
- DI guard (`Program.cs:86-90`) registers `IClaudeService` **only when `Claude:ApiKey` is non-empty** — gateway-only config currently gets no service.
- `MarketRegime`/`RegimeType` live in `src/TradingSystem.Core/Models/MarketData.cs` (regime block lines 140-173). `MarketRegime.Source` is a magic-string `string` (line 164).
- **Test project reference gap:** `tests/TradingSystem.Tests/TradingSystem.Tests.csproj` references Core, Strategies, IBKR, Storage, Polygon, Functions — **NOT `TradingSystem.AI`**. Items that test `ClaudeService` directly (**S2-002, S2-004**) must add `<ProjectReference Include="..\..\src\TradingSystem.AI\TradingSystem.AI.csproj" />` to the test csproj as part of their RED step.
- `TradingSystem.SmokeTest` references Strategies + Core (not AI) and never touches `ClaudeConfig`/`ClaudeService`, so the S2-005 config move does not affect it.
- Latest ADR in `DECISIONS.md` is **ADR-028**; **ADR-029** is the correct next number for S2-004.
- Logging assertions in this repo are currently done with `NullLogger<T>.Instance`; no existing test verifies log level/message. Items asserting "warn-on-X" must switch the SUT under test to a `Mock<ILogger<T>>` (Moq) and verify via the standard `It.IsAny<EventId>()` / state-callback pattern (see each item's test notes). This is additive — existing tests keep `NullLogger`.

---

### S2-003: Clamp AI RiskMultiplier to [0.5, 1.0] + warn-on-clamp
**Type:** fix · **Size:** S · **Risk:** risk-touching (position sizing input)

**Objective:** Bound the Claude-supplied `RiskMultiplier` to `[0.5, 1.0]` before it is stored on `MarketRegime`, so it can never inflate (or zero out) `MomentumBreakoutStrategy` position sizing. Out-of-range or absent values must clamp/default to a safe value and emit a Warning. In-range values pass through unchanged.

**Approach:**
1. In `CachingMarketDataService.DetectRegimeWithClaudeAsync`, replace the line-223 cast
   `RiskMultiplier = (decimal)(response.RiskMultiplier ?? 1.0)` with a guarded local:
   - `var rawMultiplier = (decimal)(response.RiskMultiplier ?? 1.0);`
   - `var clampedMultiplier = Math.Clamp(rawMultiplier, MinRiskMultiplier, MaxRiskMultiplier);`
   - When `clampedMultiplier != rawMultiplier`, `_logger.LogWarning("Claude RiskMultiplier {Raw} out of bounds [{Min},{Max}]; clamped to {Clamped}", rawMultiplier, MinRiskMultiplier, MaxRiskMultiplier, clampedMultiplier);`
   - Assign `RiskMultiplier = clampedMultiplier` when building the `MarketRegime`.
2. Add `private const decimal MinRiskMultiplier = 0.5m;` and `MaxRiskMultiplier = 1.0m;` near the existing `QuoteCacheDuration` field. (Constants, not config — these are safety bounds, not tunables. Note in code that the rule-path table already lives in `[0.25, 1.0]`; the clamp applies **only** to the AI path, matching the system prompt's stated `0.5-1.0` contract. Rule-path values are untouched.)
3. `null` / `0.0`: `?? 1.0` already handles `null` → `1.0` (in range, no warn). `0.0` is a real out-of-range value → clamps to `0.5` **with** a warn. Document this distinction in the test names.

**Test Plan (TDD RED):** add to `tests/TradingSystem.Tests/Options/CachingMarketDataServiceTests.cs` (extend existing class; it already mocks `IBrokerService`). These require a Claude path, so add an `IClaudeService` mock — but **do not** add an AI project reference: `IClaudeService` lives in `TradingSystem.Core.Interfaces`, already referenced. Inject `_claudeMock.Object` via the existing constructor's optional `claudeService` param. To assert the warn, construct a second SUT with a `Mock<ILogger<CachingMarketDataService>>`.
1. `GetMarketRegimeAsync_ClaudeMultiplierAboveMax_ClampsToOne` — Claude returns `riskMultiplier: 2.0` ⇒ `regime.RiskMultiplier == 1.0m`.
2. `GetMarketRegimeAsync_ClaudeMultiplierBelowMin_ClampsToHalf` — returns `-0.3` ⇒ `0.5m`.
3. `GetMarketRegimeAsync_ClaudeMultiplierZero_ClampsToHalf` — returns `0.0` ⇒ `0.5m` **and** a Warning is logged.
4. `GetMarketRegimeAsync_ClaudeMultiplierNull_DefaultsToOne_NoWarn` — `riskMultiplier` absent/null ⇒ `1.0m`, **no** Warning logged.
5. `GetMarketRegimeAsync_ClaudeMultiplierInRange_PassesThrough` — returns `0.75` ⇒ `0.75m`, no Warning.
6. `GetMarketRegimeAsync_OutOfRange_EmitsWarning` — returns `2.0`, assert `loggerMock.Verify(LogLevel.Warning … Times.Once)` (Moq `It.IsAny<EventId>()` + state callback pattern).

*Mock helper:* set `_claudeMock.Setup(c => c.AnalyzeAsync<It.IsAnyType>(...))` — but the SUT calls the **generic** `AnalyzeAsync<ClaudeRegimeResponse>` where `ClaudeRegimeResponse` is a `private` nested type. Moq cannot reference it. **Resolution:** mock returns are keyed off the public `AnalyzeAsync(AIAnalysisRequest, ct)` non-generic overload returning a JSON string; the SUT's generic overload deserializes it. So the test mocks `IClaudeService.AnalyzeAsync<T>` is not directly mockable — instead have the mock implement the generic call by returning a real `ClaudeRegimeResponse`-shaped object is impossible across the private type boundary. **Therefore:** the cleanest seam is to mock the **non-generic** path is also not what the SUT calls. *Flag for executor:* the private `ClaudeRegimeResponse` type blocks a direct generic-mock. Two acceptable fixes (executor's choice, no behavior change): (a) make `ClaudeRegimeResponse` `internal` and add `InternalsVisibleTo("TradingSystem.Tests")` to the Strategies csproj (already present per its csproj), then `_claudeMock.Setup(c => c.AnalyzeAsync<ClaudeRegimeResponse>(...))`; or (b) have the mock's generic setup use `It.IsAnyType` via Moq's `ReturnsAsync` with a factory. Prefer (a) — it is the smallest change and the InternalsVisibleTo line already exists.

**Integration/E2E:** None.
**Post-Merge Validation:** None.
**Files:** Create: none. Modify: `src/TradingSystem.Strategies/Services/CachingMarketDataService.cs`; `tests/TradingSystem.Tests/Options/CachingMarketDataServiceTests.cs`. (Possibly: change `ClaudeRegimeResponse` accessibility to `internal` — same file.)
**Dependencies:** None. First in order so the safety bound lands before the cache (S2-001) starts persisting regime values.
**Upgrade Impact:** N/A.
**Custom Instructions:** None.
**Branch:** `fix/clamp-ai-risk-multiplier`
**Commit:** `fix(strategies): clamp AI RiskMultiplier to [0.5,1.0] with warn-on-clamp`

---

### S2-001: Regime-result cache + stampede guard in GetMarketRegimeAsync
**Type:** feat · **Size:** M · **Risk:** risk-touching (gates a metered Claude call)

**Objective:** Cache the `MarketRegime` result (both Claude and rule-fallback outcomes) for a configurable TTL, and guard against a thundering herd so that N concurrent callers share exactly one underlying computation (and at most one Claude round-trip).

**Approach:**
1. Add a single-slot regime cache field mirroring the quote-cache style:
   `private (MarketRegime Regime, DateTime CachedAt)? _regimeCache;` plus a `SemaphoreSlim _regimeLock = new(1, 1)`.
   (Single slot, not a dictionary — `GetMarketRegimeAsync` takes no key.)
2. New flow in `GetMarketRegimeAsync`:
   - Fast path: if `_regimeCache` is set and `UtcNow - CachedAt < RegimeCacheDuration`, return the cached regime (0 Claude calls, 0 broker calls beyond what's needed — return immediately).
   - Otherwise `await _regimeLock.WaitAsync(ct)`; re-check the cache inside the lock (double-checked locking) so queued callers returning after the first refresh get the fresh value without recomputing.
   - On a miss, run the existing detection logic (Claude-then-rules), store `(_regimeCache = (result, UtcNow))`, release the semaphore in a `finally`.
   - **Both** the Claude result and the rule-fallback result are cached (the spec requires rule results to be cached too).
3. TTL config: add `RegimeCacheMinutes` to `ClaudeConfig` (default **20**, within the 15-30 min target). Bind via existing `IOptions<ClaudeConfig>`. **But** `CachingMarketDataService` currently takes no `ClaudeConfig` — it only takes `IBrokerService`, `ILogger`, optional `IClaudeService`. *Decision:* inject `IOptions<ClaudeConfig>? = null` as a new optional ctor param (keeps existing test ctor call working) and derive `RegimeCacheDuration = TimeSpan.FromMinutes(config?.Value.RegimeCacheMinutes ?? 20)`. Register nothing new in `Program.cs` (Options already configured at line 84). Flag: if S2-005 moves `ClaudeConfig` namespace first, rebase the `using`.
4. `ct` propagation: pass the existing `CancellationToken` into `WaitAsync`.

**Test Plan (TDD RED):** add to `CachingMarketDataServiceTests.cs`. Use the `IClaudeService` mock + broker mock; assert call counts via Moq `Times`.
1. `GetMarketRegimeAsync_WithinTtl_NoSecondClaudeCall` — call twice; Claude `AnalyzeAsync` verified `Times.Once`, broker quote/bars not re-fetched on the 2nd call.
2. `GetMarketRegimeAsync_AfterTtlExpiry_RefreshesExactlyOnce` — first call, expire `_regimeCache` via reflection (mirror the existing line 57-60 reflection pattern used for `_quoteCache`), second call ⇒ Claude `Times.Exactly(2)`.
3. `GetMarketRegimeAsync_ConcurrentCallers_ShareOneUnderlyingCall` — make the mocked `AnalyzeAsync` block on a `TaskCompletionSource`/short delay, fire N=10 concurrent `GetMarketRegimeAsync(...)` via `Task.WhenAll`, release, assert Claude `Times.Once` and all 10 results are equal.
4. `GetMarketRegimeAsync_RuleFallbackResult_IsCached` — no `IClaudeService` injected (rule path); call twice; broker SPY-bars + VIX-quote fetched only once (verify via `Times.Once` on broker, allowing for the indicator cache already covering bars).
5. `GetMarketRegimeAsync_ClaudeFailure_CachesRuleFallback` — Claude `AnalyzeAsync` throws; result is the rule regime; second call does not retry Claude (cached).
6. `ClaudeConfig_RegimeCacheMinutes_DefaultsTo20` — add to `ConfigurationTests.cs`.

**Integration/E2E:** None (unit-level concurrency test covers the stampede guard).
**Post-Merge Validation:** None.
**Files:** Create: none. Modify: `src/TradingSystem.Strategies/Services/CachingMarketDataService.cs`; `src/TradingSystem.Core/Interfaces/IClaudeService.cs` (add `RegimeCacheMinutes` to `ClaudeConfig`); `tests/TradingSystem.Tests/Options/CachingMarketDataServiceTests.cs`; `tests/TradingSystem.Tests/ConfigurationTests.cs`.
**Dependencies:** Builds on S2-003 (clamp must already be in the cached value). Touches the same method/file as S2-003 — land S2-003 first to avoid a self-conflict.
**Upgrade Impact:** N/A.
**Custom Instructions:** None.
**Branch:** `feat/regime-result-cache`
**Commit:** `feat(strategies): cache regime result with TTL + stampede guard`

---

### S2-002: Fail-closed cost controls on metered-API fallback in ClaudeService
**Type:** feat · **Size:** M · **Risk:** risk-touching (controls spend on metered API)

**Objective:** Make the gateway→direct-API fallback observable and bounded. Promote the fallback log to Warning with structured fields; add a daily counter of direct (metered) calls with a configurable `MaxDirectApiCallsPerDay` that **fails closed** (no metered call; return the rule path) once exceeded; log the active pricing path at startup and warn when `GatewayApiKey` is empty.

**Approach:**
1. **Warning fallback log:** in `ClaudeService.AnalyzeAsync`, change line 53 from `LogInformation("Gateway unavailable, falling back to direct API")` to `LogWarning` with structured fields, e.g. `_logger.LogWarning("Claude gateway unavailable; falling back to METERED direct API. StrategyId={StrategyId} DirectCallsToday={Count}/{Max}", request.StrategyId, count, max)`.
2. **Daily counter + cap (fail closed):**
   - Add `private int _directCallsToday;` and `private DateOnly _counterDate = DateOnly.FromDateTime(DateTime.UtcNow);` plus a lock object (`ClaudeService` is registered as a typed `HttpClient` service — scope/lifetime is per `HttpClient` factory; treat the counter as instance state guarded by a lock; note it resets on process restart — acceptable for a daily soft cap, document in ADR/comment).
   - Before each direct call: roll the counter if `DateOnly.FromDateTime(UtcNow) != _counterDate` (reset to 0, update date) — this satisfies "resets daily".
   - If `_directCallsToday >= _config.MaxDirectApiCallsPerDay`, **do not** call Anthropic. Log Warning (`"Daily metered-API cap {Max} reached; refusing direct call, returning null (rule fallback)"`) and **return `null`** from the non-generic `AnalyzeAsync` (signature returns `Task<string>` — change the gateway-miss-and-capped branch to return an empty/sentinel that the caller treats as "no AI"). *Caller contract:* `CachingMarketDataService.DetectRegimeWithClaudeAsync` already treats a failed/unusable Claude response as "fall back to rules" — verify the empty-string path makes `AnalyzeAsync<T>` throw `InvalidOperationException("...did not contain valid JSON")`, which `GetMarketRegimeAsync` catches (line 85-88) and falls back. So returning empty string is already fail-closed. **Preferred cleaner approach:** have the capped branch in the non-generic `AnalyzeAsync` throw a typed `ClaudeCostCapExceededException`, caught by the existing try/catch in `GetMarketRegimeAsync` → rule fallback. Executor picks one; both are fail-closed. Document the choice.
   - Increment `_directCallsToday` only when a direct (metered) call is actually issued — **not** on gateway success.
3. **Startup pricing-path log + empty-gateway warning:** add a small log at construction time in the `ClaudeService` ctor: if `string.IsNullOrEmpty(_config.GatewayApiKey)` → `LogWarning("Claude gateway key not set; ALL calls will use the metered direct API")`; else `LogInformation("Claude pricing path: gateway-first (subscription), metered fallback capped at {Max}/day", _config.MaxDirectApiCallsPerDay)`.
4. **Config:** add `int MaxDirectApiCallsPerDay { get; set; } = 50;` (sensible conservative default) to `ClaudeConfig`.

**Test Plan (TDD RED):** new file `tests/TradingSystem.Tests/AI/ClaudeServiceTests.cs`. **RED prerequisite:** add `<ProjectReference Include="..\..\src\TradingSystem.AI\TradingSystem.AI.csproj" />` to `tests/TradingSystem.Tests/TradingSystem.Tests.csproj`. Use a stub `HttpMessageHandler` to fake the injected direct `HttpClient`, and a `Mock<ILogger<ClaudeService>>`. The gateway uses a static client today — **this item depends on S2-004 only if** we need to intercept gateway HTTP; for S2-002 we can drive the fallback by leaving `GatewayApiKey` empty (so `TryGatewayAsync` returns null at line 82-83 without any HTTP), which deterministically forces the direct path. That keeps S2-002 independent of S2-004's HttpClientFactory change.
1. `Fallback_IncrementsCounterOnlyOnDirectCall` — empty gateway key, one `AnalyzeAsync` ⇒ counter == 1; (and a gateway-success scenario where counter stays 0 — needs S2-004's injectable gateway client, so mark this sub-case as deferred/covered in S2-004 or use a test seam).
2. `Fallback_EmitsWarningLog` — assert `LogLevel.Warning` logged on the fallback.
3. `CapExceeded_DoesNotCallAnthropic_AndFailsClosed` — set `MaxDirectApiCallsPerDay = 1`; second call must **not** hit the stub handler (verify handler invocation count == 1) and must surface as no-AI (exception or empty), not a metered call.
4. `Counter_ResetsOnNewDay` — drive `_counterDate` to yesterday via reflection (mirror existing reflection pattern), then a call resets to 1.
5. `Ctor_EmptyGatewayKey_LogsMeteredWarning` — construct with empty `GatewayApiKey`, assert Warning.
6. `Ctor_GatewayKeyPresent_LogsGatewayFirstPath` — assert Info path log.
7. `MaxDirectApiCallsPerDay_DefaultsTo50` — add to `ConfigurationTests.cs`.

**Integration/E2E:** None.
**Post-Merge Validation:** None (counter is in-process; no deploy-dependent behavior).
**Files:** Create: `tests/TradingSystem.Tests/AI/ClaudeServiceTests.cs`. Modify: `src/TradingSystem.AI/Services/ClaudeService.cs`; `src/TradingSystem.Core/Interfaces/IClaudeService.cs` (add `MaxDirectApiCallsPerDay`); `tests/TradingSystem.Tests/TradingSystem.Tests.csproj` (add AI project ref); `tests/TradingSystem.Tests/ConfigurationTests.cs`. Possibly create `src/TradingSystem.AI/Services/ClaudeCostCapExceededException.cs` if the exception approach is chosen.
**Dependencies:** S2-001/S2-003 are in different files (no conflict). The "counter stays 0 on gateway success" assertion needs an injectable gateway client (S2-004); split that sub-assertion into S2-004 or use the empty-gateway seam for S2-002. Note for executor: do NOT reorder ahead of S2-004 expecting the factory — S2-002 ships first using the empty-gateway seam.
**Upgrade Impact:** N/A.
**Custom Instructions:** None.
**Branch:** `feat/claude-cost-controls`
**Commit:** `feat(ai): fail-closed daily cap + warning on metered-API fallback`

---

### S2-004: Gateway HTTP cluster via IHttpClientFactory + gateway-only DI
**Type:** refactor · **Size:** M · **Risk:** risk-touching (network timeout governs fallback latency)

**Objective:** Replace the process-wide `static readonly HttpClient _gatewayClient` with a named `"ClaudeGateway"` client created through `IHttpClientFactory` (configurable base address + timeout, default reduced to 5-10s), make `IClaudeService` register when **either** `Claude:ApiKey` or `Claude:GatewayApiKey` is present (gateway-only mode), and record the localhost-plaintext-Bearer stance as **ADR-029** (https/named-pipe explicitly noted as future, not implemented — no scope expansion).

**Approach:**
1. **Inject the named client:** add a constructor param. Two options — pick the one that keeps the typed-client registration clean:
   - `ClaudeService` is registered today via `services.AddHttpClient<IClaudeService, ClaudeService>()` (typed client → injects `HttpClient` for the **direct** API). Add a *second*, named client `services.AddHttpClient("ClaudeGateway", c => { c.BaseAddress = new Uri(config.GatewayBaseUrl); c.Timeout = TimeSpan.FromSeconds(config.GatewayTimeoutSeconds); })` and inject `IHttpClientFactory` into `ClaudeService`; in `TryGatewayAsync` use `_httpFactory.CreateClient("ClaudeGateway")`.
   - Replace the static field entirely; remove lines 21-25.
2. **Config:** add to `ClaudeConfig`: `string GatewayBaseUrl { get; set; } = "http://localhost:3131/";` and `int GatewayTimeoutSeconds { get; set; } = 8;` (within 5-10s). The 60s direct-API timeout on the injected typed client is unchanged (separate concern).
3. **DI guard broadening (`Program.cs:86-90`):** change to register when either key is present:
   ```csharp
   var claudeKey = context.Configuration["Claude:ApiKey"];
   var gatewayKey = context.Configuration["Claude:GatewayApiKey"];
   if (!string.IsNullOrEmpty(claudeKey) || !string.IsNullOrEmpty(gatewayKey))
   {
       services.AddHttpClient<IClaudeService, ClaudeService>();
       services.AddHttpClient("ClaudeGateway", c => { /* base+timeout from config */ });
   }
   ```
   Bind the named client's options from the already-configured `Claude` section. Gateway-only mode: direct `HttpClient` will have an empty `x-api-key` — acceptable because the cap/fallback (S2-002) and the empty-key behavior already fail closed; document that gateway-only relies on the gateway and that a metered fallback would 401 (which `EnsureSuccessStatusCode` turns into an exception → rule fallback). Confirm this is fail-closed, not fail-open.
4. **ADR-029:** append to `DECISIONS.md` after ADR-028, Status: Decided. State: gateway runs on `localhost:3131` over plaintext HTTP with a static Bearer token; this is acceptable because the gateway is loopback-only on the same trusted host; **https / named-pipe transport is noted as a future hardening, explicitly NOT implemented in this change.** Include alternatives considered (https-with-self-signed, Windows named pipe) and why deferred. Do not implement them.

**Test Plan (TDD RED):** extend `tests/TradingSystem.Tests/AI/ClaudeServiceTests.cs` (created in S2-002). Add a fake `IHttpClientFactory` returning an `HttpClient` backed by a controllable `HttpMessageHandler`.
1. `Gateway_UsesConfiguredTimeout` — configure `GatewayTimeoutSeconds = 5`; assert the created gateway `HttpClient.Timeout == TimeSpan.FromSeconds(5)` (or that the handler observes cancellation at ~5s — prefer asserting the configured client's `Timeout` to avoid real waits).
2. `Gateway_Hang_FallsBackFast` — gateway handler delays beyond the timeout (use a short test timeout, e.g. 200ms via injected config) / throws `TaskCanceledException`; assert `AnalyzeAsync` proceeds to the direct path (handler for direct API invoked) — i.e. fast fallback, no indefinite hang.
3. `GatewaySuccess_DoesNotIncrementDirectCounter` — gateway returns 200 with a `GatewayResponse`; assert direct handler **not** invoked and S2-002 counter stays 0 (this is the sub-case deferred from S2-002).
4. **DI test** `Program_GatewayOnlyConfig_RegistersClaudeService` — build a `ServiceCollection`, run the same registration predicate with only `Claude:GatewayApiKey` set, assert `IClaudeService` resolves. *Note:* `Program.cs` is top-level statements (not easily unit-testable). Extract the Claude registration into a small `static` helper (e.g. `ClaudeServiceRegistration.Add(services, configuration)`) in `TradingSystem.AI` so it is testable, and call it from `Program.cs`. This keeps the DI logic covered without spinning up the Functions host.
5. `Program_NoKeys_DoesNotRegisterClaudeService` — neither key set ⇒ no `IClaudeService` registered.
6. `ClaudeConfig_GatewayDefaults` — `GatewayBaseUrl` and `GatewayTimeoutSeconds` defaults (add to `ConfigurationTests.cs`).

**Integration/E2E:** None (no live gateway in CI).
**Post-Merge Validation:** After deploy, confirm the startup log (from S2-002) reports the expected pricing path and that gateway calls honor the new timeout. Mark as manual log check, not an automated test.
**Files:** Create: `src/TradingSystem.AI/Services/ClaudeServiceRegistration.cs` (extracted DI helper). Modify: `src/TradingSystem.AI/Services/ClaudeService.cs` (remove static client, inject factory); `src/TradingSystem.Functions/Program.cs` (broaden guard, call helper, register named client); `src/TradingSystem.Core/Interfaces/IClaudeService.cs` (`GatewayBaseUrl`, `GatewayTimeoutSeconds`); `DECISIONS.md` (ADR-029); `tests/TradingSystem.Tests/AI/ClaudeServiceTests.cs`; `tests/TradingSystem.Tests/ConfigurationTests.cs`.
**Dependencies:** Builds on S2-002 (same file + same test file + same `ClaudeConfig` additions). Land after S2-002. Cross-item hazard: both edit `ClaudeConfig` and `ClaudeServiceTests.cs` — sequential execution avoids conflicts.
**Upgrade Impact:** N/A (no SDK version change; `IHttpClientFactory` already available via the Functions host).
**Custom Instructions:** Keep scope tight — do NOT implement https or named-pipe transport; ADR-029 only documents the stance.
**Branch:** `refactor/gateway-httpclientfactory`
**Commit:** `refactor(ai): named ClaudeGateway HttpClient via factory + gateway-only DI`

---

### S2-005: Boundary + model cleanups (ClaudeConfig move, RegimeSource enum, comment/param hygiene)
**Type:** refactor · **Size:** M · **Risk:** no-risk (no behavior change)

**Objective:** Tidy boundaries with zero behavior change: move `ClaudeConfig` out of `TradingSystem.Core` (interface stays in Core), introduce a `RegimeSource` enum to replace the `MarketRegime.Source` magic string, fix the misleading "re-exports" comment, and standardize the public-interface `CancellationToken` parameter name to `cancellationToken`.

**Approach:**
1. **Move `ClaudeConfig`:** extract the class (currently `IClaudeService.cs` lines 14-21) into a new file. The interface must stay in `TradingSystem.Core.Interfaces` (Strategies depends on it). Target: a `Configuration` namespace. Two valid homes — pick one and apply consistently:
   - **Preferred:** new file `src/TradingSystem.Core/Configuration/ClaudeConfig.cs` in namespace `TradingSystem.Core.Configuration` (mirrors `DiscordConfig`, `TradingSystemConfig`). This keeps it referenceable by both AI and Strategies without a new dependency and matches the existing config-namespace convention. (The item title says "to TradingSystem.AI or a Configuration namespace" — Configuration namespace in Core is the lower-risk choice because `ClaudeService` (AI) and `Program.cs` (Functions) both already reference it; moving it into `TradingSystem.AI` would force Strategies/Functions to take an AI reference they may not want. **Flag this as a decision point for human approval.**)
   - Update `using`s in `ClaudeService.cs`, `Program.cs`, and any test files (S2-001/S2-002/S2-004 will have added references).
2. **`RegimeSource` enum:** add `public enum RegimeSource { Rules, Claude }` in `MarketData.cs` (next to `RegimeType`, following that pattern). Change `MarketRegime.Source` from `string` (default `"rules"`) to `RegimeSource Source { get; set; } = RegimeSource.Rules;`. Update the only writer: `CachingMarketDataService` line 225 `Source = "claude"` → `Source = RegimeSource.Claude` (the rule path leaves the default). Update `SmokeTest` only if it reads `.Source` (it does not — confirmed). No JSON-persistence consumer of `Source` exists (confirmed: only references are the model + the one writer).
3. **Fix the comment:** rewrite the 3-line `src/TradingSystem.AI/Services/IClaudeService.cs`. The `global using TradingSystem.Core.Interfaces;` is the actual mechanism — it does not "re-export". Replace the comment with an accurate one (e.g. "Brings the Core IClaudeService into scope for AI-layer files via a global using; the interface itself lives in TradingSystem.Core.Interfaces."), or delete the file and add the `global using` to the AI project's `GlobalUsings` if cleaner. Keep it a no-op for behavior.
4. **`cancellationToken` param name:** in `IClaudeService` (Core) the params are already named `cancellationToken` — verify. The cross-cutting target is `IMarketDataService` and `IStrategy`/`IBrokerService` where the param is `ct` vs `cancellationToken`. **Scope guard:** the item says "public-interface CancellationToken parameter name" — limit to the **public interface declarations**, standardizing to `cancellationToken`. Note `IMarketDataService` and `CachingMarketDataService` use `ct`. Renaming a parameter is source-compatible (callers use positional args here) but touches many signatures. *Flag:* this could be a large mechanical diff; confirm with human whether to scope to just the Claude/AI-adjacent interfaces or all public interfaces. Default to **all public interface declarations** but keep implementations free to use `ct` internally only if the interface is the contract (C# allows differing param names in implementations, but consistency is the point — rename implementations too where trivial).

**Test Plan (TDD RED):** mostly compile-driven (no behavior change), but add guard tests:
1. `MarketRegime_DefaultSource_IsRules` — `new MarketRegime().Source == RegimeSource.Rules`.
2. `GetMarketRegimeAsync_ClaudePath_SetsSourceClaude` — Claude path ⇒ `regime.Source == RegimeSource.Claude` (update/replace any existing string-based assertion).
3. `GetMarketRegimeAsync_RulePath_SourceRemainsRules` — rule path ⇒ `RegimeSource.Rules`.
4. `ConfigurationTests` — `ClaudeConfig` binds from the `Claude` section under its new namespace (update the `using`).
5. Compile gate: solution builds with new namespaces; all 418 + S2-001..004 tests still green. This is the primary "test" for the rename/move.

**Integration/E2E:** None.
**Post-Merge Validation:** None.
**Files:** Create: `src/TradingSystem.Core/Configuration/ClaudeConfig.cs`. Modify: `src/TradingSystem.Core/Interfaces/IClaudeService.cs` (remove `ClaudeConfig`, verify param names); `src/TradingSystem.Core/Models/MarketData.cs` (`RegimeSource` enum + `MarketRegime.Source` type); `src/TradingSystem.AI/Services/ClaudeService.cs` (using + accurate comment); `src/TradingSystem.AI/Services/IClaudeService.cs` (comment fix); `src/TradingSystem.Functions/Program.cs` (using); `src/TradingSystem.Strategies/Services/CachingMarketDataService.cs` (`Source` assignment); public-interface files for the `cancellationToken` rename (`IMarketDataService`, others as scoped); tests as above.
**Dependencies:** **Land LAST among the C# items.** Cross-item hazard (explicitly flagged): S2-005 moves the `ClaudeConfig` namespace and changes `MarketRegime.Source` — every earlier item (S2-001 added `RegimeCacheMinutes`, S2-002 added `MaxDirectApiCallsPerDay`, S2-004 added gateway fields, all to `ClaudeConfig`; S2-003 may have touched `Source` indirectly) will need their `using`s rebased. Sequencing S2-005 after S2-001/002/003/004 means the move absorbs all prior `ClaudeConfig` additions in one place rather than forcing repeated rebases. Do the move on top of the merged result of the prior four.
**Upgrade Impact:** N/A.
**Custom Instructions:** Two human decision points: (a) `ClaudeConfig` target — `TradingSystem.Core.Configuration` (recommended) vs `TradingSystem.AI`; (b) scope of the `cancellationToken` rename — all public interfaces vs Claude-adjacent only.
**Branch:** `refactor/boundary-and-model-cleanups`
**Commit:** `refactor(core): move ClaudeConfig, add RegimeSource enum, param/comment hygiene`

---

### S2-006: Python + log hygiene + bulk fan-out
**Type:** fix · **Size:** M · **Risk:** no-risk (tooling + observability; no trading-path change)

**Objective:** Three independent hygiene fixes: (a) in `run_cloud_backtest.py`, cancel the QC cloud job on poll timeout (avoid an orphaned paid node) and bound the log-pagination loop; (b) truncate/sanitize the Claude `Rationale` before it is logged to App Insights; (c) fan out `GetIndicatorsBulkAsync` with `Task.WhenAll` while preserving per-symbol mapping.

**Approach:**

**(a) Python — `tools/backtest/run_cloud_backtest.py`:**
- **Cancel on timeout:** in `QCClient.poll_backtest` (the `TimeoutError` raise at line 355-356) and `compile` timeout (line 317-318), before raising, attempt to abort the cloud job. QC REST exposes `backtests/delete` (and `compile` has no explicit cancel — for compile, just bound it). Add `def abort_backtest(self, backtest_id)` that POSTs `backtests/delete` with `projectId`+`backtestId`, wrapped in try/except (best-effort; log a warning, never mask the original timeout). Call it in the `poll_backtest` timeout branch before raising `TimeoutError`. (Confirm endpoint name against QC API — if `backtests/delete` is wrong, use the documented stop/abort endpoint; flag for executor to verify against `https://www.quantconnect.com/docs/v2/cloud-platform/api-reference`.)
- **Bound pagination:** in `read_log` (lines 358-399) the `while offset < total` loop is bounded by `total` but a misbehaving API (`total` huge / `end` not advancing) could spin. Add `MAX_LOG_PAGES = 50` (module constant near line 56-58) and a page counter; break with a stderr warning when exceeded. Also guard against `end == offset` non-advance.

**(b) Rationale sanitize — `CachingMarketDataService`:**
- Add `private static string SanitizeRationale(string? raw)` that: returns `"none"` for null/empty; strips control chars (`char.IsControl`, keep nothing or replace with space); collapses whitespace; caps to ~500 chars (append `"…"` when truncated). Apply at the **log site** (line 79-81) — pass `SanitizeRationale(claudeRegime.Rationale)` into the structured log. Do **not** mutate the stored `MarketRegime.Rationale` (keep the model value intact; only the App Insights log is sanitized). *Decision flag:* if downstream also logs the raw rationale elsewhere, sanitize there too — grep confirms the only log is line 79.

**(c) Bulk fan-out — `CachingMarketDataService.GetIndicatorsBulkAsync` (lines 154-163):**
- Replace the sequential `foreach { result[symbol] = await GetIndicatorsAsync(...) }` with a fan-out that preserves mapping:
  ```csharp
  var symbolList = symbols.ToList();
  var tasks = symbolList.Select(s => GetIndicatorsAsync(s, ct)).ToList();
  var results = await Task.WhenAll(tasks);
  return symbolList.Zip(results, (s, r) => (s, r)).ToDictionary(x => x.s, x => x.r);
  ```
  Note `GetIndicatorsAsync` caches and computes per symbol; concurrent calls are safe (`ConcurrentDictionary`). Preserve the symbol→result mapping by zipping the ordered input list with the ordered `Task.WhenAll` results (order is guaranteed). De-dup symbols defensively if needed.

**Test Plan (TDD RED):**
- **C# (in `CachingMarketDataServiceTests.cs`):**
  1. `GetIndicatorsBulkAsync_IssuesConcurrentCalls` — mock `GetHistoricalBarsAsync` per symbol with a small delay + a shared counter that records max concurrency; assert observed concurrency > 1 for ≥3 symbols (or assert total wall-time < sum-of-delays as a proxy). Keep robust/non-flaky (use a `TaskCompletionSource` gate so all tasks are in-flight before release).
  2. `GetIndicatorsBulkAsync_PreservesPerSymbolMapping` — 3 symbols with distinct bar sets ⇒ each `result[sym].Symbol == sym` and SMA values correspond to that symbol's bars (extends the existing `GetIndicatorsBulkAsync_CallsForEachSymbol` test).
  3. `SanitizeRationale_StripsControlChars_AndTruncates` — input with ` `, `\n`, `\t`, and a 1000-char string ⇒ no control chars, length ≤ 501 (500 + ellipsis), null ⇒ `"none"`. (Requires `SanitizeRationale` to be reachable — make it `internal static` so `InternalsVisibleTo("TradingSystem.Tests")` (already on Strategies csproj) exposes it.)
- **Python (in `tools/backtest/`):** add `tools/backtest/tests/test_run_cloud_backtest.py` (pytest). Confirm whether a Python test harness/`requirements.txt` already exists; if not, add a minimal `pytest` dev dep and note it. Tests use a fake/mock `httpx` transport or monkeypatch `QCClient._post`:
  4. `test_poll_timeout_aborts_backtest` — monkeypatch `_post` so `backtests/read` never reports `completed`, force `BACKTEST_TIMEOUT_S`/deadline tiny (monkeypatch the module constant), assert `abort_backtest` (i.e. a `backtests/delete` POST) is invoked before `TimeoutError` propagates. Verify via mock call record.
  5. `test_read_log_bounded_by_max_pages` — make `total` huge and `_post` return a fixed page; assert the loop stops at `MAX_LOG_PAGES` and emits the warning.
  6. (dry-run flavor) `test_abort_is_best_effort` — `abort_backtest` swallows a `_post` exception and still raises the original `TimeoutError`.

**Integration/E2E:** None (no live QC run in CI; mocked transport only).
**Post-Merge Validation:** Optional manual: trigger a real cloud run and confirm a forced timeout aborts the node (do not run automatically — costs money). Mark as manual-only.
**Files:** Create: `tools/backtest/tests/test_run_cloud_backtest.py` (+ minimal pytest dev-dep note if absent). Modify: `tools/backtest/run_cloud_backtest.py` (`abort_backtest`, `MAX_LOG_PAGES`, bounded loop, timeout-abort call); `src/TradingSystem.Strategies/Services/CachingMarketDataService.cs` (`SanitizeRationale` + apply at log site, `Task.WhenAll` bulk fan-out); `tests/TradingSystem.Tests/Options/CachingMarketDataServiceTests.cs`.
**Dependencies:** Touches `CachingMarketDataService` (same file as S2-001/S2-003) and `CachingMarketDataServiceTests.cs`. Land after S2-005 (last in order) so the final file state already has the cache, clamp, and `RegimeSource` changes; the bulk/sanitize edits are in different methods (`GetIndicatorsBulkAsync`, the log site) so conflicts are minimal. Python changes are fully independent.
**Upgrade Impact:** N/A.
**Custom Instructions:** Verify the QC abort endpoint name against current QC API docs before implementing; if no documented cancel exists, `backtests/delete` is the fallback and the comment must say so.
**Branch:** `fix/python-log-hygiene-and-bulk-fanout`
**Commit:** `fix(tooling): cancel QC job on timeout, sanitize rationale logs, parallel bulk indicators`

---

## Cross-item ordering hazards (summary)

| Item | Shared file(s) | Hazard | Mitigation |
|------|----------------|--------|------------|
| S2-003 → S2-001 → S2-006 | `CachingMarketDataService.cs`, `CachingMarketDataServiceTests.cs` | Three items edit the same class/test file | Different methods/regions; sequential order; rebase each on prior merge |
| S2-001 / S2-002 / S2-004 | `ClaudeConfig` (in `IClaudeService.cs` until S2-005) | All add new config properties to the same class | Sequential; small additive diffs |
| S2-002 → S2-004 | `ClaudeService.cs`, `tests/.../AI/ClaudeServiceTests.cs`, test csproj AI ref | S2-004 builds on S2-002's new test file + AI project reference | S2-002 adds the AI project reference and creates the test file; S2-004 extends both |
| **S2-005** | `ClaudeConfig` namespace move + `MarketRegime.Source` type + `cancellationToken` rename | **Rebases over S2-001/002/003/004** — every prior `ClaudeConfig` addition and `using` must move | **Run S2-005 last among C# items**, on top of the merged result of S2-001..004, so the move absorbs all additions once |
| S2-006 | `CachingMarketDataService.cs` | After S2-005's `RegimeSource` change | Land S2-006 last; bulk/sanitize live in different methods than the regime/cache logic |

**Decision points requiring human approval (raised in-spec):**
1. **S2-005(a):** `ClaudeConfig` target namespace — `TradingSystem.Core.Configuration` (recommended, lowest churn) vs `TradingSystem.AI`.
2. **S2-005(b):** scope of the `cancellationToken` parameter rename — all public interfaces vs Claude-adjacent only.
3. **S2-002:** capped-branch signal — typed `ClaudeCostCapExceededException` vs empty-string return (both fail-closed; executor's choice unless human prefers).
4. **S2-006(a):** QC cloud abort endpoint name — verify against live QC API docs.
