# SPRINT.md - Sprint Header

> Sprint scope and status. Loaded via @import each session when an active sprint exists.
> Individual issues are tracked as native Claude Code Tasks (persistent across sessions).
> Archived to git history when a sprint completes.

*No active sprint.*

---

## Sprint Archive

### S6: Protect & Instrument the Paper-Validation Run

<!-- sizing: 7-8 -->
**Completed:** 2026-06-11 | **Status:** complete | **Final main HEAD:** 55921a6
**Goal:** Protect and instrument the live paper-validation run — close the income-sleeve wiring gap before the July 1 reinvest firing (owner-gated, recommendation-only by default), automate the host-boot and dead-man blind spots, and clear doc/repo hygiene — with zero changes to deterministic trading rules, risk parameters, or run posture.

**Outcome:** 7/7 items completed and merged to main (PRs #98–#104, all CI-gated, merge-commit). Test suite 590 → 632 passing (0 failed), +42 tests. **Zero rework, zero blocked, zero scope changes** — and the fastest sprint yet: plan approved 2026-06-10, all 7 merged by 06:56 PT 2026-06-11 (~12.5 h wall-clock). Strict TDD per item; executors ran in worktree isolation. First sprint with `.sprint-metrics.json` emission (S6-005) — phase timing captured from EXECUTE onward (PLAN/SPEC events missing; early events backfilled after a jq WSL-PATH fix that S6-005's own advisory surfaced); review-finding counters not yet wired (S7 chore). S6-001 (sprint centerpiece, deadline 2026-07-01) merged 20 days early; its primary invariant — reinvest flag off ⇒ IExecutionService never called — proven against the real IncomeSleeveManager. S6-002's host-boot CI gate (the S5-004r blind-spot fix) ran on both subsequent src PRs (#100, #101) and passed — live and self-proven within the sprint. Five of seven items took a single review-fix cycle each (S6-005 `922e46b`, S6-002 `b7170f3`, S6-001 `6fa381e`, S6-003 `c1b7983`, S6-004 `05fe512`) — all polish/docs-level, none correctness; S6-007/S6-006 merged clean. The judge rejected three lens findings as factually wrong (S6-002 A1, S6-003 A1, S6-006 U4 — single-layer reads), blocking pointless fix cycles. One session-limit interruption (S6-006 lens pass) re-spawned cleanly; two pipeliner background-CI-watch stalls were finished inline by the orchestrator. Post-merge validation: live host restarted onto merged code (all 4 timers register, no TypeLoadException, PID-stable), TradingSystem-GapDayMonitor task registered, preflight run (TWS FAIL = human-owned login, data-dir FAIL = self-resolving day-1 state), Check-DailySnapshot -WhatIf exit 3 correct. Sprint goal met: the 12-week paper run enters day 1 (2026-06-11) instrumented — dead-man monitor armed, host-boot gated in CI, reinvest path wired and owner-gated.

| ID | Title | Type | Risk | PR | Outcome |
|----|-------|------|------|-----|---------|
| S6-001 | Wire IncomeSleeve_MonthlyReinvest timer to IncomeSleeveManager — recommendation-only default, order placement behind owner flag (OFF) | feature | ⚠ | #101 | merged, pass |
| S6-002 | Automated host-boot smoke test in CI — worker boots, 4 functions register, no TypeLoadException | test | - | #99 | merged, pass |
| S6-003 | Runbook + ADR-031 reconciliation — all 4 timers documented, App-Insights triage refs fixed | docs | - | #102 | merged, n/a |
| S6-004 | Dead-man gap-day monitor + preflight.ps1 — PowerShell scheduled task, Discord alert on missing EOD snapshot | feature | ⚠ | #103 | merged, pass |
| S6-005 | Repo housekeeping — commit untracked docs, gitignore .vscode/, emit .sprint-metrics.json | chore | - | #98 | merged, n/a |
| S6-006 | DECISIONS.md / roadmap state refresh — stale snapshot, resolved blockers | docs | - | #104 | merged, n/a |
| S6-007 | Quarterly-audit stub honesty — stop logging "complete" for a no-op | fix | - | #100 | merged, pass |

**Decisions logged:** ADR-031 amended (S6-003) — corrected from "two NCRONTAB timers" to all four; a missed monthly-reinvest firing is a tolerated gap day, not an incident; quarterly-audit stub produces an honest Warning and nothing else (S6-007). KD-007 logged (S6-001 judge deferral, recorded by S6-006) — optional-null ctor dependency pattern, decide once portfolio-wide. DECISIONS.md Project State Snapshot refreshed (S6-006): Phase 3 live since 2026-06-11, 632 tests, both 2026-04-07 blockers cleared. Locked human decisions at plan approval: S6-001 touches the trading path mid-run ONLY recommendation-only (plan + Discord report, no orders), order placement behind a new owner flag defaulting OFF, income sleeve's PDR-004 clock starts at its first actual trade; S6-004 = PowerShell scheduled task with webhook from gitignored local.settings.json; S6-002 host-boot smoke in CI; .vscode/ gitignored, not committed; deferrals confirmed — KD-005/KD-006 (upstream still blocked, Worker AI 2.51.0), quarterly-audit full impl (S7+), B-001/B-002/B-003 (gated per ADR-030).
**Debt closed:** All four S5-surfaced process items — the host-boot blind spot is now a structural CI gate, no longer a checklist (S6-002); the quarterly-audit stub no longer logs a false "complete" (S6-007); stale DECISIONS.md snapshot and cleared blockers reconciled (S6-006); untracked S5 docs committed and sprint-metrics emission wired (S6-005).
**Debt surfaced (deferred, not lost):** KD-007 — optional-null ctor dependency pattern (S6-001 judge deferral; S6-002's gate partially mitigates accidental de-registration). Metrics instrumentation gaps — reviewFindings/reworkCount counters never incremented, completedAt/totals not finalized at COMPLETE, PLAN/SPEC events not emitted (S7 chore). Backlog candidates: S6-002 grep hardening, build-artifact reuse, failure-class prefixes; S6-004 Format-Table polish, WARN count, embed copy ordering; S6-001 defence-in-depth exception wrapper, alert-gate consolidation.
**Next-sprint sizing:** 7–8 items (recommend 8). All items completed with zero rework/blocked → same-or-+1 off a clean 7. S5's hold-at-7 rationale has dissolved: the host-boot gate is automated CI (no per-item overhead) and S6 cleared 7 items in ~12.5 h wall-clock with capacity to spare. Go 8 if the mix is ≤2 ⚠-risk items (quarterly-audit impl + KD-007 decision + S6 backlog candidates fit); hold at 7 if S7 pulls ≥2 ⚠ items or anything touching the live run's trading path — the paper run's daily monitoring is now a standing tax (ADR-031) and the 5/7 review-fix cycle rate still costs pipeline wall-clock.

### S5: Start the Paper-Validation Run — EOD orchestration, ops wiring, runbook

<!-- sizing: 7-8 -->
**Completed:** 2026-06-10 | **Status:** complete | **Final main HEAD:** b410f8f
**Goal:** Close the end-of-day orchestration gap and the operational wiring (config, alerting, runbook) so the 12-week SANDBOX paper-validation run can actually start, while clearing the small debt surfaced by S4 — with zero deterministic-trading, risk-parameter, sleeve-allocation, or SANDBOX→LIVE changes and zero metered API spend.

**Outcome:** 9/9 items completed and merged to main (8 planned + 1 rework; PRs #88–#96, all CI-gated, merge-commit). Test suite 551 → 590 passing (0 failed), +39 tests. **One rework, zero blocked, zero scope changes beyond the rework.** S5-004's post-merge validation FAILED — the Functions host did not boot (ApplicationInsights.WorkerService 3.0.0 incompatible with Functions Worker AI 2.50.0 → `TypeLoadException: ITelemetryInitializer`, plus a missing bare-`TacticalConfig` DI registration); both defects shipped invisibly because the 590-test xUnit suite never boots the Functions host. Rework S5-004r (PR #96, fix `02d7c24`) realigned the AI packages, restored host boot (all 4 timers register), and passed post-merge validation (Azurite scheduled-task quoting bug also fixed, FuncHost stable). Process changes adopted: host-boot smoke is a runbook preflight step AND an acceptance criterion for any item touching Program.cs/csproj/DI; executor specs must mandate explicit timeouts on every named HttpClient (3 of 4 S5 HIGH findings were that defect class). Strict TDD per item; executors ran in worktree isolation. Six of nine PRs took a single review-fix cycle each (S5-001 `f0454f9`, S5-002 `20b3a54`, S5-003 `bef0e23`, S5-004 `9f2089e`, S5-006 `15a07e7`, S5-004r `364c9eb` docs-only); S5-005/007/008 merged clean. Sprint goal met: day-0 webhook/gateway wiring done, test alert delivered (HTTP 204), the 12-week paper-validation run starts 2026-06-11 (Run Log in docs/paper-validation-runbook.md is the system of record).

| ID | Title | Type | Risk | PR | Outcome |
|----|-------|------|------|-----|---------|
| S5-001 | Implement EOD orchestrator pipeline — position/P&L sync, DailySnapshot persistence, stop-trigger check | feature | ⚠ | #91 | merged, pass |
| S5-002 | Wire daily Discord report + weekly readiness-scorecard cadence into EOD run | feature | - | #93 | merged, pass |
| S5-003 | Operational failure alerting — Discord alert on broker-connect / orchestration failure | feature | - | #92 | merged, pass |
| S5-004 | Paper-validation launch runbook + day-0 config wiring (webhook, gateway preflight) | docs | ⚠ | #94 | merged, post-merge FAIL → reworked (S5-004r) |
| S5-005 | Pin transitive OpenTelemetry.Api past GHSA-g94r-2vxg-569j | chore | ⚠ | #89 | merged, pass |
| S5-006 | DECISIONS.md hygiene — mark PDR-004 resolved; re-scope KD-001/KD-002 | docs | - | #95 | merged, n/a |
| S5-007 | S4-007 deferred cosmetic test polish | test | - | #90 | merged, n/a |
| S5-008 | .gitignore sprint runtime artifacts + *.sh LF pin | chore | - | #88 | merged, n/a |
| S5-004r | Rework: func host fails to boot — ApplicationInsights.WorkerService 3.0.0 incompatible with Functions Worker AI 2.50.0 (TypeLoadException: ITelemetryInitializer) | fix | ⚠ | #96 | merged, pass |

**Decisions logged:** ADR-031 (new) — paper-validation run hosted on the locally hosted Functions worker, co-resident with loopback-only TWS (127.0.0.1:7497) and claude-gateway (localhost:3131); missed timer firings are tolerated gap days, not incidents; Azure path intact for a future LIVE posture. PDR-004 marked resolved (S5-006, PDR-002 style). KD-001 closed — Discord webhook provisioned day-0, test alert delivered through the production guard path. KD-002 re-scoped to conditional (gateway-or-rules default posture needs no metered key). Locked human decisions: local run model; `DirectApiFallbackEnabled` stays OFF; validation clock starts at sprint close (run starts 2026-06-11); EOD stop-check alert-only; weekly scorecard Friday EOD.
**Debt closed:** KD-001 (Discord webhook wired), plus all four S4-surfaced items — `.gitattributes` `*.sh text eol=lf` pin (PR #88), OpenTelemetry.Api advisory pin (S5-005; pin later superseded by the S5-004r package realignment — see csproj comment), S4-007 cosmetic test polish (S5-007), DECISIONS.md PDR-004 hygiene (S5-006).
**Debt surfaced (deferred, not lost):** KD-005 — ApplicationInsights 2.x→3.x upgrade gate: blocked until Functions Worker AI ships a 3.x-compatible release; re-evaluate the removed OTel pin and clear KD-006 in the same change. KD-006 — discord.com URI-redaction gate: AI 2.x dependency telemetry records full outbound URLs (the Discord webhook URL IS the token), bypassing the Program.cs HttpClient ILogger filter; do NOT set APPLICATIONINSIGHTS_CONNECTION_STRING until a redacting telemetry processor exists or KD-005 supersedes.
**Next-sprint sizing:** 7–8 items (recommend 7). Rework occurred → same-or-minus-one off 8. The rework's root cause was a test-coverage blind spot (the suite never boots the host), not over-sizing — so hold at 7 rather than cutting deeper. Lean 7 because the 12-week paper run is now live (monitoring/triage is a standing tax on the daily budget per ADR-031) and the two new gates (host-boot acceptance criterion, timeout-mandating specs) add per-item overhead until routine; go to 8 only if ≥2 items are trivial chores/docs.

### S4: Paper-Trading Readiness — PDR-004 gate, Week-10 digest, AI-path hardening

<!-- sizing: 7-8 -->
**Completed:** 2026-06-10 | **Status:** complete | **Final main HEAD:** 03d3c2d
**Goal:** Make the system paper-validation-ready — define the numeric per-sleeve paper gate (PDR-004), ship the Week-10 reporting digest, harden the gateway/regime AI path, and clear low-risk operability debt — with zero deterministic-trading, risk-parameter, or SANDBOX→LIVE changes.

**Outcome:** 7/7 items completed and merged to main (PRs #79–#85, all CI-gated, merge-commit). Test suite 479 → 551 passing (0 failed), +72 tests. Zero rework, zero blocked, zero scope changes. Strict TDD per item; executors ran in worktree isolation. Split-environment sprint: PLAN→SPEC ran remotely without the .NET SDK (execution deferred by design); EXECUTE→COMPLETE ran locally. A PC restart interrupted the session mid-S4-007 (after the executor's commit, before TEST) — recovery from git/SPRINT.md state was clean, zero work lost. Six of seven items took a single review-fix cycle each (S4-001 `44ca7de`, S4-002 `5158493`, S4-003 `fa89d27`, S4-004 `39c2f68`, S4-005 `dcb5149`, S4-007 `d94eeba`) — all operability/clarity polish, not correctness; S4-006 merged clean. Final quality review: pass, 0 critical / 0 high.

| ID | Title | Type | Risk | PR | Outcome |
|----|-------|------|------|-----|---------|
| S4-001 | Numeric per-sleeve paper-validation thresholds (PDR-004), config-driven | feature | ⚠ | #81 | merged, pass |
| S4-002 | Weekly sleeve readiness scorecard vs S4-001 thresholds | feature | ⚠ | #83 | merged, pass |
| S4-003 | Discord rich daily report — Week-10 digest (B-004) | feature | - | #84 | merged, pass |
| S4-004 | Gateway jsonSchema structured-output for regime parsing (B-005) | chore | ⚠ | #82 | merged, pass |
| S4-005 | Discord disabled-path log level + GatewayTimeoutSeconds bound (B-006, B-008) | chore | - | #80 | merged, pass |
| S4-006 | Reconcile .pr-pipeline.json with repo merge policy (B-007) | chore | - | #79 | merged, pass |
| S4-007 | E2E SANDBOX scorecard/report smoke test (readiness path) | test | ⚠ | #85 | merged, pass |

**Decisions logged:** PDR-004 (resolved in code, S4-001) — per-sleeve paper gate, owner-confirmed 2026-06-09: hit rate ≥45%, profit factor ≥1.3, max drawdown ≤15%, ≥12 weeks observed, profitable-OR-beat-SPY (strict >), $100k minimum live capital per sleeve; evaluation-only via the IConfigRepository settings seam, never on any order/execution path. No new ADRs. Follow-up: DECISIONS.md still lists PDR-004 as pending — mark it resolved (PDR-002 style) next sprint.
**Debt closed:** B-004, B-005, B-006, B-007, B-008 — Discord rich daily report, gateway jsonSchema adoption, disabled-path log demotion, .pr-pipeline.json reconciliation, GatewayTimeoutSeconds 120s clamp.
**Debt surfaced (deferred, not lost):** (a) `core.autocrlf=true` with no `.gitattributes` `*.sh text eol=lf` pin — CRLF smudging broke `.claude` bash hooks after the restart; pin shell scripts to LF. (b) Transitive OpenTelemetry.Api 1.15.0 Moderate advisory (GHSA-g94r-2vxg-569j) via TradingSystem.Functions — pre-existing on main, surfaced in S4-007 review. (c) Review-judge deferred cosmetic test polish from S4-007 (banner annotations, DateTime.Today rationale comment, log-scrub assertion). (d) DECISIONS.md PDR-004 entry not yet marked resolved.
**Next-sprint sizing:** 7–8 items (recommend 7). All items completed with zero rework/blocked → same-or-+1 off a clean 7. Hold at 7 unless ≥2 items are trivial chores: review-fix cycle rate rose to 6/7 (all polish, but each cycle costs pipeline wall-clock), and the LF-pin + advisory cleanups should ride along as small chores.

### S3: Pivot to Paper-Trading Readiness — debt, integrations, ADR

<!-- sizing: 6-7 -->
**Completed:** 2026-05-29 | **Status:** complete | **Final main HEAD:** 5b0ecb5
**Goal:** Pivot off backtesting-as-the-options-gate toward paper-trading readiness. Record the pivot as an ADR, finish the KD-004 refactor, harden both external integrations (Claude gateway-first with the metered fallback flagged off; Discord reusing the active bots-repo webhook channel), and prove the pre-market orchestrator wires up end-to-end in SANDBOX with externals mocked. No deterministic-trading / risk-parameter / sleeve-allocation changes; no SANDBOX→LIVE; no external API spend.

**Outcome:** 6/6 items completed and merged to main (PRs #70–#75, all CI-gated, merge-commit). Test suite 449 → 479 passing (0 failed), +30 tests. Zero rework, zero blocked, zero scope changes. Strict TDD per item; executors ran in worktree isolation. A strategic pivot landed mid-PLAN: the planner found the SPX iron-condor backtest already existed and was net-negative (CAGR −0.889%), which reframed the sprint from "distill the backtest into a go/no-go" to "shelve backtesting as the gate, pivot to paper trading" — captured durably as ADR-030. Two of six items took a single review-fix cycle each (S3-003 startup-posture/log clarity `8713d93`; S3-004 loud dropped-alert signaling + transport diagnostics `e41d696`) — both operability/observability polish, not correctness; four merged clean. S3-004 review also found and fixed a pre-existing token-leak (old code logged the webhook response body).

| ID | Title | Type | Risk | PR | Outcome |
|----|-------|------|------|-----|---------|
| S3-001 | Decompose CachingMarketDataService under 300-line threshold (KD-004, ADR-017) | refactor | ⚠ | #70 | merged, pass |
| S3-002 | ADR-030 — backtesting isn't the options gate; paper trading is | docs | - | #71 | merged, pass |
| S3-003 | ClaudeConfig DirectApiFallbackEnabled flag (default false) + 35s gateway timeout + ADR-029 update | feat | ⚠ | #72 | merged, pass |
| S3-004 | Point Discord plumbing at the active bots-repo webhook channel | feat | - | #73 | merged, pass |
| S3-005 | Backlog grooming — capture shelved alpha + backtest threads | chore | - | #74 | merged, pass |
| S3-006 | E2E SANDBOX paper-mode orchestrator pre-market smoke test | test | ⚠ | #75 | merged, pass |

**Decisions logged:** ADR-030 (new) — paper trading (SANDBOX, forward) is the validation gate for options/complex multi-leg strategies; backtesting is a research aid (simple-stock-trade exception kept); SPX iron-condor result inconclusive, NOT a demotion; supersedes the SPX-gate stance in ADR-026/028; backtest-distillation work shelved (not deleted). ADR-029 (updated, S3-003) — gateway client timeout 8s→35s (covers Claude CLI cold-start for the ~1/day regime call); metered direct-API fallback ships disabled by default (`DirectApiFallbackEnabled=false`) so default posture is gateway-or-rules with no metered spend; S2-002 fail-closed cap unchanged when fallback is enabled. Locked human decisions: hybrid sprint focus; gateway-first with metered fallback flagged off; 35s gateway timeout; Discord reuses the bots-repo webhook (corrected from solo-ops-agents after verification); 5 spec-level defaults (test-ref option B, ADR-030-only supersession, Discord option A no-new-config, B-005 detail line, S3-006 inert-AI via the S3-003 harness).
**Debt closed:** KD-004 — CachingMarketDataService decomposed into a 125-line facade + new 280-line MarketRegimeProvider, internal composition, no DI change, zero behavior change.
**Debt surfaced (deferred to backlog, not lost):** B-002…B-008 — SPX credit-spread backtest, shelved backtest distillation, Discord rich daily report, gateway jsonSchema adoption, Discord disabled-path log level, .pr-pipeline.json reconciliation, GatewayTimeoutSeconds upper-bound validation; plus 4 cosmetic Low test-cleanups on S3-006. B-001 untouched.
**Next-sprint sizing:** 6–7 items (planner target 7). KD-004 is closed, so the prior hold-at-6 constraint no longer applies; all items completed with zero rework/blocked → same-or-+1. Lean to 7 unless the next sprint pulls in another high-risk refactor or net-new external integration.

### S2: Phase 2B — AI Hardening & Consolidation Follow-ups

<!-- sizing: 6-7 -->
**Completed:** 2026-05-29 | **Status:** complete | **Final main HEAD:** ea027c9
**Goal:** Harden the Claude regime/risk integration (from PR #61) for correctness, cost-control, and capital-preservation safety before IBKR paper validation — no change to deterministic trading or risk-engine behavior.

**Outcome:** 6/6 items completed and merged to main (PRs #63–#68, all CI-gated, merge-commit). Test suite 418 → 449 passing (0 failed), +31 tests. Zero rework, zero blocked, zero scope changes. Strict TDD per item. Execution model moved to worktree-isolated executors mid-sprint after a main-checkout concurrency collision (recovered, zero data loss); a transient socket drop on S2-005 was discarded and cleanly restarted.

| ID | Title | Type | Risk | PR | Outcome |
|----|-------|------|------|-----|---------|
| S2-003 | Clamp AI RiskMultiplier to [0.5,1.0] + warn-on-clamp | fix | ⚠ | #63 | merged, pass |
| S2-001 | Regime result cache + stampede guard for GetMarketRegimeAsync | feat | ⚠ | #64 | merged, pass |
| S2-002 | Fail-closed cost cap on metered-API fallback + active-path startup log | feat | ⚠ | #65 | merged, pass |
| S2-004 | Gateway HTTP cluster: named IHttpClientFactory client, fail-fast timeout, DI guard, ADR-029 | refactor | ⚠ | #66 | merged, pass |
| S2-005 | Domain/infra boundary + model cleanups (ClaudeConfig→Core.Configuration, RegimeSource enum) | refactor | - | #67 | merged, pass |
| S2-006 | Python backtest tooling safety + rationale-log hygiene + bulk-indicator fan-out | fix | - | #68 | merged, pass |

**Decisions logged:** ADR-029 (plaintext-loopback gateway Bearer stance; HTTPS/named-pipe deferred); ClaudeConfig → TradingSystem.Core.Configuration; cancellationToken standardization scoped to Claude-adjacent only; cost-cap fail-closed signal = return-to-rules (no new exception type).
**Debt surfaced:** KD-004 — CachingMarketDataService ~360 lines, decomposition candidate.
**Next-sprint sizing:** 6–7 items.
