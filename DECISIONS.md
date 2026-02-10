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

## Pending Decisions

### PDR-001: Intraday vs Daily Execution for Options
**Blocking:** Options roll/close timing | **Needs:** Paper trading results from Phase 1
Start with daily batch, assess need for intraday monitoring (especially near-expiry positions). Deferred to post-Phase 1.

### PDR-002: Backtesting Engine Scope
**Blocking:** Strategy optimization | **Needs:** Decision on simple/medium/full scope
Defer until post-live planning.

### PDR-003: Swing Trade Third Sleeve
**Blocking:** Nothing (independent) | **Needs:** Options sleeve proven in live trading
Potential future addition after options sleeve is validated.

---
