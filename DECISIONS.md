# DECISIONS.md - Architectural Decision Record

> Track significant decisions to avoid re-debating.
> Move superseded decisions to `docs/archive/decisions-archive.md`.

## Decisions

### ADR-001: Brokerage Platform Selection
**Date:** 2025-02-04 | **Status:** Decided | **Rationale:** Full options support (CSPs, bear call spreads, covered calls), free paper trading, institutional-grade reliability, comprehensive market data included.
**Decision:** Interactive Brokers (IBKR)
**Consequences:** Need IBKR account, must run TWS or IB Gateway locally, more complex API integration than Alpaca.

---

### ADR-002: Cloud Platform Selection
**Date:** 2025-02-04 | **Status:** Decided | **Rationale:** Developer's strong Azure expertise, existing subscription, excellent .NET 8 support, serverless consumption model keeps costs low.
**Decision:** Azure (Functions, Cosmos DB, Key Vault, Application Insights)
**Consequences:** Monthly cost ~$20-40. Deployment via Bicep templates.

---

### ADR-003: AI Analysis Approach
**Date:** 2025-02-04 | **Status:** Decided | **Rationale:** AI excels at nuanced analysis; rules are better for deterministic execution. Cost-effective at ~$15-50/month.
**Decision:** Hybrid -- Claude AI for complex analysis (market regime, quality audits, candidate ranking), rule-based execution for orders, position sizing, stop losses, drift calculation, technical signals.
**Consequences:** Two parallel paths in strategy evaluation, need fallback if Claude API fails.

---

### ADR-004: Notification System
**Date:** 2025-02-04 | **Status:** Decided | **Rationale:** Free, rich embed formatting, mobile notifications, simple webhook implementation.
**Decision:** Discord webhooks
**Consequences:** Human creates Discord server and webhook. Reports formatted as embeds.

---

### ADR-005: Earnings Calendar Data Source
**Date:** 2025-02-04 | **Status:** Decided | **Rationale:** Earnings timing is critical for avoiding costly surprises. Reliability worth $29/month.
**Decision:** Polygon.io ($29/month)
**Consequences:** Additional monthly cost, human signs up for Polygon.io account.

---

### ADR-006: Options IV Data Source
**Date:** 2025-02-04 | **Status:** Decided (implementation details TBD for Phase 2) | **Rationale:** Avoids additional costs, IBKR provides historical data, more control over methodology.
**Decision:** Calculate IV Rank/Percentile from IBKR historical volatility data.
**Consequences:** Need to implement calculation in Phase 2 (Week 13-14). Consider whether 1-year lookback is sufficient.

---

### ADR-007: Income Quality Data Source
**Date:** 2025-02-04 | **Status:** Decided | **Rationale:** Quarterly frequency means latency acceptable. Claude can synthesize from multiple sources. Free (part of existing API budget).
**Decision:** Claude web search during quarterly audits.
**Consequences:** Quality data may not be 100% accurate -- human should verify. Audit reports should cite sources.

---

### ADR-008: Git Workflow
**Date:** 2025-02-04 | **Status:** Decided | **Rationale:** Feature branches isolate work, PR reviews ensure quality, human merge control prevents accidental deployments.
**Decision:** Feature branches off main. All changes via PR. Human reviews and merges.

---

### ADR-009: Risk Parameter Defaults
**Date:** 2025-02-04 | **Status:** Decided | **Rationale:** Conservative defaults with single equity/spread slightly raised for better position sizing.
**Decision:**
| Parameter | Value |
|-----------|-------|
| Per-trade risk | 0.4% |
| Daily stop | 2% |
| Weekly stop | 4% |
| Max single equity | 7.5% (raised from 5%) |
| Max single spread | 3% (raised from 2%) |
| Max gross leverage | 1.2x |
| Issuer cap | 10% |
| Category cap | 40% |
**Consequences:** Tunable based on paper trading. Any changes require human approval.

---

### ADR-010: Paper Trading Validation Criteria
**Date:** 2025-02-04 | **Status:** Decided | **Rationale:** 12 weeks provides reasonable sample size. "OR S&P 500" allows validation even in down markets.
**Decision:** Minimum 12 weeks. Profitable OR outperform S&P 500. Informational metrics: hit rate ≥ 45%, profit factor ≥ 1.3, max drawdown ≤ 15%.
**Consequences:** Live trading earliest at Week 27. Human approval still required.

---

### ADR-011: Rename Tactical Sleeve to Options Sleeve
**Date:** 2026-02-09 | **Status:** Decided | **Rationale:** The 30% sleeve is focused on multi-leg options strategies (credit spreads, iron condors, CSPs, calendar spreads), not equity swing trades. Name should reflect actual strategy.
**Decision:** Rename "Tactical Sleeve" to "Options Sleeve" throughout the codebase and docs.
**Consequences:** Swing trades may become a future third sleeve. All code references updated.

---

### ADR-012: AI Scope -- Regime Detection + Quarterly Audits Only
**Date:** 2026-02-09 | **Status:** Decided | **Rationale:** Claude AI for regime detection is high value/low cost (~1 call/day, $2-5/mo). Screening/ranking candidates algorithmically is sufficient and avoids expensive per-candidate API calls.
**Decision:** Claude AI limited to (1) daily market regime detection for options strategy selection, (2) quarterly income quality audits. All scanning, filtering, entry/exit, position sizing is algorithmic.
**Consequences:** Total Claude API cost ~$2-10/month. System must have rule-only fallback if Claude unavailable.

---

### ADR-013: Options Strategies Are Phase 1 Core
**Date:** 2026-02-09 | **Status:** Decided | **Rationale:** Options are the core of the 30% sleeve, not an add-on. The system can't function without them. Phase 1 expanded from 8 to 10 weeks to accommodate.
**Decision:** Credit spreads, iron condors, CSPs, and calendar spreads are Phase 1 MVP features. Phase 1 is 10 weeks.
**Consequences:** More complex Phase 1, but the system is complete at end of Phase 1 (not partially functional).

---

### ADR-014: Split Quality Tier
**Date:** 2026-02-09 | **Status:** Decided | **Rationale:** Trading logic errors can lose real money. Non-critical code (notifications, reporting) doesn't need the same rigor.
**Decision:** Rigorous testing (unit + integration + E2E) for trading-critical code (risk engine, order generation, position sizing, options calculations, IV rank). Standard testing (unit + integration) for everything else.
**Consequences:** More test code for core trading logic. PR reviews required for trading-critical changes.

---

### ADR-015: Monthly Cost Ceiling
**Date:** 2026-02-09 | **Status:** Decided | **Rationale:** Keep operational costs sustainable. Estimate: Azure ~$10-30, Polygon.io $29, Claude API ~$2-10 = $40-65 total.
**Decision:** Total monthly infrastructure cost must stay under $100. Track costs in logs with alerts if approaching ceiling.
**Consequences:** Constrains AI usage (no expensive per-candidate analysis). May need to revisit if Polygon.io price increases.

---

### ADR-016: Polygon.io as Separate Project
**Date:** 2026-02-10 | **Status:** Decided | **Rationale:** Polygon.io is an external data source with its own HTTP client, rate limiting, and DTOs. Separate project keeps boundaries clean and allows independent testing.
**Decision:** `TradingSystem.MarketData.Polygon` as a separate project referencing Core.
**Consequences:** Additional project in solution. Clean dependency graph.

---

### ADR-017: CachingMarketDataService Location
**Date:** 2026-02-10 | **Status:** Decided | **Rationale:** The service orchestrates broker calls with caching and regime detection — it's strategy-layer logic, not broker-layer.
**Decision:** `CachingMarketDataService` lives in `TradingSystem.Strategies/Services/`.
**Consequences:** Strategies project depends on Core interfaces only, not on IBKR directly.

---

### ADR-018: IV History Persistence
**Date:** 2026-02-10 | **Status:** Decided | **Rationale:** IV history is expensive to fetch (1-year of daily data from IBKR). Caching to JSON avoids repeated API calls within the same trading day.
**Decision:** IV history persisted to JSON files per symbol, expires daily (stale if `LastUpdated.Date < DateTime.Today`).
**Consequences:** Requires `TradingSystem.Storage` dependency. First call each day is slow; subsequent calls are fast.

---

### ADR-019: IBKR Option Chain Pacing
**Date:** 2026-02-10 | **Status:** Decided | **Rationale:** IBKR enforces 50 concurrent market data requests. Need headroom for other data needs.
**Decision:** SemaphoreSlim(45) + 100ms delay between option snapshot requests.
**Consequences:** Option chain retrieval is throttled but stays within IBKR limits. Full chain scan takes ~5-15 seconds depending on strike count.

---

### ADR-020: Dynamic Sleeve Activation and Live Allocation
**Date:** 2026-02-16 | **Status:** Decided | **Rationale:** Fixed 70/30 live allocation is too rigid. Sleeves should earn live capital through paper validation, and activation may be staged.
**Decision:** Run both sleeves in paper validation. For live trading, activate one or both sleeves based on validation results. Human chooses final capital split at live transition. Expected deployable capital range is ~$100,000-$400,000 at decision time, with a minimum of $100,000 per active sleeve account.
**Consequences:** System must provide sleeve-level readiness scorecards and allocation recommendations. Live path must not hard-code 70/30.

---

### ADR-021: Rebalancing and Capital Flows Are Human-Executed
**Date:** 2026-02-16 | **Status:** Decided | **Rationale:** Capital movement decisions require human context (taxes, external cash needs, account constraints).
**Decision:** System provides rebalance/transfer/withdrawal recommendations; human executes actual rebalancing and cash movement. For options sleeve, only free cash may be withdrawn (not cash reserved against open options positions). For income sleeve, cash withdrawals and occasional stock sales for withdrawals are allowed.
**Consequences:** Reporting must include recommendation rationale and collateral-aware free-cash calculations. No automatic rebalance/capital-transfer execution.

---

### ADR-022: Phase-Gated Clarification Prompts
**Date:** 2026-02-16 | **Status:** Decided | **Rationale:** Unknowns should not stall implementation, but dependent automation must pause for owner input at defined gates.
**Decision:** Add explicit phase-gated checkpoints where Claude prompts the owner before proceeding with dependent automation (options lifecycle rules, recommendation format, paper validation criteria, live activation split, post-live tuning).
**Consequences:** Roadmap/progress docs must maintain gate checkpoints. Claude should stop and prompt at each gate before advancing.

---

### ADR-023: Cost Ceiling Scope and Brokerage Forecasting
**Date:** 2026-02-16 | **Status:** Decided | **Rationale:** Platform cost control and brokerage activity costs behave differently and should be tracked separately.
**Decision:** The <$100 monthly ceiling applies to platform costs (Azure + Polygon.io + Claude API). Brokerage commissions/fees are tracked and forecasted separately (conservative per contract-side model in reporting).
**Consequences:** Reports must break out platform vs brokerage costs and include activity-based fee forecasts.

---

### ADR-024: Pre-Market Orchestrator Degrades Gracefully When Options DI Is Incomplete
**Date:** 2026-02-16 | **Status:** Decided | **Rationale:** Week 8 wiring needs to land before Week 9 risk engine is complete. Scheduled pre-market runs should not hard-fail while `IRiskManager` is still missing.
**Decision:** `DailyOrchestrator` attempts options sleeve execution each pre-market run, but if required options dependencies cannot be resolved, it logs a warning and skips options execution for that run. Broker connect/disconnect handling remains explicit and safe.
**Consequences:** Timer runs stay healthy during staged integration and retain a defensive skip path for future DI regressions. Options sleeve runtime activation now depends on concrete registrations being present (including `IRiskManager`).

---

### ADR-025: Persisted Snapshot Baselines for Risk Stops and Drawdown Alerts
**Date:** 2026-02-16 | **Status:** Decided | **Rationale:** Stop logic based only on current unrealized P&L is too noisy and does not track true account drawdown over time.
**Decision:** `RiskManager` uses `ISnapshotRepository` baselines for daily/weekly P&L and computes high-water mark/current/max drawdown from persisted snapshots. Stop alerts are sent through `IRiskAlertService` only on state transitions (new trigger events) to avoid duplicate alert spam.
**Consequences:** Risk metrics now require snapshot persistence registration in DI. Discord webhook configuration is required for external alert delivery; without it alerts degrade to logs.

---

### ADR-026: Consolidate OptiTrade into TradingSystem
**Date:** 2026-04-07 | **Status:** Decided | **Rationale:** OptiTrade (Python options backtest system) and TradingSystem (C#/.NET 8 two-sleeve platform) overlap in scope but TradingSystem is far more mature (418+ tests, working IBKR/risk/options/income infrastructure vs 5-file Python scaffold). Maintaining two repos doubles effort for no benefit. OptiTrade's backtest pipeline (Python scripts driving QC REST API) is the only asset worth preserving.
**Decision:** TradingSystem is the primary repo. OptiTrade is archived (tagged `v1.0-archive`). Backtest pipeline migrated to `tools/backtest/`. Iron condor findings inform options sleeve parameters. SPX backtesting is the next critical path.
**Consequences:** All future trading work in this repo. Python tooling lives in `tools/backtest/` with its own `requirements.txt`. The backtest pipeline is a developer tool, not part of the .NET runtime.

---

### ADR-027: Dual-Mode Strategy — Alpha-Seeking vs Income+Protection
**Date:** 2026-04-07 | **Status:** Decided | **Rationale:** User's goals differ by life phase. While working full-time (next ~5 years), primary goal is generating alpha (beating S&P 500). In retirement, goal shifts to 5-8% consistent yield with drawdown protection. System should support both modes via configurable profiles.
**Decision:** Build Mode A (alpha-seeking) first. Options sleeve is the primary alpha engine, not a supplement. SPX options (commission-efficient) are the focus. Income sleeve is secondary while working. The 70/30 income/options split from the original design may invert to 30/70 or 50/50 for Mode A. Mode B (income+protection) can reuse the same infrastructure with different allocation weights.
**Consequences:** Options sleeve priority increases. SPX iron condor backtest is the immediate next step. Sleeve allocation weights become configurable. Return targets recalibrated: alpha-seeking benchmarked against SPY, not absolute yield targets.

---

### ADR-028: Iron Condor Backtest Findings — SPY Parameters and Limitations
**Date:** 2026-04-07 | **Status:** Decided | **Rationale:** 8 OptiTrade backtests (2019-2025) proved SPY iron condors produce +0.07% CAGR on $400K — essentially breakeven. The edge is real (PF 2.0, 72% win rate) but commission drag ($2.60/spread vs $1.86-2.40 credit) consumes it. SPX (100x multiplier) should eliminate this drag.
**Decision:** SPY iron condor parameters locked: IV>=18% ATM, PT=70%, MinCredit=$2.00, SL=2.0x, Wing=10pt, Delta=0.16. These are reference parameters for the options sleeve. SPX iron condor backtest is the highest priority — same logic, 10x better commission-to-credit ratio. If SPX passes gate with meaningful CAGR, iron condors become a core strategy. If not, iron condors are demoted to minor/inactive.
**Consequences:** SPX backtest determines whether iron condors are worth running in production. `tools/backtest/algorithms/IronCondorBaseline.cs` preserves the validated SPY algorithm. Pipeline in `tools/backtest/` is ready for SPX variant.

---

### ADR-029: Claude Gateway Transport — Plaintext Loopback HTTP with Static Bearer Token
**Date:** 2026-05-29 | **Status:** Decided
**Rationale:** The Claude Gateway (subscription-priced CLI bridge) runs as a local process on `localhost:3131` on the same trusted host as the Functions worker. `ClaudeService` reaches it over plaintext HTTP, authenticating with a static `Bearer` token (`Claude:GatewayApiKey`). The question is whether this transport is acceptable or whether it needs TLS / a non-network IPC channel.
**Decision:** Keep plaintext HTTP over loopback (`http://localhost:3131/`) with the static Bearer token. This is acceptable because the listener is bound to the loopback interface only — traffic never leaves the host, so there is no on-wire interception surface, and the Bearer token guards against other local processes that lack the secret. The named `ClaudeGateway` `HttpClient` is created via `IHttpClientFactory` with a short configurable timeout (`GatewayTimeoutSeconds`, default 8s) so a hung gateway fails fast to the metered direct API (which itself fails closed to deterministic rules when no key is present). This ADR DOCUMENTS the current stance only — it does NOT introduce TLS or named-pipe transport in this change.
**Alternatives considered:**
- *HTTPS with a self-signed certificate on loopback* — DEFERRED. Adds certificate generation, trust-store management, and rotation overhead for a channel that never leaves the host; no meaningful confidentiality gain over a loopback-bound socket. Revisit only if the gateway is ever moved off-box.
- *Windows named pipe (or Unix domain socket) IPC* — DEFERRED. Removes the network surface entirely and is the strongest option, but requires a different client/transport abstraction in both the gateway and `ClaudeService`. Out of scope for this refactor; a candidate if a future threat model rules out any local TCP listener.
- *No auth token (rely on loopback binding alone)* — REJECTED. Any local process could then call the subscription-priced gateway; the static Bearer token is a cheap defense-in-depth layer worth keeping.
**Consequences:** Gateway base address and timeout are bound from the `Claude` config section, so the loopback URL and fail-fast timeout are tunable without code changes. If the gateway is ever relocated to another host, this ADR must be superseded — plaintext + static token is NOT acceptable off-loopback. The deferred TLS/named-pipe options remain explicitly un-built.

---

### ADR-030: Paper Trading Is the Validation Gate for Options/Complex Strategies — Not Backtesting
**Date:** 2026-05-29 | **Status:** Decided
**Rationale:** Backtesting complex/options strategies in this system has proven unreliable as a
go/no-go signal: (1) data-reliability and aggregation doubts in the QC pipeline make results
hard to trust at the precision a gate needs; (2) runs are slow, lengthening the feedback loop;
(3) the SPX iron-condor backtest came back inconclusive — NOT a demotion of iron condors, just
not a clean pass/fail. Forward paper trading in SANDBOX exercises the real execution path
(IBKR, screening, lifecycle, risk) on live data and is the more trustworthy validation gate.
**Decision:** Paper trading (SANDBOX, forward) is the validation gate for options and complex
multi-leg strategies. Backtesting is a research/sanity aid, not the activation gate. The
SPX iron-condor result is treated as inconclusive (iron condors are neither promoted nor
demoted on backtest evidence alone). EXCEPTION: simple stock-trade strategies may still use
backtesting as a reasonable gate, since their fills/aggregation are well-modeled. The
backtest-distillation / gate-evaluation work is SHELVED (see backlog).
**Alternatives considered:**
- *Keep SPX backtest as the go/no-go gate (status quo per ADR-026/028)* — REJECTED for complex
  strategies. Data-reliability/aggregation doubts and an inconclusive SPX run mean a backtest
  pass/fail would gate capital on a signal we do not trust.
- *Invest to harden the backtest pipeline (fix aggregation, speed) until it is gate-grade* —
  DEFERRED. High effort against a tool whose modeling of multi-leg fills is the core doubt;
  not worth it before paper validation has run. Distillation work shelved to backlog, not deleted.
- *No formal gate; activate on judgment* — REJECTED. A defined forward-paper gate is needed
  before any SANDBOX→LIVE step (ties into PDR-004/PDR-005 sleeve thresholds).
**Consequences:** Supersedes the "SPX backtest is the next critical path / go-no-go" stance in
ADR-026 and ADR-028 for options/complex strategies — those ADRs' SPX-gate language is now
historical. `tools/backtest/` remains for research and the simple-stock-trade exception; it is
no longer on the activation critical path. Phase 3 paper validation (PDR-004/005 thresholds)
becomes the gate to define numerically. Iron-condor parameters in ADR-028 remain valid reference
parameters; only their gating role changes.

---

## Pending Decisions

### PDR-001: Intraday vs Daily Execution for Options
**Blocking:** Options roll/close timing | **Needs:** Paper trading results from Phase 1
Start with daily batch, assess need for intraday monitoring (especially near-expiry positions). Deferred to post-Phase 1.

### PDR-002: ~~Backtesting Engine Scope~~ RESOLVED
**Resolution:** ADR-026 — use OptiTrade's QuantConnect cloud pipeline (migrated to `tools/backtest/`). No custom backtesting engine needed. QC REST API automates compile/run/collect. Python scripts drive it.

### PDR-003: Swing Trade Third Sleeve
**Blocking:** Nothing (independent) | **Needs:** Options sleeve proven in live trading
Potential future addition after options sleeve is validated.

### PDR-004: Sleeve-Level Validation Thresholds for Live Activation
**Blocking:** Final go-live recommendation logic | **Needs:** Numeric pass/fail criteria by sleeve
Define exact per-sleeve thresholds (return/risk/consistency metrics) that qualify a sleeve for live activation.

### PDR-005: Initial Live Sleeve Set and Capital Split
**Blocking:** Live transition execution plan | **Needs:** Paper validation outputs + owner decision
Select which sleeve(s) activate first and final initial capital split/account mapping at live transition.

---

## Project State Snapshot | 2026-04-07 | Post-Consolidation

**Phase:** 1 — Foundation (Week 9 complete, 418 tests passing). OptiTrade consolidated.

### Unified Phase Structure

| Phase | Weeks | Status | Goal |
|---|---|---|---|
| 1. Foundation | 1-10 | Week 9 done, Week 10 pending | IBKR, sleeves, risk, orchestration, Claude regime |
| 2. Integration + SPX Backtests | 11-16 | Pending | Migrate backtest pipeline (done), SPX backtests, strategy lockdown |
| 3. Paper Validation | 17-28 | Pending | 12+ weeks autonomous paper trading |
| 4. Live Transition | 29-32 | Pending | Staged go-live with human approval |
| 5. Stabilization | 33-44+ | Pending | Tuning, performance reviews |

### Completed Through Week 9
- Weeks 1-8: IBKR connection, market data, storage, orders, income sleeve, option chains, IV rank, screening, Polygon.io calendar, multi-leg orders, options lifecycle, execution service, orchestration wiring, pre-market tests
- Week 9: Concrete `RiskManager` with per-trade checks, stop-halt, position/cap enforcement, no-trade windows; snapshot-backed drawdown tracking; Discord stop alerts via `IRiskAlertService`; Azure.Identity upgraded to 1.17.1
- 2026-04-07: OptiTrade consolidated (ADR-026). Backtest pipeline migrated to `tools/backtest/`. Iron condor findings recorded (ADR-028).

### Blockers (as of 2026-04-07)
- Discord webhook: server/channel not yet created — needed for stop alerts
- Claude API key: needed for regime service integration (Week 10)

### Next Steps
1. Complete Phase 1 (Week 10): Claude regime service, Discord webhook
2. SPX iron condor backtest via `tools/backtest/` — the alpha-seeking critical path
3. SPX credit spread backtests if iron condor passes
4. Begin Phase 3 paper validation (~June 2026)

## Known Debt

| ID | Description | Impact | Logged |
|---|---|---|---|
| KD-001 | Discord webhook not configured | Stop alerts silently drop | 2026-02-16 |
| KD-002 | Claude API key not provisioned | Regime service integration blocked | 2026-02-16 |
| KD-003 | Backtest pipeline paths use Python (not .NET) | Must install Python + deps separately for backtesting | 2026-04-07 |
| KD-004 | CachingMarketDataService.cs ~360 lines, exceeds 300-line architecture-fitness threshold (grew across S2-001/003/005/006) | Maintainability; single class concentrates broker-call orchestration, caching, regime caching/stampede guard, and cost-cap fallback. Decompose by responsibility within TradingSystem.Strategies/Services/ per ADR-017 | 2026-05-29 |
