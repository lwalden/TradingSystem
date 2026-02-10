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

## Pending Decisions

### PDR-001: Intraday vs Daily Execution
**Blocking:** Tactical sleeve architecture | **Needs:** Paper trading results from Phase 1
Start with daily batch, assess need based on paper trading results. Deferred to Phase 2.

### PDR-002: Backtesting Engine Scope
**Blocking:** Phase 3 planning | **Needs:** Decision on simple/medium/full scope
Defer until Phase 3 planning.

---
