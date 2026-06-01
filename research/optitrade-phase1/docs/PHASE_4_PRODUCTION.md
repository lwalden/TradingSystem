# Phase 4: Production Hardening and Go-Live

**Window:** Weeks 31-42+
**Effort:** 180-260 hours at 15-20 hrs/week
**Mode:** transition from paper to staged live
**Last Updated:** 2026-02-20

---

## Phase Goal

Harden operations, validate parity assumptions, deploy production infrastructure, and execute staged go-live safely.

## Entry Criteria

- Phase 3 gate passed.

## Exit Criteria

1. Phase 4 gate in `docs/VALIDATION_GATES.md` is passed.
2. Production environment is stable.
3. First live trade executed under staged controls.

---

## Sprint 4.1 (Weeks 31-33): Parity and Parameter Lock

### Deliverables

- Backtest vs paper divergence report.
- Updated walk-forward analysis.
- Final production parameter lock with rationale.

### Acceptance

- Unexplained critical parity divergence is not allowed to proceed.

---

## Sprint 4.2 (Weeks 34-36): Analytics and Operations Visibility

### Deliverables

- Performance dashboard.
- Risk and incident observability views.
- Reporting exports for review and audit.

### Acceptance

- Core performance and risk metrics visible and auditable.

---

## Sprint 4.3 (Weeks 37-39): Production Hardening

### Deliverables

- Deployment automation and service supervision.
- Health checks and alerting.
- Incident and recovery runbooks.

### Acceptance

- Production deployment stable for >= 2 weeks prior to live mode.

---

## Sprint 4.4 (Weeks 40-42+): Staged Go-Live

### Stage Plan

1. Week 1 live:
   - one iron condor,
   - one contract,
   - minimal risk.
2. Week 2 live:
   - limited multi-position exposure.
3. Week 3-4 live:
   - gradual sizing toward normal limits if no critical incidents.

### Acceptance

- Any critical incident pauses scaling.

---

## Live Monitoring Protocol

- Daily operational and risk review.
- Weekly live-vs-paper comparison.
- Monthly parity and performance review.

Immediate alerts for:

- risk-limit breaches,
- execution anomalies,
- data integrity failures,
- runtime downtime during market hours.

---

## Cost and ROI Governance

- Canonical cost scenarios: `docs/COST_MODEL.md`.
- Use low/base/high scenario framing for all ROI statements.

## Dependencies

- Phase 1-3 evidence complete.
- Portfolio Margin and account approvals complete.

## Phase 4 Evidence Checklist

- [ ] Parity report completed.
- [ ] Pre-live runbook tested.
- [ ] Production stability window passed.
- [ ] Staged live evidence captured.
- [ ] Gate report produced at `reports/gates/phase4_live_readiness.json`.
- [ ] Gate scoreboard updated in `PROGRESS.md`.
