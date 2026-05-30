# SPRINT.md - Sprint Header

> Sprint scope and status. Loaded via @import each session when an active sprint exists.
> Individual issues are tracked as native Claude Code Tasks (persistent across sessions).
> Archived to git history when a sprint completes.

No active sprint. Run sprint planning to begin (e.g., "start a sprint" or "begin Phase 1").

---

## Sprint Archive

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
