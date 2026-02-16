# TradingSystem Project Review (2026-02-16, Revised 2026-02-16)

Scope: Review based on `CLAUDE.md`, `DECISIONS.md`, `PROGRESS.md`, `docs/ARCHITECTURE.md`, `docs/strategy-roadmap.md`, infrastructure (`infrastructure/main.bicep`), and current implementation across `src/` and `tests/`.

Implementation snapshot used in this review:
- Phase status in `PROGRESS.md`: Week 7 in progress.
- Current unit test run during review: 360 passed, 1 failed (`tests/TradingSystem.Tests/Storage/JsonIVHistoryRepositoryTests.cs`).
- Function orchestration layer exists but is mostly scaffold/TODO.

## 1) Executive Summary

### Confirmed operating model (owner decisions)
This project should now be treated as a two-sleeve platform with flexible live activation, not a fixed 70/30 live allocator.

Confirmed rules:
1. Paper validation phase: both sleeves should be runnable and evaluated, with a baseline of $100,000 available for validation.
2. Live activation: go live only for sleeves that pass validation expectations.
3. Capital envelope before live split decision: expected total deployable capital is approximately $10,000 to $400,000, with a minimum of $100,000 for any sleeve activated live.
4. Live activation can be staggered: one sleeve may go live first; the other can be added later after additional paper refinement.
5. Rebalancing: system provides recommendations; human executes actual rebalancing manually.
6. Cash flow operations are human-driven: deposits/withdrawals can occur over time per sleeve/account.
7. Options sleeve withdrawal rule: do not withdraw cash reserved against open options positions; withdraw only truly free cash.
8. Income sleeve withdrawal rule: cash withdrawals allowed; occasional stock sales for withdrawals are allowed.
9. Ongoing funding floor: keep at least $100,000 in each sleeve account.

### What this project will do when complete
When fully implemented, the system will:
- Run both sleeves under common orchestration in paper and live modes.
- Generate sleeve-level recommendations (allocation, risk, rebalancing suggestions, and operational actions).
- Execute deterministic strategy logic and trade workflows with risk controls.
- Keep an auditable record of data, decisions, orders, fills, and outcomes.
- Produce owner-facing reports with recommendations, alerts, and action context.

### What it will not do
- It will not auto-approve live capital allocation or rebalance executions without human decision.
- It will not replace human constraints around tax/compliance/account policy.
- It will not operate as a multi-tenant product.
- It will not include crypto trading in current scope.

### What still requires a human
- Final live sleeve activation decisions and capital split decisions.
- Manual execution of rebalance transfers and account cash movement decisions.
- Manual handling of exceptional operations and approvals (risk changes, mode switches).
- Ongoing oversight of broker/runtime availability and external service billing.

### What the user is buying
Primary value is process quality and execution discipline:
- Consistent, testable decision and execution workflows.
- A recommendation engine around sleeve activation/allocation/rebalancing.
- Lower manual burden for scans, monitoring, and logging.
- Better control and traceability than ad-hoc discretionary workflows.

### Expected monthly cost (platform + trading activity)

#### A. Platform baseline (infrastructure + data + AI)
| Cost Component | Expected Monthly Range | Notes |
|---|---:|---|
| Azure Functions + storage + telemetry + secrets + persistence workload | $12 - $35 | Typical low-to-moderate scheduled workflow volume |
| Polygon.io earnings data | $29 | Current planned subscription |
| Claude API usage | $2 - $12 | Regime + audit + recommendation/report augmentation |
| **Platform subtotal** | **$43 - $76** | Before brokerage fees |

#### B. Conservative options commission model (volume-based)
Assumption for conservative planning: **$1.00 all-in per option contract-side** (broker + exchange/regulatory).

| Monthly Option Contract-Sides | Estimated Commission Cost |
|---:|---:|
| 100 | $100 |
| 250 | $250 |
| 500 | $500 |
| 1,000 | $1,000 |

#### C. All-in monthly operating envelope
| Scenario | Platform | Commissions | Total |
|---|---:|---:|---:|
| Light options activity (100 sides) | $43 - $76 | ~$100 | **$143 - $176** |
| Moderate options activity (250 sides) | $43 - $76 | ~$250 | **$293 - $326** |
| Active options activity (500 sides) | $43 - $76 | ~$500 | **$543 - $576** |

Note: if realized all-in per contract-side is lower than this conservative assumption, totals will compress materially.

## 2) Gaps in Project Goals and Vision

1. Live allocation governance is not yet first-class in product goals.
The docs still assume fixed sleeve ratios, but your confirmed model requires dynamic sleeve activation and allocation at go-live.

2. Recommendation scope is underdefined.
You want recommendations for sleeve activation and rebalancing, but recommendation confidence, rationale format, and acceptance workflow are not yet specified.

3. Cash-flow policy needs explicit codification.
Withdrawal/addition rules by sleeve are currently in your operating intent, not encoded as system policy constraints and report logic.

4. Validation outcomes need explicit go-live gates per sleeve.
Current success criteria are mostly portfolio-level. Your operating model needs sleeve-level pass/fail and staged-live criteria.

5. Unknowns management needs formal phase gates.
Your process expects Claude to prompt interview questions at the right point; roadmap currently tracks unknowns but not explicit blocking prompts.

6. Compliance/tax operational expectations still need explicit handling boundaries.
The system must clearly distinguish recommendation output from human-accountable execution and reporting actions.

## 3) Gaps in Existing Implementation and in the Remaining Plan

### A. Critical implementation gaps
1. Orchestration is scaffolded, not operational.
`src/TradingSystem.Functions/DailyOrchestrator.cs` and `src/TradingSystem.Functions/IncomeSleeveFunction.cs` define schedules but contain TODO-only execution bodies.

2. Dependency wiring is largely absent.
`src/TradingSystem.Functions/Program.cs` has major service registrations commented out, so end-to-end runtime assembly is not in place.

3. Risk engine interface exists, implementation does not.
`src/TradingSystem.Core/Interfaces/IRiskManager.cs` exists, but no concrete implementation is present.

4. Options execution lifecycle manager is missing.
Screening and combo order primitives exist, but no complete candidate conversion, sizing, lifecycle transitions, roll/close controller, or sleeve manager orchestration is present yet.

5. Recommendation/report layer is missing.
No implemented subsystem currently produces structured owner recommendations for:
- sleeve activation/deactivation,
- allocation split proposals,
- manual rebalance action plans,
- withdrawal-safe cash advisories.

6. Reporting/alerts channel is not implemented.
Discord is planned but no webhook/reporting sender exists in source.

### B. Architectural consistency gaps
1. Storage target mismatch.
Roadmap centers on Cosmos DB; active implementations are JSON repositories.

2. Domain naming still mixes Tactical vs Options.
Examples include tactical terms in core config/models/interfaces despite ADR-011 direction.

3. Risk defaults in code are misaligned with ADR values.
`DECISIONS.md` ADR-009 vs defaults in `src/TradingSystem.Core/Configuration/TradingSystemConfig.cs`.

4. Legacy tactical strategy footprint remains active.
`src/TradingSystem.Strategies/Tactical/MomentumBreakoutStrategy.cs` remains in active codebase.

### C. Feature-completeness gaps versus desired operating model
1. No sleeve-level go-live gate evaluator exists.
Need per-sleeve validation scoring and recommendation output.

2. No manual rebalance recommendation pipeline exists.
Need explicit report outputs showing recommended transfer/sell/buy actions without auto-execution.

3. No account cash policy engine exists.
Need sleeve-aware free-cash calculations including options collateral constraints.

4. No phase-gated interview checkpoints are encoded.
Need explicit development stop points where Claude asks for decisions before proceeding.

5. Test-status drift exists.
Review-time run has 1 failing test; integration readiness remains below documented quality targets.

## 4) End-State Feature Summary (Engineer-Oriented)

### Runtime and orchestration
- Scheduled functions for pre-market, end-of-day, monthly, quarterly, plus manual control endpoints.
- Shared orchestration context including account state, sleeve state, market regime, risk state, and policy state.

### Sleeve engines
- Income sleeve:
  - category/issuer drift and cap tracking,
  - quality checks,
  - reinvest recommendations and optional execution workflows,
  - withdrawal-impact advisories.
- Options sleeve:
  - universe scan and IV/liquidity/no-trade filtering,
  - strategy-specific candidate generation/scoring,
  - position sizing and execution orchestration,
  - lifecycle management (profit take, stop, roll, expiry),
  - free-cash vs collateral-aware withdrawal advisories.

### Decision support and recommendation layer
- Sleeve-level readiness recommendation (paper -> live gate).
- Allocation recommendation model under user constraints (capital range, min per active sleeve).
- Rebalancing recommendations in periodic reports (manual execution by human).
- Confidence/rationale metadata for each recommendation.

### Risk/control plane
- Pre-trade validations and concentration limits.
- Daily/weekly stop logic, emergency halt, and re-enable flow.
- Clear human-approval gates for high-impact changes.

### Persistence and observability
- Canonical data: signals, orders, fills, trades, options positions, snapshots, configuration, recommendations, and decision logs.
- Audit trail that ties recommendation -> user approval/rejection -> execution outcome.

## 5) Prioritized Remediation Plan With Estimates

Effort estimates assume focused part-time development cadence and current codebase maturity.

### Milestone 0: Alignment and policy codification (High)
- Deliverables:
  - codify confirmed operating model (dynamic sleeve activation/allocation),
  - define recommendation schema and report outputs,
  - codify cash withdrawal/deposit policy rules by sleeve.
- Estimate: **2-4 days**
- Exit criteria: updated roadmap/architecture/decisions reflect your operating rules.

### Milestone 1: Runtime assembly and orchestration backbone (Critical)
- Deliverables:
  - wire DI registrations,
  - implement orchestrator flow skeletons with real service calls,
  - establish end-to-end paper-mode execution path.
- Estimate: **1.5-2.5 weeks**
- Exit criteria: scheduled workflows execute in paper mode without TODO gaps.

### Milestone 2: Risk engine implementation (Critical)
- Deliverables:
  - concrete `IRiskManager` implementation,
  - halt logic, exposure checks, and risk validation integration.
- Estimate: **1-2 weeks**
- Exit criteria: risk checks enforced pre-trade and reflected in reports.

### Milestone 3: Options sleeve lifecycle manager (Critical)
- Deliverables:
  - candidate->signal->order conversion,
  - position sizing and lifecycle transitions,
  - roll/close orchestration.
- Estimate: **2-3 weeks**
- Exit criteria: options sleeve can run full paper lifecycle autonomously.

### Milestone 4: Recommendation/reporting subsystem (High)
- Deliverables:
  - sleeve readiness recommendations,
  - allocation and rebalance recommendations,
  - Discord/report output integration.
- Estimate: **1-2 weeks**
- Exit criteria: daily/weekly/monthly reports include actionable recommendations.

### Milestone 5: Cash policy and manual operation support (High)
- Deliverables:
  - collateral-aware free-cash computations,
  - withdrawal/addition impact simulation,
  - guardrails in recommendations.
- Estimate: **1-1.5 weeks**
- Exit criteria: reports distinguish free cash from restricted cash, with rule-compliant guidance.

### Milestone 6: Quality hardening and validation gates (Critical)
- Deliverables:
  - fix existing test drift,
  - add integration tests around orchestration and policy workflows,
  - implement sleeve-level go-live gate scoring.
- Estimate: **1-2 weeks**
- Exit criteria: stable test baseline and explicit per-sleeve go-live recommendation output.

## 6) Phase-Gated Claude Interview Checkpoints (Non-Blocking Unknowns)

Unknowns should not block ongoing work, but must trigger prompt checkpoints before specific automation steps.

### Gate A: Pre-Milestone 3 (Options lifecycle behavior)
Claude asks:
- roll timing preference (daily batch vs near-expiry intraday checks),
- assignment tolerance and handling rules,
- profit-taking/stop policy finalization per strategy.

### Gate B: Pre-Milestone 4 (Recommendation/report format)
Claude asks:
- report cadence and format preferences,
- recommendation confidence thresholds,
- required rationale detail for human decisions.

### Gate C: Pre-paper validation launch
Claude asks:
- sleeve-specific pass/fail criteria,
- required sample size and acceptable drawdown bands,
- conditions for activating one sleeve while holding the other in paper mode.

### Gate D: Pre-live transition
Claude asks:
- final active sleeves,
- final capital split and account mapping,
- rebalance authority boundaries and approval workflow.

### Gate E: Post-live periodic tuning
Claude asks on schedule:
- strategy parameter adjustment approvals,
- withdrawal/addition policy changes,
- thresholds for pausing/reactivating sleeves.

## 7) Immediate Next Actions

1. Update `docs/strategy-roadmap.md` and `DECISIONS.md` to encode the confirmed dynamic live-allocation model and manual rebalance authority.
2. Implement Milestone 0 artifacts (policy model + recommendation schema + gate checklist).
3. Start Milestone 1 by wiring the orchestrator and DI graph to remove scaffold-only execution paths.
4. Add a commission-cost telemetry field to reports using contract-side volume and configurable all-in cost assumptions.
