# S5 Implementation Specs — Start the Paper-Validation Run

> Sprint S5 specs. Written during the AAM sprint-master SPEC phase (2026-06-10).
> Plan approved 2026-06-10 (8 items). These specs are self-contained for worktree-isolated
> item-executors to implement via strict TDD without further clarification.

**Sprint goal:** Close the end-of-day orchestration gap and the operational wiring (config,
alerting, runbook) so the 12-week SANDBOX paper-validation run can actually start, while
clearing the small debt surfaced by S4 — with zero deterministic-trading, risk-parameter,
sleeve-allocation, or SANDBOX→LIVE changes and zero metered API spend.

## Locked human decisions (do NOT reopen)

1. **Run model:** locally hosted Functions worker (TWS + claude-gateway are loopback-only).
   Recorded as **ADR-031** in **S5-004** (the runbook item owns the ops narrative; S5-001 stays
   pure code — see Default D1).
2. `DirectApiFallbackEnabled` stays **OFF**. Zero metered API spend.
3. Validation clock starts at sprint close — S5-004's final ops step records the start date.
4. EOD stop-trigger check = **existing `RiskManager` evaluation semantics, alert-only**. No new
   halt behavior, no risk-parameter changes.
5. Weekly scorecard cadence day = **Friday** (config-driven default).

## Spec-level defaults chosen (human reviews at APPROVE)

| # | Default | Where |
|---|---------|-------|
| D1 | ADR-031 (local run model) is written in **S5-004**, not S5-001 — it is an ops/deploy decision, and S5-004 already touches DECISIONS-adjacent docs | S5-004 |
| D2 | S5-001 introduces `IEndOfDayService` (Core.Interfaces) + `EndOfDayService` (Functions); `DailyOrchestrator.RunEndOfDay` stays a thin timer wrapper. The EOD spine **reuses `IRiskManager.GetRiskMetricsAsync`** (it already does broker sync, stop evaluation, transition-gated stop alerts, and base-snapshot upsert) and then **enriches** today's snapshot with activity + market context via a second upsert | S5-001 |
| D3 | Broker-connect failure at EOD writes **no snapshot** (never persist stale data) and does **not** throw; it returns a structured failure result (S5-003 alerts on it). Unexpected exceptions still log + rethrow (App Insights failure signal preserved) | S5-001 |
| D4 | Snapshot enrichment (trades/commissions/realized P&L, SPY/VIX/regime) is **best-effort**: enrichment failure logs a warning and keeps the base snapshot — it never discards the RiskManager-persisted snapshot. Market context comes from one `IMarketDataService.GetMarketRegimeAsync` call (cached, gateway-or-rules, zero metered spend) | S5-001 |
| D5 | S5-003 adds a new `IOperationalAlertService` interface implemented by the **existing** `DiscordRiskAlertService` class (same webhook, same config, same named client, same S3-004 hardening). `IRiskAlertService` is **unchanged**, so every existing fake/mock/test stays green. Operational embeds use a warning color (orange `15105570`) vs the risk-stop red | S5-003 |
| D6 | Alert-spam guard = **once per run per failure category** (connect-failure, orchestration-failure), tracked per `runId` invocation — each timer fires once/day, so worst case is 2 ops alerts/day/run | S5-003 |
| D7 | Weekly readiness gating lives **inside `DiscordDailyReportService`** via a new `ReportingConfig` (`Reporting` section, `WeeklyScorecardDay` default `Friday`), not in the orchestrator — `IDailyReportService`'s signature is unchanged. Existing S4-007 smoke fixtures are updated to set `WeeklyScorecardDay = AsOf.DayOfWeek` so the readiness-embed assertions keep exercising the embed path | S5-002 |
| D8 | The daily report is invoked from `EndOfDayService` **after** snapshot persistence, inside its own try/catch — report failure can never fail the EOD run or block the snapshot | S5-002 |
| D9 | OpenTelemetry.Api pinned to **1.15.3** (first patched per GHSA-g94r-2vxg-569j) with a single `PackageReference` in `TradingSystem.Functions.csproj`; Tests and SmokeTest inherit the lift transitively (both project-reference Functions) | S5-005 |
| D10 | The runbook's Run Log section is the **system of record** for the validation start date (decision 3); DECISIONS.md only points at it | S5-004 |
| D11 | KD-001 is worded **"resolved by S5-004 day-0 wiring"** with the date left as a fill-in completed when the day-0 step actually executes at sprint close | S5-006 |
| D12 | S5-007's rename applies the review-judge wording verbatim: literal `"smoke-key-never-used"` → `"test-key"` | S5-007 |

## Dependency / suggested merge order

- **Dependencies:** S5-001 → S5-003 → S5-002 (all three touch `DailyOrchestrator`/`EndOfDayService` — spec'd against post-predecessor state); S5-002 → S5-004 (runbook documents the cadence + example keys); S5-004 → S5-006 (DECISIONS edits reference ADR-031/runbook). S5-005, S5-007, S5-008 independent.
- **Suggested sequence:** S5-008 · S5-005 · S5-007 (independent trivials first, parallelizable) → S5-001 → S5-003 → S5-002 → S5-004 → S5-006.

## Shared grounding (read before all items)

- **The EOD spine already exists in `RiskManager`:** `RiskManager.GetRiskMetricsAsync`
  (`src/TradingSystem.Core/Services/RiskManager.cs:266-344`) performs broker account+position
  sync, daily/weekly/drawdown stop evaluation against config thresholds, **transition-gated**
  stop alerts (`SendStopAlertsIfNeededAsync` at `:392` only alerts when today's persisted
  snapshot does not already carry the triggered flag), and base `DailySnapshot` persistence
  (`PersistSnapshotAsync` at `:420`). The base snapshot does **not** populate
  `RealizedPnL` (hardcoded `0m`), `TradesExecuted`, `CommissionsPaid`, `SPYClose`, `VIXClose`,
  or `MarketRegime` — that is S5-001's enrichment surface.
- **Snapshot upsert is idempotent by date:** `JsonSnapshotRepository.SaveDailySnapshotAsync`
  (`src/TradingSystem.Storage/Repositories/JsonSnapshotRepository.cs:20-30`) replaces the
  existing same-date entry. Same-day re-runs cannot duplicate snapshots, and re-runs cannot
  re-fire stop alerts (flags already persisted → transition gate holds).
- **EOD timer stub:** `DailyOrchestrator.RunEndOfDay`
  (`src/TradingSystem.Functions/DailyOrchestrator.cs:58-83`) is a TODO stub (cron
  `0 30 20 * * 1-5` = 1:30 PM PT). Pre-market broker-connect failure at `:94-99` is log-only.
- **Discord hardening pattern:** `DiscordRiskAlertService`
  (`src/TradingSystem.Functions/DiscordRiskAlertService.cs`) + `DiscordWebhookGuard` — https
  Discord-host allow-list, token-redacted logging, bounded 429/Retry-After retry with
  injectable `_delay`, `Enabled==false` one-time ctor Info + per-call Debug skip (B-006),
  loud `AlertDropped=true` terminal logs. **Reuse, never re-implement.**
- **Daily report:** `DiscordDailyReportService.SendDailyReportAsync(DateTime date, ...)`
  (`src/TradingSystem.Functions/DiscordDailyReportService.cs`) is DI-registered
  (`Program.cs:63-64`) but never invoked by any timer. It already appends an optional
  readiness embed when `ISleeveReadinessScorecardService` is wired, best-effort.
- **DI / config style:** `src/TradingSystem.Functions/Program.cs` — options-bound config
  sections (`Discord`, `IBKR`, …), singletons, named `IHttpClientFactory` clients.
- **Data seams:** `ISnapshotRepository`/`DailySnapshot` (`IRepositories.cs:72-123`),
  `ITradeRepository`/`Trade` (`Trade.cs` — `Commission`, `RealizedPnL`, `EntryTime`,
  `ExitTime`), `IMarketDataService.GetMarketRegimeAsync` (`IBrokerService.cs:57`; the
  `MarketRegime` model carries `VIX`, `SPYPrice`, and the regime classification — one cached
  call covers all three context fields).
- **Current suite:** 551 tests green (S4 close). All items keep it green; S5-001's snapshot
  persistence path is trading-critical → **Rigorous test tier**.

---

### S5-008: .gitignore sprint runtime artifacts
**Approach:** Append four entries to the existing "AAM sprint/session transient state" block in
`/.gitignore` (currently lines 132-139): `.quality-gate-pass`, `.quality-review-result.json`,
`.claude/worktrees/`, `.exec/`. All four are hook/pipeline-generated local-only runtime state
currently showing as untracked noise in `git status`. **None are tracked** (verify with
`git ls-files` before editing) — so the gitignore-safety untracking protocol does not apply;
this is a pure additive ignore.
**Test Plan (TDD RED):** None applicable (ignore-file change, no test-harness binding).
Verification: `git status --porcelain` no longer lists the four paths; `git check-ignore -v
.quality-gate-pass .quality-review-result.json .claude/worktrees/ .exec/` resolves each to the
new lines; `git ls-files` confirms nothing tracked was affected.
**Integration/E2E:** None.
**Post-Merge Validation:** None.
**Files:** Create: none | Modify: `/.gitignore`.
**Dependencies:** None.
**Upgrade Impact:** N/A.
**Custom Instructions:** Add entries inside the existing AAM block (keep the block comment),
not as a new scattered section. Do not touch any other ignore rules.

---

### S5-005: Pin transitive OpenTelemetry.Api past GHSA-g94r-2vxg-569j
**Approach:** `dotnet list TradingSystem.sln package --vulnerable --include-transitive`
currently flags **OpenTelemetry.Api 1.15.0 (Moderate)** in `TradingSystem.Functions`,
`TradingSystem.Tests`, and `TradingSystem.SmokeTest`. Root: `Microsoft.ApplicationInsights.WorkerService 3.0.0`
→ `Azure.Monitor.OpenTelemetry.Exporter 1.6.0` → OpenTelemetry 1.15.0 chain. Advisory
GHSA-g94r-2vxg-569j ("excessive memory allocation when parsing OpenTelemetry propagation
headers") — first patched version **1.15.3** (the sibling `OpenTelemetry.Extensions.Propagators`
package is not in our graph; only `.Api` needs pinning). Add an explicit top-level
`<PackageReference Include="OpenTelemetry.Api" Version="1.15.3" />` to
`src/TradingSystem.Functions/TradingSystem.Functions.csproj` with an XML comment naming the
advisory and why the pin exists (transitive lift — remove once
Microsoft.ApplicationInsights.WorkerService ships a patched chain). Tests and SmokeTest both
project-reference Functions, so the single pin lifts all three flagged projects (Default D9).
**Test Plan (TDD RED):** No unit tests (dependency-graph change). RED/GREEN is the scanner:
1. RED: `dotnet list TradingSystem.sln package --vulnerable --include-transitive` shows the three flagged projects (capture in PR).
2. GREEN after pin: same command reports **no vulnerable packages in any project**.
3. `dotnet build` + full `dotnet test` — all 551 tests stay green (App Insights worker-service telemetry registration in `Program.cs:53-54` still composes).
**Integration/E2E:** None.
**Post-Merge Validation:** First local host start (S5-004 day-0): confirm the Functions worker
boots clean with the pinned assembly (no OTel version-conflict bind failure at startup).
**Files:** Create: none | Modify: `src/TradingSystem.Functions/TradingSystem.Functions.csproj`.
**Dependencies:** None.
**Upgrade Impact:** OpenTelemetry.Api 1.15.0 → 1.15.3 is a patch-level lift on a transitive
package the repo never calls directly. Integration points to verify: (a) Application Insights
worker-service pipeline still registers (`AddApplicationInsightsTelemetryWorkerService` /
`ConfigureFunctionsApplicationInsights`); (b) no NuGet downgrade warnings (NU1605/NU1608) in
the restore log; (c) the vulnerable scan is clean for **all** projects including Tests and
SmokeTest (if either ever drops its Functions reference, it needs its own pin — note this in
the csproj comment).
**Custom Instructions:** Pin exactly 1.15.3 (smallest patched), not "latest" — minimal-delta
chore. Do not bump Microsoft.ApplicationInsights.WorkerService itself in this item.

---

### S5-007: S4-007 deferred cosmetic test polish
**Approach:** Apply the seven review-judge deferred items from S4-007 (PR #85). **Test tree
only** — zero `src/` changes.
1. `tests/TradingSystem.SmokeTest/Program.cs:33` header banner (and the section banner at
   `:408-414`): annotate that tests **15-17 are inert** — no TWS, no live HTTP — so a manual
   runner knows the count includes non-TWS checks (e.g. `=== IBKR Smoke Test (17 tests; 15-17
   inert — no TWS) ===`).
2. `Program.cs:417-418`: add a cross-link comment that `firstIndex: 15` and `totalTests = 17`
   (`:29`) must move together — 3 readiness checks occupy slots 15-17; renumber both when
   adding TWS tests.
3. `tests/TradingSystem.SmokeTest/ReadinessSmokeSection.cs:27`: comment explaining why
   `DateTime.Today` is correct here (manual console harness — current-day realism wanted)
   versus the fixed `AsOf = 2026-06-08` in the xUnit twin (CI determinism).
4. `ReadinessSmokeSection.cs:95`: split the merged OK line into two `Console.WriteLine`s — one
   fact per OK line ("payload captured by stub handler (N chars)" / "summary + readiness
   embeds present"), matching the section's one-check-per-line style.
5. `ReadinessSmokeSection.cs:117`: rename the literal `"smoke-key-never-used"` → `"test-key"`
   (Default D12 — review-judge wording verbatim; the adjacent comment already documents that
   the key is never used).
6. `tests/TradingSystem.Tests/Functions/SandboxReadinessSmokeTests.cs`: add a **log-scrub
   assertion** — swap the report-service logger in `SmokeFixture` for a small capturing
   `ILogger` fake and assert the fake-webhook token segment (`inert-smoke-token`, from
   `FakeWebhookUrl` at `:36`) never appears in any captured log line across the smoke runs.
7. `SandboxReadinessSmokeTests.cs:419-423` `CountingConfigRepository.SaveConfigAsync`: persist
   the passed config (store it so a subsequent `GetConfigAsync` returns what was saved) while
   still counting — the counter-only stub silently drops state and could mask a write-then-read
   bug in a future smoke extension.
**Test Plan (TDD RED):** Items 6-7 are the assertable changes — write them RED first:
1. New log-scrub test fails before the capturing logger exists / passes after (and stays a
   permanent token-leak tripwire on the report path).
2. New `CountingConfigRepository` round-trip check: `SaveConfigAsync(modified)` then
   `GetConfigAsync()` returns the modified instance AND `SaveConfigCount == 1` (fails against
   the current drop-on-save stub).
Items 1-5 are comments/strings: verified by compile + an unchanged pass/fail count when the
console harness builds (`dotnet build tests/TradingSystem.SmokeTest`).
**Integration/E2E:** None (the items polish the existing E2E harness itself).
**Post-Merge Validation:** Optional manual console-harness run against paper TWS to eyeball
the new banner/OK-line output. Deferred/manual.
**Files:** Create: none | Modify: `tests/TradingSystem.SmokeTest/Program.cs`,
`tests/TradingSystem.SmokeTest/ReadinessSmokeSection.cs`,
`tests/TradingSystem.Tests/Functions/SandboxReadinessSmokeTests.cs`.
**Dependencies:** None. (S5-002 later edits the same smoke files for cadence config — merge
S5-007 first per the sequence so S5-002 rebases on the polished text.)
**Upgrade Impact:** N/A.
**Custom Instructions:** Cosmetic only — no behavior change to any `src/` code, no new prod
types. All existing safety assertions (no LIVE switch, no order placement, no live POST,
inert AI) must remain byte-for-byte intact. Suite stays green (551 + the 2 new assertions).

---

### S5-001: Implement EOD orchestrator pipeline — position/P&L sync, DailySnapshot persistence, stop-trigger check
**Approach:** Create `IEndOfDayService` in `src/TradingSystem.Core/Interfaces/` with
`Task<EndOfDayResult> RunAsync(string runId, CancellationToken ct)` and `EndOfDayService` in
`src/TradingSystem.Functions/` (Default D2). `EndOfDayResult` (small POCO next to the
interface): `BrokerConnected`, `SnapshotPersisted`, `StopTriggered`, `Warnings` — S5-003
alerts off it and tests assert on it. Pipeline:
1. **Connect:** resolve `IBrokerService`; `ConnectAsync`. On failure → log warning, return
   `EndOfDayResult { BrokerConnected = false }` — **no snapshot write, no throw** (Default D3;
   mirrors the pre-market degrade at `DailyOrchestrator.cs:94-99`; S5-003 adds the alert).
2. **Sync + stop check + base snapshot:** call `IRiskManager.GetRiskMetricsAsync(ct)` — per
   locked decision 4 this IS the stop-trigger check: existing `RiskManager` evaluation
   semantics, transition-gated alerts via the existing `IRiskAlertService`, **alert-only**
   (no halt action, no order, no config write), and it upserts the base `DailySnapshot`.
   Do not duplicate any of that logic in `EndOfDayService`.
3. **Enrich (best-effort, Default D4):** `GetSnapshotAsync(today)` to read the base snapshot
   back, then populate the fields `RiskManager` leaves empty: `TradesExecuted` +
   `CommissionsPaid` from `ITradeRepository.GetByDateRangeAsync(today, today)` (count;
   `Sum(t => t.Commission ?? 0m)`); `RealizedPnL` from the day's closed trades
   (`ExitTime?.Date == today`, `Sum(t => t.RealizedPnL ?? 0m)`); `SPYClose`/`VIXClose`/
   `MarketRegime` from **one** `IMarketDataService.GetMarketRegimeAsync(ct)` call (S2-001
   cache; gateway-or-rules per ADR-029/030 — zero metered spend with the fallback off). Upsert
   the enriched snapshot via `SaveDailySnapshotAsync` (idempotent replace-by-date). Any
   enrichment failure → log warning, keep the base snapshot, continue.
4. **Disconnect** in `finally` after a successful connect.
`DailyOrchestrator.RunEndOfDay` (`:58-83`): replace the TODO with resolve-and-call (same
`GetService` null-tolerant style as pre-market), keep the existing catch/log/rethrow.
Register `IEndOfDayService` → `EndOfDayService` singleton in `Program.cs`. **Idempotent
same-day re-run** falls out of existing seams: snapshot upsert replaces by date; the
transition gate in `SendStopAlertsIfNeededAsync` reads today's already-persisted flags so
alerts don't re-fire.
**Test Plan (TDD RED)** — Rigorous tier (trading-critical persistence path); use the REAL
`RiskManager` with mocked `IBrokerService`/`ICalendarService` + an in-memory
`ISnapshotRepository` where transition/idempotency semantics matter:
1. Happy path: connect succeeds → exactly one `GetRiskMetricsAsync`-driven base persist, then the enriched snapshot for today carries `TradesExecuted`, `CommissionsPaid`, `RealizedPnL`, `SPYClose`, `VIXClose`, `MarketRegime` from the seeded fakes; `EndOfDayResult.SnapshotPersisted == true`.
2. Broker connect fails → **zero** snapshot writes, no throw, `BrokerConnected == false`, warning logged, no market-data/trade-repo calls.
3. **Idempotent re-run:** run twice same day → snapshot store holds exactly **one** entry for the date (replaced, not duplicated), and the `IRiskAlertService` mock receives **zero** additional stop alerts on the second run when the first persisted triggered flags (transition gate).
4. Stop-trigger day (seed account P&L below `-DailyStopPercent`): exactly one daily-stop alert via the existing `IRiskAlertService`; **alert-only** — no `IExecutionService` call, no order, no `IConfigRepository` write, run completes normally.
5. Enrichment failure (market-data fake throws): base snapshot remains persisted, run completes, `Warnings` non-empty, warning logged — degrade never loses the snapshot.
6. `DisconnectAsync` is called exactly once after a successful connect, including when enrichment throws (finally semantics).
7. Zero-trade day: enriched snapshot has `TradesExecuted == 0`, `CommissionsPaid == 0m`, `RealizedPnL == 0m` — no null/empty-collection throw.
8. Orchestrator wiring: `RunEndOfDay` with `IEndOfDayService` unregistered degrades like pre-market (log + return, no throw); with it registered, delegates once with the runId.
**Integration/E2E:** Covered by the updated smoke path in S5-002 (report wiring) — no live
TWS in CI. Optional manual: console smoke harness against paper TWS post-sprint.
**Post-Merge Validation:** First real scheduled EOD run after S5-004 day-0 (local host, paper
TWS live): verify `data/snapshots.json` gains an enriched entry for the day. Deferred/manual —
recorded as a runbook day-1 check, does not gate the PR.
**Files:** Create: `src/TradingSystem.Core/Interfaces/IEndOfDayService.cs` (interface +
`EndOfDayResult`), `src/TradingSystem.Functions/EndOfDayService.cs`,
`tests/TradingSystem.Tests/Functions/EndOfDayServiceTests.cs` | Modify:
`src/TradingSystem.Functions/DailyOrchestrator.cs`, `src/TradingSystem.Functions/Program.cs`
(DI registration).
**Dependencies:** None (first feature in sequence). S5-003 and S5-002 build on this file state.
**Upgrade Impact:** N/A.
**Custom Instructions:** Locked decision 4 is the contract: the stop check is
`GetRiskMetricsAsync` as-is — do NOT add halt behavior, do NOT touch `RiskConfig`, do NOT
re-implement stop math in `EndOfDayService`. This item feeds the readiness gate (S4-001/002)
and ADR-025 drawdown baselines their first scheduled data — snapshot field fidelity matters
more than breadth; leave a field default-valued rather than guessing a source. Existing
`RiskManager` tests stay green unchanged. Do not wire the daily report here (S5-002).

---

### S5-003: Operational failure alerting — Discord alert on broker-connect / orchestration failure
**Approach:** (Spec'd against post-S5-001 state.) New `IOperationalAlertService` in
`src/TradingSystem.Core/Interfaces/`: `Task SendOperationalAlertAsync(string title, string
description, CancellationToken ct = default)`. Implement it on the **existing**
`DiscordRiskAlertService` (class implements both interfaces — Default D5): the new method
funnels into the existing private `SendAlertAsync`/`PostWithRetryAsync` machinery (same
webhook, same `DiscordConfig`, same `DiscordRiskAlerts` named client, same host allow-list /
token redaction / bounded 429 retry / `Enabled==false` Debug skip). Only deltas: operational
embeds use warning-orange color `15105570` (risk stops keep red `15158332`), and the metrics
fields block is replaced by the description (no `RiskMetrics` on this path — refactor
`SendAlertAsync` minimally, e.g. an embed-payload overload; existing risk-alert payloads stay
byte-identical). Terminal failures reuse the loud `AlertDropped=true` pattern with a softer
message (an ops alert is not a capital-preservation stop). Register in `Program.cs` so both
interfaces resolve to the SAME singleton instance. Wire three call sites (all best-effort —
the alert sender never throws; per Default D6, once per run per failure category):
1. `DailyOrchestrator.RunPreMarket` broker-connect failure (`RunOptionsSleeveAsync` `:94-99`,
   currently log-only) → connect-failure alert with runId + "pre-market" context.
2. `EndOfDayService` broker-connect failure (S5-001's `BrokerConnected == false` path) →
   connect-failure alert with runId + "end-of-day" context.
3. Both timer catch blocks (`RunPreMarket` `:48-52`, `RunEndOfDay` `:79-83`) → orchestration-
   failure alert (exception **type name only** — never `ex.Message`/`ToString()`, which can
   echo URIs/secrets; matches the S3-004 transport-failure logging stance) **before** the
   existing rethrow.
**Test Plan (TDD RED):**
1. Pre-market connect failure → exactly one operational alert POST (stub handler), embed titled as connect failure, contains runId, color 15105570; run still degrades exactly as before (options sleeve skipped, no throw).
2. EOD connect failure → one alert; S5-001 regression intact: no snapshot write, no throw.
3. `RunEndOfDay` body throws → operational alert sent AND the exception still propagates (rethrow preserved — assert via `Assert.ThrowsAsync` + handler invocation count 1).
4. `Enabled == false` → per-call Debug skip, zero POSTs (B-006 pattern holds on the new path).
5. Alert transport failure (`HttpRequestException` from the stub) → swallowed by the alert service; the orchestrator outcome is unchanged (no secondary failure).
6. Same run / same category fires at most once (invoke the failure path twice within one run scope → one POST).
7. Token redaction on the new path: capturing logger sees no webhook token segment on a forced delivery failure.
8. Regression: existing risk-stop alert payloads byte-identical (existing `DiscordRiskAlertServiceTests` green **unchanged**); a risk-stop alert still renders red with metrics fields.
**Integration/E2E:** None live (webhook HTTP stubbed; loopback posture). The S5-004 day-0 test
alert is the live proof.
**Post-Merge Validation:** S5-004 day-0 step sends a real test alert through this path to the
provisioned webhook and confirms Discord delivery/rendering.
**Files:** Create: `src/TradingSystem.Core/Interfaces/IOperationalAlertService.cs`,
`tests/TradingSystem.Tests/Functions/OperationalAlertTests.cs` | Modify:
`src/TradingSystem.Functions/DiscordRiskAlertService.cs`,
`src/TradingSystem.Functions/DailyOrchestrator.cs`,
`src/TradingSystem.Functions/EndOfDayService.cs`,
`src/TradingSystem.Functions/Program.cs` (second-interface registration),
`tests/TradingSystem.Tests/Functions/DiscordRiskAlertServiceTests.cs` (only if the minimal
payload-overload refactor requires a shared helper touch — behavior must stay identical).
**Dependencies:** **S5-001** (wires into `EndOfDayService` + the orchestrator file S5-001
edits). Sequence S5-001 → S5-003.
**Upgrade Impact:** N/A.
**Custom Instructions:** No new transport, no new secret, no new config key —
`DiscordConfig` as-is. `IRiskAlertService` is frozen (NoOp fallback in `RiskManager`, mocks
across the suite). Never log or embed exception messages on this path — type names + runId
only. Alerting is observability: it must never change a trading decision or halt anything.

---

### S5-002: Wire daily Discord report + weekly readiness-scorecard cadence into EOD run
**Approach:** (Spec'd against post-S5-003 state.) Two changes:
1. **Invocation (Default D8):** in `EndOfDayService.RunAsync`, after snapshot persistence
   (and only when a snapshot was persisted — no report for a skipped/degraded run), call
   `IDailyReportService.SendDailyReportAsync(today, ct)` inside its own try/catch: any
   exception → log warning, append to `EndOfDayResult.Warnings`, run still succeeds. The
   service itself already degrades-without-throw on delivery failure; the try/catch is
   belt-and-braces so a future implementation bug still can't fail the EOD run. Inject
   `IDailyReportService` as optional (null-tolerant, matching the scorecard-service pattern)
   so partial DI registration degrades gracefully.
2. **Weekly cadence (Default D7, locked decision 5):** new `ReportingConfig` in
   `src/TradingSystem.Core/Configuration/` — `public DayOfWeek WeeklyScorecardDay { get; set; }
   = DayOfWeek.Friday;` — bound from the `Reporting` section in `Program.cs`.
   `DiscordDailyReportService` takes `IOptions<ReportingConfig>` (optional ctor param
   defaulting to `Defaults` so existing construction sites compile) and appends the readiness
   embed **only when** `date.DayOfWeek == WeeklyScorecardDay` (and the scorecard service is
   wired). Core report unchanged on all other days. `Discord:Enabled=false` behavior untouched
   — one-time ctor Info + per-call Debug skip (S4-005/B-006 preserved). Add
   `"Reporting:WeeklyScorecardDay": "Friday"` to
   `src/TradingSystem.Functions/local.settings.json.example`.
   **Required fixture updates:** `SandboxReadinessSmokeTests.SmokeFixture` (AsOf 2026-06-08, a
   Monday) and `ReadinessSmokeSection` (AsOf = today) must set
   `WeeklyScorecardDay = AsOf.DayOfWeek` so their readiness-embed assertions keep exercising
   the embed path.
**Test Plan (TDD RED):**
1. Report service throws (faulting fake) → EOD run completes successfully, snapshot remains persisted, warning logged + surfaced in `EndOfDayResult.Warnings`.
2. Happy path: `SendDailyReportAsync` invoked exactly once per EOD run, with today's date, **after** the enriched-snapshot upsert (assert via recording fakes/ordering).
3. Degraded run (broker connect failed → no snapshot) → report NOT sent.
4. `date.DayOfWeek == WeeklyScorecardDay` → payload contains the readiness embed (existing S4-003 embed content assertions reused).
5. Any other day → readiness embed absent; core report fields (trades, P&L, positions, regime, ADR-023 cost breakout) all present.
6. Unconfigured `Reporting` section → default is Friday (locked decision 5).
7. `Discord:Enabled=false` → per-call Debug skip, zero POSTs, EOD run unaffected (S4-005 regression).
8. Updated smoke fixtures (xUnit + console section) pass with the cadence config set to AsOf's weekday; full suite green.
**Integration/E2E:** Extend `SandboxReadinessSmokeTests` minimally: the EOD-path test (S5-001's
seam) sends the report through the recording stub on a scorecard day — still zero live POSTs,
zero TWS, AI inert.
**Post-Merge Validation:** First scheduled Friday EOD after day-0: confirm the Discord message
carries the readiness scorecard section; a non-Friday EOD carries only the core digest.
Recorded as a runbook day-1/week-1 check.
**Files:** Create: `src/TradingSystem.Core/Configuration/ReportingConfig.cs` | Modify:
`src/TradingSystem.Functions/EndOfDayService.cs`,
`src/TradingSystem.Functions/DiscordDailyReportService.cs`,
`src/TradingSystem.Functions/Program.cs` (bind `Reporting`, pass report service into EOD),
`src/TradingSystem.Functions/local.settings.json.example`,
`tests/TradingSystem.Tests/Functions/DiscordDailyReportServiceTests.cs`,
`tests/TradingSystem.Tests/Functions/EndOfDayServiceTests.cs`,
`tests/TradingSystem.Tests/Functions/SandboxReadinessSmokeTests.cs`,
`tests/TradingSystem.SmokeTest/ReadinessSmokeSection.cs`.
**Dependencies:** **S5-001** (EOD seam), **S5-003** (same orchestrator/EOD file state).
Sequence S5-001 → S5-003 → S5-002. Merge after S5-007 (same smoke files — rebase on the
polished text).
**Upgrade Impact:** N/A.
**Custom Instructions:** Report failure must NEVER fail the EOD run or block snapshot
persistence — that asymmetry (snapshot = trading-critical, report = observability) is the
core invariant under test. No new secret; same webhook. `IDailyReportService`'s signature is
frozen. `ReportingConfig` is operational cadence config, NOT risk config — it must not touch
`TradingSystemConfig`/`RiskConfig`.

---

### S5-004: Paper-validation launch runbook + day-0 config wiring (webhook, gateway preflight)
**Approach:** Three parts — docs, ADR, and a human-executed day-0 ops checklist.
1. **Runbook** at `docs/paper-validation-runbook.md`:
   - **Run model (ADR-031):** locally hosted Azure Functions isolated worker
     (`func start` / `dotnet run` from `src/TradingSystem.Functions`) on the dev box — TWS
     (paper, port 7497) and claude-gateway (`localhost:3131`) are loopback-only, so the worker
     must share the host. Machine-on requirement during market hours; what happens on a missed
     run (timers don't catch up — a gap day is a missing snapshot, triage section covers it).
   - **Daily preflight:** TWS paper running + API enabled (port 7497, trusted IP 127.0.0.1);
     claude-gateway process up (health check curl; on miss the system degrades to
     deterministic rules — ADR-029/030, not an incident); Functions worker started; smoke
     check that the two timers are registered in startup logs.
   - **Timer schedule:** pre-market 6:00 AM PT (`0 0 13 * * 1-5` UTC) and EOD 1:30 PM PT
     (`0 30 20 * * 1-5` UTC) — note the crons are UTC-fixed, so PT wall-clock shifts ±1h
     across DST transitions (document, don't change).
   - **Discord message meanings:** table mapping each message type → meaning → operator
     action: risk-stop alerts (red; capital-preservation, alert-only in paper), operational
     alerts (orange; connect/orchestration failure → triage), daily EOD digest (core
     P&L/activity/ADR-023 cost breakout), Friday readiness scorecard section (PDR-004 gate
     progress), silence cases (`Discord:Enabled=false`, dropped-alert `AlertDropped=true`
     log signature).
   - **Failure triage:** no daily report received; operational connect-failure alert;
     gateway down (expected: rules fallback, regime source logged); TWS not running; worker
     not running (no alert possible — the dead-man case; check this first each morning).
   - **Locked posture statement:** SANDBOX only, `DirectApiFallbackEnabled=false` (zero
     metered spend), alert-only stops, no risk-parameter/sleeve-weight changes, SANDBOX→LIVE
     requires explicit human approval — restating CLAUDE.md trading rules as ops policy.
   - **Day-0 checklist + Run Log:** the checklist below, plus a Run Log table whose first row
     records the validation **start date** (Default D10; locked decision 3 — filled at sprint
     close, target ≥12 weeks per ADR-010/PDR-004).
2. **ADR-031** appended to `DECISIONS.md` (next free number after ADR-030): "Paper-Validation
   Run Hosting — Locally Hosted Functions Worker". Rationale: TWS and claude-gateway are
   loopback-only (ADR-029); the worker must be co-resident. Alternatives considered: *Azure-
   hosted Functions* — REJECTED (cannot reach loopback-bound TWS/gateway; exposing either
   off-box violates the ADR-029 transport stance); *relay/tunnel to Azure* — DEFERRED
   (operational complexity + new attack surface for zero validation benefit); *dedicated
   always-on VM hosting all three* — DEFERRED (cost; revisit if dev-box uptime proves
   inadequate mid-run). Consequence: validation continuity depends on dev-box uptime; missed
   days are visible as snapshot gaps and tolerated by the ≥12-week window.
3. **Config wiring:** verify `local.settings.json.example` carries every key the run needs
   (S5-002 added `Reporting:WeeklyScorecardDay`; confirm `Discord`, `IBKR`, `Claude`,
   `LocalStorage`/storage section as actually bound by `Program.cs` — placeholders only,
   never real values).
4. **Day-0 ops steps** (human/sprint-master at sprint close — Post-Merge Validation, not
   executor code): (a) retrieve the real Discord webhook URL from the Bitwarden **"ClimbOn
   Co"** org vault into the gitignored `local.settings.json` (`.gitignore:16`) —
   **the URL must NEVER be committed, logged, or echoed**; (b) start the gateway + worker,
   send a test operational alert through the S5-003 path and confirm Discord delivery;
   (c) confirm gateway preflight (regime call resolves gateway-or-rules); (d) record the
   start date in the runbook Run Log; (e) close KD-001's fill-in date (S5-006 wording).
**Test Plan (TDD RED):** None applicable (docs + ADR + ops checklist; no production code).
Verification: runbook sections complete per this spec; ADR-031 number is the next free one;
`local.settings.json.example` diff contains placeholders only (reviewer greps the diff for
`discord.com/api/webhooks` and real-looking tokens — must be none); markdown renders.
**Integration/E2E:** None.
**Post-Merge Validation:** The day-0 checklist itself (step 4 above) — executed once at sprint
close. Success = test alert visible in Discord, gateway preflight logged, start date recorded.
**Files:** Create: `docs/paper-validation-runbook.md` | Modify: `DECISIONS.md` (ADR-031),
`src/TradingSystem.Functions/local.settings.json.example` (only if a bound key is missing
after S5-002).
**Dependencies:** **S5-001/S5-002/S5-003** (documents their behavior — write after they
merge). Sequence after S5-002.
**Upgrade Impact:** N/A.
**Custom Instructions:** Locked decisions 1-3 verbatim: local hosting (ADR-031 here per
Default D1), fallback stays OFF (the runbook must NOT contain a "turn on the metered
fallback" remediation), start date at sprint close. Webhook secrecy is absolute — Bitwarden →
gitignored file only; per user-global rules never print the secret in any response/log/diff.
Grounded, non-superlative tone per repo rules.

---

### S5-006: DECISIONS.md hygiene — mark PDR-004 resolved; re-scope KD-001/KD-002
**Approach:** Three surgical edits to `DECISIONS.md`, mirroring the existing PDR-002 style:
1. **PDR-004** (`Pending Decisions`): retitle `### PDR-004: ~~Sleeve-Level Validation
   Thresholds for Live Activation~~ RESOLVED` and replace the body with a Resolution line:
   resolved in code by S4-001 (PR #81), owner-confirmed 2026-06-09 — per-sleeve paper gate:
   hit rate ≥45%, profit factor ≥1.3, max drawdown ≤15%, ≥12 weeks observed,
   profitable-OR-beat-SPY (strict >), $100k minimum live capital per sleeve; evaluation-only
   via the `IConfigRepository` settings seam (`SleeveValidationThresholds`), never on any
   order/execution path; consumed by the S4-002 scorecard. Note PDR-005 remains pending
   (live sleeve set/capital split) — do not touch it.
2. **KD-001** (Known Debt table): mark resolved-by-S5-004 with conditional wording (Default
   D11): `✅ RESOLVED by S5-004 day-0 wiring (date: ____ at sprint close) — webhook
   provisioned from Bitwarden "ClimbOn Co" into gitignored local.settings.json; test alert
   delivered (see docs/paper-validation-runbook.md Run Log)` with the original text
   struck through. The blank is filled when day-0 executes.
3. **KD-002**: keep the row, annotate (not resolve): metered Claude API key NOT required
   under the default posture — ADR-029/030 gateway-or-rules with
   `DirectApiFallbackEnabled=false` means zero metered spend; the key becomes relevant only
   if the owner explicitly enables the fallback. Impact column drops "blocked" — regime
   integration shipped and degrades to deterministic rules.
**Test Plan (TDD RED):** None applicable (docs-only). Verification: diff touches exactly the
PDR-004 entry + two Known Debt rows; PDR-002 formatting precedent matched; no ADR bodies
altered; markdown table intact.
**Integration/E2E:** None.
**Post-Merge Validation:** Fill KD-001's date blank when the S5-004 day-0 step completes
(single-line follow-up commit at sprint close, covered by the close-out flow).
**Files:** Create: none | Modify: `DECISIONS.md`.
**Dependencies:** **S5-004** (KD-001 wording points at the runbook + day-0 step; ADR-031 must
already exist so DECISIONS.md edits don't collide). Sequence last.
**Upgrade Impact:** N/A.
**Custom Instructions:** Hygiene only — record what already happened; make no new decisions.
Threshold values must match the S4 close-out record verbatim (SPRINT.md S4 archive,
"Decisions logged"). KD-002 is re-scoped, not deleted — the fallback path still exists behind
the flag.

---

## Cross-cutting invariants (all items)

- **Deterministic trading logic untouched.** No item adds, removes, or reorders a trading
  decision. S5-001's stop check delegates to existing `RiskManager` semantics — alert-only,
  no halt behavior (locked decision 4).
- **AI is analysis-only**, and the gateway-or-rules fallback contract (ADR-029/030) is
  unchanged: the EOD regime enrichment uses the cached provider; a gateway miss degrades to
  deterministic rules with zero metered spend (`DirectApiFallbackEnabled` stays false).
- **No SANDBOX→LIVE**, no risk-parameter or sleeve-weight changes, anywhere — including
  tests and the runbook (which restates this as ops policy).
- **Reporting/alerting is observability, never control:** report or alert failure must never
  fail the EOD run, block snapshot persistence, or influence any trading path.
- **Secrets:** the Discord webhook URL lives only in Bitwarden and the gitignored
  `local.settings.json`; token redaction (`DiscordWebhookGuard`) covers every new send path;
  S5-007 adds a permanent log-scrub tripwire.
- **Suite:** all 551 existing tests stay green unchanged (notably `RiskManager`,
  `DiscordRiskAlertService`, `DiscordDailyReportService`, `ClaudeService`, and the S4-007
  smoke fixtures except the explicitly spec'd cadence updates). Snapshot persistence
  (S5-001) is tested at the **Rigorous** tier with the real `RiskManager`.
