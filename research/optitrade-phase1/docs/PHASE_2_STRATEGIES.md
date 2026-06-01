# Phase 2: Strategy Engine and Risk Layer

**Window:** Weeks 11-20
**Effort:** 150-220 hours at 15-20 hrs/week
**Mode:** paper only
**Last Updated:** 2026-02-20

---

## Phase Goal

Run multiple strategies concurrently in paper mode while enforcing all hard risk controls and guided execution flow.

## Entry Criteria

- Phase 1 gate passed.

## Exit Criteria

1. Phase 2 gate in `docs/VALIDATION_GATES.md` is passed.
2. >= 4 weeks continuous paper operation without hard risk-limit violations.
3. Multi-strategy scheduling and monitoring are stable.

---

## Sprint 2.1 (Weeks 11-12): Strategy Engine Foundation

### Deliverables

- Strategy base interface and registry.
- Config-driven strategy activation.
- Implemented strategies:
  - iron condor,
  - credit spreads,
  - butterfly (initial).

### Acceptance

- Strategies can be scheduled and evaluated in a common pipeline.

---

## Sprint 2.2 (Weeks 13-15): Portfolio Risk Controls

### Deliverables

- Pre-trade risk checker with hard limits.
- Margin utilization and concentration enforcement.
- Portfolio Greeks monitoring:
  - delta warn 7%, hard limit 10% of NLV,
  - vega limit 1.5% of NLV.
- Circuit breakers:
  - daily halt -3%,
  - daily emergency -5%,
  - weekly -5%,
  - monthly -10%.

### Acceptance

- All risk checks are auditable and tested.

---

## Sprint 2.3 (Weeks 16-17): Threat Detection and Adjustments

### Deliverables

- Threat-level classification for open positions.
- Adjustment recommendations and bounded adjustment budget.
- Max 2 adjustments per position enforced.

### Acceptance

- Adjustments are traceable and cannot bypass risk checks.

---

## Sprint 2.4 (Weeks 18-20): Guided Execution and Paper Soak

### Deliverables

- Guided mode workflow:
  - recommend,
  - notify,
  - approve/reject,
  - execute.
- Notification hooks for risk and trade events.
- Continuous paper soak period begins.

### Acceptance

- Stable operation across all active strategies during soak period.

---

## n8n Operations Workflows (Phase 2)

n8n workflows provide operational automation without adding complexity to the trading runtime. See ADR-019 and `docs/ARCHITECTURE.md` § Operations Automation.

| Workflow | Trigger | Deliverable Sprint |
|----------|---------|-------------------|
| Trade Alerts | Webhook (ORDER_FILLED, POSITION_CLOSED events) | Sprint 2.1 |
| Daily P&L Report | Cron (market close + 30min) | Sprint 2.2 |
| Risk Alert Escalation | Webhook (CIRCUIT_BREAKER_FIRED) | Sprint 2.2 |
| IBKR Health Monitor | Cron (1min during market hours) | Sprint 2.1 |

**Integration:** OptiMind publishes events via HTTP POST to n8n webhook endpoints. n8n workflows are read-only — they consume events and produce notifications/reports but never modify trading state.

---

## Dependencies

- Completed Phase 1 artifacts.
- Reliable IBKR paper connectivity and data feed.
- n8n running locally with Anthropic credential configured (for Claude-powered report formatting).

## Out of Scope

- Live deployment.
- AI strategy weighting as authoritative allocator.

## Phase 2 Evidence Checklist

- [ ] No hard risk-limit violations during qualifying window.
- [ ] Circuit-breaker behavior tested and documented.
- [ ] Multi-strategy metrics captured.
- [ ] Gate report produced at `reports/gates/phase2_paper_ops.json`.
- [ ] Gate scoreboard updated in `PROGRESS.md`.
