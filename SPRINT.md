# SPRINT.md - Sprint Header

> Sprint scope and status. Loaded via @import each session when an active sprint exists.
> Individual issues are tracked as native Claude Code Tasks (persistent across sessions).
> Archived to git history when a sprint completes.

---

## Sprint Archive

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
