# Phase 1: Foundation, Backtesting, and First Full Paper Lifecycle

**Window:** Weeks 1-10
**Effort:** 150-220 hours at 15-20 hrs/week
**Mode:** paper only
**Last Updated:** 2026-02-20

---

## Phase Goal

Validate core strategy viability before scaling implementation complexity, then deliver one complete iron condor paper lifecycle from scan to close.

## Entry Criteria

- Repository scaffold available.
- IBKR paper account access configured.

## Exit Criteria

1. Phase 1 gate in `docs/VALIDATION_GATES.md` is passed.
2. One complete paper lifecycle is executed and documented.
3. Backtest viability evidence is recorded.

---

## Execution Order Note

Sprint 1.1 (runtime scaffold) was implemented before Sprint 1.0 (LEAN backtest). This order was intentional: understanding the Python package structure and IBKR connection behavior first reduced risk of misaligned assumptions in the backtest. The sprint numbers reflect dependency priority (LEAN viability is the strategic prerequisite), not the order of implementation. See `DECISIONS.md` ADR-015.

---

## Sprint 1.0 (Weeks 1-2 planned; implemented after Sprint 1.1): LEAN Backtest Viability

### Deliverables

- `optimind/config/strategies.yaml` — canonical strategy parameter source (required before LEAN work begins).
- `optimind/config/risk_limits.yaml` — non-hardcoded risk parameter overrides.
- `optimind/config/watchlist.yaml` — underlyings to monitor.
- `optimind/config/sectors.yaml` — sector correlation mapping.
- `scripts/generate_lean_config.py` — config translator: reads `strategies.yaml`, emits `backtests/lean/Config/StrategyConstants.cs`.
- `scripts/evaluate_phase1_gate.py` — gate evaluator: validates `phase1_baseline.json` against all Phase 1 gate criteria and prints pass/fail.
- `backtests/lean/` C# baseline strategy implementation (Algorithm/, Config/, lean.json).
- Baseline backtest metrics for 2019-2025.
- In-sample vs out-of-sample sensitivity summary.

### Required Metrics

- Net CAGR, Sharpe, max drawdown, win rate, profit factor.
- Slippage drag analysis.
- In-sample/out-of-sample delta.

### Go/No-Go Rules

- If net CAGR <= 0 or severe overfit is detected, pause runtime feature expansion and redesign parameters.

---

## Sprint 1.1 (Weeks 3-4): Runtime Scaffold and IBKR Connection

### Deliverables

- Async Python package structure.
- Stable IBKR connection handling for paper mode.
- `OPTIMIND_MODE` routing for paper/live endpoints.

### Notes

- Use async-safe SQLite (`aiosqlite`) for any DB operations.
- No live switching in Phase 1.

---

## Sprint 1.2 (Weeks 5-6): Chain Data and Greeks

### Deliverables

- Options chain retrieval for SPX/SPY/QQQ/IWM.
- Filtering by DTE and delta targets.
- Greeks pipeline with validation checks.
- IV rank calculation support.

### Acceptance

- Greeks discrepancy checks implemented and monitored.
- Pacing and throttling logic enforced.

---

## Sprint 1.3 (Weeks 7-8): Iron Condor Construction and Execution

### Deliverables

- Strategy object and contract selection logic.
- 4-leg BAG/combination order construction.
- SmartPricing walk logic for entry/exit.
- Fill and position state tracking.

### Acceptance

- Position risk computed before order submission.
- Order adjustments logged with timestamps.

---

## Sprint 1.4 (Weeks 9-10): Monitoring, Exit Rules, and CLI

### Deliverables

- Position monitor loop during market hours.
- Exit logic:
  - 50% profit target,
  - 200% stop loss,
  - DTE management at 21/14/7.
- CLI commands for status, scan, trade, close, mode, history.

### Acceptance

- One complete lifecycle documented with artifacts.

---

## Dependencies

- QuantConnect account and .NET SDK (Sprint 1.0).
- IBKR paper account and market data subscription.
- Python 3.12 environment and uv workflow.
- n8n (optional, local) for Gate Evaluation Runner workflow (ADR-019). Automates gate script execution and report delivery via email. Not blocking — gate scripts can be run manually.

## Cost Expectation

- Typical Phase 1 monthly external cost: low (mostly market data).
- Canonical production cost scenarios are tracked in `docs/COST_MODEL.md`.

## Out of Scope

- Live trading.
- AI allocation logic.
- Production deployment hardening.

## Phase 1 Evidence Checklist

- [ ] Backtest report committed.
- [ ] Slippage-cost gate result documented.
- [ ] Overfitting check documented.
- [ ] Full paper lifecycle evidence captured.
- [ ] Gate scoreboard updated in `PROGRESS.md`.
