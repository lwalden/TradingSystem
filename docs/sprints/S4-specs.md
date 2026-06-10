# S4 Implementation Specs — Paper-Trading Readiness

> Sprint S4 specs. Written during the AAM sprint-master SPEC phase (2026-06-10).
> Scope approved by owner as-is (7 items). **Execution deferred:** the remote planning
> environment cannot install the .NET SDK (network allowlist blocks all SDK channels),
> so EXECUTE/TEST/REVIEW (build + full test suite per CLAUDE.md) must run in a
> toolchain-capable environment. These specs are written to be self-contained for an
> item-executor to implement via strict TDD without further clarification.

**Sprint goal:** Make the system paper-validation-ready — define the numeric per-sleeve
paper gate (PDR-004), ship the Week-10 reporting digest, harden the gateway/regime AI
path, and clear low-risk operability debt — with zero deterministic-trading,
risk-parameter, or SANDBOX→LIVE changes.

## ⚠ Blocking human sign-offs before EXECUTE (do NOT guess)

1. **S4-001 threshold numbers + $100k minimum-capital (S4-002).** PDR-004 leaves the exact
   per-sleeve numeric pass/fail criteria undefined; ADR-010 provides only *informational*
   metrics (hit rate ≥45%, profit factor ≥1.3, max drawdown ≤15%) and a "profitable OR
   beat SPY" gate with **no** SPY margin. Per CLAUDE.md, threshold values that influence a
   go-live recommendation require explicit human approval. Executor implements the ADR-010
   numbers as documented placeholders behind a `Defaults()` factory; the PR must request
   owner confirmation/override before they are authoritative. Do **not** invent a beat-SPY
   margin.
2. **S4-004 gateway field name/shape.** The `claude-gateway` `INTEGRATION.md`
   structured-output section is **not in this repo**. Confirm the request field name
   (`jsonSchema` vs `json_schema`) and the `response`-as-JSON-string wrapping against the
   live gateway doc before merge; until confirmed, gate the new parse behind
   schema-presence so the legacy path stays default and nothing regresses.

## Dependency / suggested merge order

- **Dependencies:** S4-001 → S4-002 → S4-007; S4-003 → S4-007. S4-004, S4-005, S4-006 independent.
- **Suggested sequence:** S4-006 (trivial) · S4-005 · S4-001 · S4-004 (parallelizable) → S4-002 → S4-003 → S4-007 (last).

## Shared grounding (read before all items)

- **Cosmos config seam:** `IConfigRepository` (`src/TradingSystem.Core/Interfaces/IRepositories.cs:128`);
  local impl `JsonConfigRepository` (`src/TradingSystem.Storage/Repositories/JsonConfigRepository.cs`)
  via `GetSettingAsync<T>/SetSettingAsync<T>`. New config objects go through this seam — do **not**
  add fields to risk-bearing `RiskConfig`/`IncomeConfig`/`OptionsConfig` in `TradingSystemConfig.cs`.
- **Readiness data sources (read-only):** `ISnapshotRepository` (`DailySnapshot` with
  `IncomeSleeveValue`/`TacticalSleeveValue`/`MaxDrawdown`/`SPYClose`, `IRepositories.cs:72`) and
  `ITradeRepository.GetStatisticsAsync` (`TradeStatistics.WinRate`/`ProfitFactor`/`TotalPnL`,
  `IRepositories.cs:21`).
- **Discord posting pattern:** `src/TradingSystem.Functions/DiscordRiskAlertService.cs` (host
  allow-list, token redaction, 429/Retry-After bounded retry, injectable `_delay`, named
  `IHttpClientFactory`). B-004 reuses this; there is no pre-existing daily-report service.
- **Gateway/AI seam:** `ClaudeService.AnalyzeAsync<T>` (`src/TradingSystem.AI/Services/ClaudeService.cs:146`);
  `GatewayResponse.Response` (`:262`); request shape posted at `:176`. Fallback-to-rules contract
  (ADR-029/030) must be preserved exactly.
- **All recommendation items (S4-001/002/007)** are read-only / recommendation-only: no writes to
  `TradingSystemConfig.Mode`, `RiskConfig`, sleeve weights, or order placement.

---

### S4-001: Numeric per-sleeve paper-validation thresholds (PDR-004), config-driven
**Approach:** Create a new POCO `SleeveValidationThresholds` (per-sleeve: `Income`, `Options`) in
`src/TradingSystem.Core/Configuration/` holding the read-only evaluation metrics from ADR-010 —
`MinHitRatePercent`, `MinProfitFactor`, `MaxDrawdownPercent`, `MinWeeksObserved`, and a
`RequireProfitableOrBeatSpy` flag (mirrors ADR-010's "Profitable OR outperform S&P 500"). Persist/retrieve
via the existing `IConfigRepository.GetSettingAsync<SleeveValidationThresholds>("sleeveValidationThresholds")`
/ `SetSettingAsync` seam (NOT a new field on `TradingSystemConfig`, since that object carries
trade-affecting risk params). Provide a `SleeveValidationThresholds.Defaults()` factory whose numeric values
map exactly to ADR-010 informational metrics (hit rate ≥45%, profit factor ≥1.3, max drawdown ≤15%, min 12
weeks). The class is a pure data + pure evaluation holder; it has **no** dependency on `RiskConfig` and is
never read by any order/execution path. Add a `ThresholdResult Evaluate(SleeveMetrics actual)` pure method
returning per-metric pass/fail booleans + an overall pass — this is the contract S4-002 consumes. Key
decision: thresholds are evaluation-only inputs; storing them via the settings seam keeps them out of the
trade-config object by construction.
**Test Plan (TDD RED):**
1. `Defaults()` returns hit-rate 45, profit-factor 1.3, max-drawdown 15, min-weeks 12 (asserts the ADR-010 mapping).
2. `Evaluate` marks a sleeve PASS when all actual metrics meet-or-exceed thresholds (drawdown at-or-below).
3. `Evaluate` marks FAIL and flags the specific failing metric when hit rate is below threshold but others pass.
4. `Evaluate` honors `RequireProfitableOrBeatSpy`: FAIL when not profitable AND not beating SPY; PASS when not profitable but beating SPY.
5. Round-trip through a fake/in-memory `IConfigRepository`: `SetSettingAsync` then `GetSettingAsync` returns an equal threshold object (serialization fidelity).
6. Evaluating with `MinWeeksObserved` not yet met returns an "insufficient data / not-yet-evaluable" overall state distinct from FAIL.
**Integration/E2E:** None (pure config + pure evaluation; exercised E2E in S4-007).
**Post-Merge Validation:** None.
**Files:** Create: `src/TradingSystem.Core/Configuration/SleeveValidationThresholds.cs`,
`tests/TradingSystem.Tests/Configuration/SleeveValidationThresholdsTests.cs` | Modify: none.
**Dependencies:** None.
**Upgrade Impact:** N/A.
**Custom Instructions:** ⚠ **NEEDS HUMAN SIGN-OFF ON NUMBERS.** ADR-010 lists the metrics as *informational*
and "Profitable OR outperform S&P 500" as the gate, but PDR-004 says the *exact* per-sleeve numeric
criteria are still undefined. Implement `Defaults()` using ADR-010 numbers as placeholders **and** the PR
must surface a checkbox asking the owner to confirm/override each per-sleeve number before they are
authoritative. Do not invent a "beat SPY by X%" margin — ADR-010 only says "outperform"; implement strict
greater-than unless the owner specifies a margin.

---

### S4-002: Weekly sleeve readiness scorecard vs S4-001 thresholds
**Approach:** Create `SleeveReadinessScorecardService` in `src/TradingSystem.Strategies/Services/`
(strategy-layer recommendation logic, consistent with ADR-017). It reads `SleeveValidationThresholds`
(S4-001) via `IConfigRepository`, pulls per-sleeve actuals from `ISnapshotRepository.GetSnapshotsAsync`
(sleeve values, max drawdown, SPY close for the beat-SPY check) and `ITradeRepository.GetStatisticsAsync`
(win rate → hit rate, profit factor), computes weeks-observed from snapshot date span, and produces a
`SleeveReadinessScorecard` per sleeve: metric values, per-metric pass/fail (delegated to
`thresholds.Evaluate`), an overall readiness state (`NotReady`/`InsufficientData`/`Ready`), a human-readable
`Rationale`, and a `Confidence`. **Recommendation-only:** returns a scorecard object and writes nothing to
config/mode/orders. Respect the active-sleeve **minimum-capital constraint** (ADR-020/roadmap: $100k per
active sleeve) via a `MeetsMinimumCapital` flag derived from current sleeve value vs a configurable
`MinimumLiveCapitalPerSleeve` (read-only). A metrics-Ready but under-capital sleeve reports `Ready` metrics +
a capital-gated recommendation note, never auto-activation. Confidence is a deterministic function of sample
size — **not** an AI call. Inject the two repositories + `IConfigRepository`; unit-test with fakes.
**Test Plan (TDD RED):**
1. All metrics above threshold and ≥ min weeks → `Ready`, all per-metric flags true.
2. Below hit-rate threshold → `NotReady`, rationale names the failing metric.
3. Fewer than `MinWeeksObserved` weeks of snapshots → `InsufficientData` (not `NotReady`).
4. Metrics-Ready sleeve below `MinimumLiveCapitalPerSleeve` → `MeetsMinimumCapital == false`, capital-gated note; assert injected config repo received no write.
5. Beat-SPY path: unprofitable sleeve that outperformed SPY → readiness not failed on the profitability gate.
6. Confidence increases monotonically with weeks-observed / trade count.
7. Both sleeves produced in one call; an empty/zero-trade sleeve yields `InsufficientData`, not divide-by-zero.
**Integration/E2E:** Covered by S4-007.
**Post-Merge Validation:** None.
**Files:** Create: `src/TradingSystem.Strategies/Services/SleeveReadinessScorecardService.cs`,
`src/TradingSystem.Core/Models/SleeveReadinessScorecard.cs`,
`tests/TradingSystem.Tests/Strategies/SleeveReadinessScorecardServiceTests.cs` | Modify: possibly
`SleeveValidationThresholds.cs` to add `MinimumLiveCapitalPerSleeve` (read-only) if not added in S4-001.
**Dependencies:** **S4-001** (consumes `SleeveValidationThresholds` + `Evaluate`). Sequence S4-001 → S4-002.
**Upgrade Impact:** N/A.
**Custom Instructions:** Recommendation-only — assert in tests that no write occurs to mode/risk/sleeve-weight
config. The $100k minimum (ADR-020/roadmap) stays configurable and is flagged in the S4-001 sign-off. The
scorecard only describes readiness; it triggers nothing.

---

### S4-003: Discord rich daily report — Week-10 digest (B-004)
**Approach:** Add `DiscordDailyReportService` (implementing a new `IDailyReportService` in
`src/TradingSystem.Core/Interfaces/`) in `src/TradingSystem.Functions/`, reusing the hardening pattern
proven in `DiscordRiskAlertService.cs`: same `DiscordConfig` (no new secrets — same webhook), the
`TryValidateWebhook` https host allow-list, token-redacted logging (scheme://host only), the bounded
429/Retry-After retry with injectable `_delay`, and the `Enabled==false` skip. **Refactor opportunity
(decide with executor):** extract the shared webhook-post-with-retry + validation into a small internal
helper used by both services; keep `DiscordRiskAlertService` behavior byte-identical (its tests stay green).
The report builds a rich embed (multiple embeds/fields) from a `DailySnapshot` (today's `ISnapshotRepository`
entry) plus `TradeRepository` fills for the day: executed trades, realized/unrealized P&L, open positions,
current regime (`DailySnapshot.MarketRegime`). Per **ADR-023**, the cost section breaks out **platform vs
brokerage** as distinct fields (platform = Azure+Polygon+Claude; brokerage = `DailySnapshot.CommissionsPaid`
/ activity-based forecast) — do not conflate. No new outbound secret, no live trading interaction.
**Test Plan (TDD RED):**
1. `Enabled==false` → no HTTP POST, returns without throwing.
2. Builds an embed payload containing the day's executed-trade count, P&L, open-position count, and regime.
3. Cost breakout: payload has **separate** platform-cost and brokerage-cost fields (ADR-023) — fails if merged.
4. Webhook token redaction: on a forced failure, no log argument contains the token path segment.
5. 429 then 204 → exactly one retry honoring Retry-After via the injected zero-wait delay, then success logged.
6. Malformed/non-Discord webhook host → skipped with a redacted warning, no POST.
7. A day with zero trades still produces a valid "no trades today" embed (no null/empty-collection throw).
**Integration/E2E:** Exercised in S4-007 with the webhook HTTP mocked (no live Discord POST).
**Post-Merge Validation:** Once a real webhook is provisioned (KD-001 open), a manual one-shot send to
confirm embed rendering. Deferred/manual — do not gate the PR on it.
**Files:** Create: `src/TradingSystem.Functions/DiscordDailyReportService.cs`,
`src/TradingSystem.Core/Interfaces/IDailyReportService.cs`,
`tests/TradingSystem.Tests/Functions/DiscordDailyReportServiceTests.cs` | Modify:
`src/TradingSystem.Functions/DiscordRiskAlertService.cs` only if extracting the shared helper (keep behavior
identical), `src/TradingSystem.Functions/Program.cs` (DI registration).
**Dependencies:** None hard. Builds on merged S3-004 plumbing. Independent of S4-001/002.
**Upgrade Impact:** N/A.
**Custom Instructions:** Reuse the S3-004 security pattern verbatim — host allow-list, https-only, token
redaction, bounded retry. No new config/secrets beyond `DiscordConfig.WebhookUrl`. If you extract a shared
helper, existing `DiscordRiskAlertServiceTests` must remain green unchanged.

---

### S4-004: Gateway jsonSchema structured-output for regime parsing (B-005)
**Approach:** Replace the brittle substring-brace extraction in `ClaudeService.AnalyzeAsync<T>`
(`ClaudeService.cs:146-162`, `IndexOf('{')` / `LastIndexOf('}')`) with gateway `jsonSchema` structured
output. Add an optional `JsonSchema` (or `ResponseSchema`) field to `AIAnalysisRequest` (`IStrategy.cs:79`)
and an optional schema parameter/overload on `IClaudeService.AnalyzeAsync<T>`. When a schema is supplied,
`TryGatewayAsync` includes `jsonSchema` in the posted body (`ClaudeService.cs:176`). **Per B-005 / the
gateway INTEGRATION.md structured-output contract: when `jsonSchema` is sent, the gateway returns `response`
as a JSON *string* (not a parsed object), so the caller must `JsonSerializer.Deserialize<T>` that string
directly** — no brace-scanning. Preserve all fallbacks exactly: gateway miss / parse failure / empty content
must still throw into the caller's try-catch so `MarketRegimeProvider` falls back to deterministic rules
(ADR-029/030). The regime→strategy mapping and the AI risk-multiplier clamp are **unchanged**. Keep the old
whole-string-or-brace-scan path as the behavior for the *no-schema* / direct-API branch (the direct Anthropic
API does not return the gateway's wrapped JSON-string shape). Key decision: structured output applies to the
gateway leg; the parse-failure → rules contract is the invariant under test.
**Test Plan (TDD RED):**
1. Gateway returns `{"response":"{\"regime\":\"riskon\"}"}` with a schema set → deserializes the inner JSON string, `Regime=="riskon"` (no brace-scan).
2. Gateway returns a `response` JSON string not valid for `T` → throws `InvalidOperationException` (caller falls back to rules); does NOT silently return null/default.
3. Schema-mode response valid JSON but missing required fields → deserializes with null unknowns (provider's null-regime guard triggers rules) — no exception masking.
4. No-schema request still works via the legacy direct-API/whole-string path (regression).
5. Posted gateway body includes `jsonSchema` when a schema is supplied, omits it when none (assert via existing `StubHandler`).
6. Full `MarketRegimeProvider` path: well-formed structured response → `RegimeSource.Claude`; malformed → rule-based fallback.
**Integration/E2E:** None live (loopback gateway; HTTP mocked via existing `FakeHttpClientFactory`/`StubHandler`).
**Post-Merge Validation:** When a real gateway runs locally, a manual one-shot regime call to confirm it
honors `jsonSchema` and returns the JSON-string `response` shape. Deferred/manual (KD-002).
**Files:** Create: none. Modify: `src/TradingSystem.AI/Services/ClaudeService.cs`,
`src/TradingSystem.Core/Interfaces/IStrategy.cs` (schema field on `AIAnalysisRequest` and/or overload),
`src/TradingSystem.Strategies/Services/MarketRegimeProvider.cs` (pass the regime schema),
`tests/TradingSystem.Tests/AI/ClaudeServiceTests.cs`.
**Dependencies:** None.
**Upgrade Impact:** ⚠ Verify: (a) the gateway `response`-as-JSON-string shape and field name `jsonSchema`
against the live `claude-gateway` INTEGRATION.md (**not in this repo** — confirm before relying);
(b) the direct-Anthropic leg is unaffected; (c) rule-only fallback (ADR-029/030) and the AI multiplier clamp
still trigger on parse failure. **Open question for human:** exact field name (`jsonSchema` vs `json_schema`)
and response wrapping are specified only in an external doc — confirm before merge; if unavailable, keep the
legacy parse as default and gate the new path behind schema-presence so nothing regresses.

---

### S4-005: Discord disabled-path log level + GatewayTimeoutSeconds upper-bound validation (B-006, B-008)
**Approach:** Two small hardening changes, no behavior change to alert delivery or the AI path.
- **B-006:** In `DiscordRiskAlertService.SendAlertAsync` (`DiscordRiskAlertService.cs:98-102`), change the
  `Enabled==false` skip log from `LogInformation` to `LogDebug` (fires every risk-check cycle → noise).
  Delivery logic untouched. If S4-003 introduced a shared helper, apply Debug there too.
- **B-008:** Add upper-bound validation for `ClaudeConfig.GatewayTimeoutSeconds` via a `ClaudeConfig.Validate()`
  / clamp-on-bind rejecting (or clamping with a warning) values above a sane max (suggest 120s; document in the
  XML comment by the 35s default at `ClaudeConfig.cs:38`). Prefer fail-loud validation in
  `ClaudeServiceRegistration.Add` (`ClaudeServiceRegistration.cs:43-47`) where the named-client timeout is set,
  so an excessive value is caught at startup rather than a multi-minute hung leg. Keep the 35s default and lower
  bound intact.
**Test Plan (TDD RED):**
1. `Enabled==false` → logs at **Debug** (not Information) and still skips the POST.
2. `GatewayTimeoutSeconds` above max → validation rejects/clamps to max and emits a warning (resolved named-client timeout ≤ max).
3. In-range value (e.g. 35) → unchanged, no warning (regression guard).
4. (If clamping) an out-of-range value still yields a usable client (fails toward availability, not a startup crash).
**Integration/E2E:** None.
**Post-Merge Validation:** None.
**Files:** Create: none. Modify: `src/TradingSystem.Functions/DiscordRiskAlertService.cs` (Debug level),
`src/TradingSystem.Core/Configuration/ClaudeConfig.cs` (max bound + validation hook),
`src/TradingSystem.AI/Services/ClaudeServiceRegistration.cs` (enforce bound at registration),
`tests/TradingSystem.Tests/Functions/DiscordRiskAlertServiceTests.cs`,
`tests/TradingSystem.Tests/AI/ClaudeServiceTests.cs`.
**Dependencies:** None. Light coordination with S4-003 if a shared Discord helper lands.
**Upgrade Impact:** N/A.
**Custom Instructions:** Decide reject-vs-clamp for the timeout bound and state it in the PR; clamp-with-warning
is the safer default (never crashes the host on a config typo). Pick the max constant (suggest 120s) and justify
it in the comment relative to the 35s cold-start default. No change to alert delivery semantics or the AI
fallback contract.

---

### S4-006: Reconcile .pr-pipeline.json with repo merge policy (B-007)
**Approach:** Edit `/.pr-pipeline.json` to match the active directive: set `"mergeMethod"` from `"squash"` to
`"merge"` (squash is **disabled** in the repo; merges go via `--merge`) and `"cycleLimit"` from `5` to `3`. No
other keys change. Config-data reconciliation, not code.
**Test Plan (TDD RED):** None applicable — single JSON policy file with no test-harness binding (consumed by the
external pipeline, not the .NET suite). Verification is the diff: `mergeMethod: "merge"` and `cycleLimit: 3`,
all other fields byte-identical.
**Integration/E2E:** None.
**Post-Merge Validation:** First pipeline run after merge should use `--merge` and honor cycle-limit 3.
**Files:** Create: none. Modify: `/.pr-pipeline.json`.
**Dependencies:** None.
**Upgrade Impact:** N/A.
**Custom Instructions:** Change only `mergeMethod` and `cycleLimit`; leave `highRiskPatterns`, `autoMerge`,
`skipPatterns`, `notification`, `mergeWait` untouched. Confirm the directive's intended cycle limit is 3 (per
BACKLOG B-007) before committing.

---

### S4-007: E2E SANDBOX scorecard/report smoke test (readiness path)
**Approach:** Extend the existing inert-AI SANDBOX harness (`tests/TradingSystem.SmokeTest/Program.cs`, the
S3-006 harness) with a readiness-path section wiring S4-001 → S4-002 → S4-003 with **all externals mocked**:
stub `ISnapshotRepository` + `ITradeRepository` (seeded deterministic paper metrics), an in-memory
`IConfigRepository` carrying `SleeveValidationThresholds.Defaults()`, a `ClaudeService` with no gateway key (AI
inert → deterministic rules, matching the harness posture), and a `DiscordDailyReportService`/scorecard with
the webhook HTTP mocked via a stub `IHttpClientFactory` (no live Discord POST). The section runs
`SleeveReadinessScorecardService` to produce both sleeve scorecards, feeds the day's snapshot into
`DiscordDailyReportService`, and asserts the smoke invariants. Because `Program.cs` is a console harness
(IBKR-dependent, returns exit code), the **assertion-bearing** version of this path should also live as a
deterministic xUnit test (`tests/TradingSystem.Tests/`) so it runs green in CI without TWS — the console
harness gets a parallel inert section for manual SANDBOX runs. Critical safety assertions: **no LIVE switch**
(`Mode` stays `Sandbox`, no write to `Mode`), **no order placement** (mocked broker/execution receives zero
`PlaceOrder`/`ExecuteSignal` calls), output **recommendation-only** (scorecard + report produced; no mutation
of risk/sleeve-weight/mode config), suite stays green.
**Test Plan (TDD RED):**
1. Readiness path runs end-to-end (thresholds → scorecard → daily report) with mocked repos/HTTP; non-null scorecard for both sleeves and a built report embed.
2. **No order placement:** mock execution/broker records zero order/signal-execution calls.
3. **No LIVE switch:** `Mode` is `Sandbox` before and after; in-memory config repo received no `Mode` write and no `SaveConfigAsync`.
4. **Recommendation-only:** no write to `RiskConfig`/sleeve weights/thresholds during the run.
5. Discord report leg makes **no live POST** — only the stubbed handler; with `Enabled==false` skipped entirely.
6. Inert-AI posture: with no gateway key, regime comes from deterministic rules (gateway handler never invoked).
7. Suite-green guard: new xUnit fixture passes without TWS/network (CI-safe).
**Integration/E2E:** This **is** the E2E item — SANDBOX readiness smoke with externals mocked. Console
`Program.cs` section is for manual SANDBOX runs against paper TWS; the xUnit fixture is the CI-enforced version.
**Post-Merge Validation:** Optional manual run of the console harness against live paper TWS. Deferred/manual.
**Files:** Create: `tests/TradingSystem.Tests/Functions/SandboxReadinessSmokeTests.cs` (CI-enforced) | Modify:
`tests/TradingSystem.SmokeTest/Program.cs` (add inert readiness section + raise test count/header).
**Dependencies:** **S4-001, S4-002, S4-003**. Sequence last.
**Upgrade Impact:** N/A.
**Custom Instructions:** The safety assertions (no LIVE switch, no order placement, recommendation-only) are the
point — make them explicit, named test cases. Keep AI inert (no gateway key) per S3-006. Never allow a real
Discord POST or a real order; all externals stubbed.

---

## Cross-cutting invariants (all items)

Deterministic trading logic untouched; AI analysis-only; no SANDBOX→LIVE; no risk-param/sleeve-weight changes;
recommendation paths read-only. S4-005's timeout bound and S4-004's parse change must not alter the ADR-029/030
fail-to-rules contract (existing `ClaudeServiceTests` must stay green).
