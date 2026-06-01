# OptiMind Risk Management Framework

**Last Updated:** 2026-02-20
**Status:** Canonical risk policy

---

## Purpose

Risk management is safety-critical.
This framework defines hard limits, emergency behavior, and required controls for data quality and execution.

## Risk Objectives

1. Prevent single-event account damage.
2. Keep risk enforcement deterministic and auditable.
3. Ensure no AI or strategy module can bypass hard limits.

## Canonical Hard Limits

Source of truth for hard limits is `optimind/core/constants.py`.

| Limit | Value |
|---|---|
| Max risk per trade | 2.5% of NLV |
| Max deployed capital | 40% of NLV |
| Max positions per underlying | 2 |
| Max sector positions | 3 |
| Delta warn/limit | 7% / 10% of NLV |
| Vega limit | 1.5% of NLV (enforced Phase 2+) |
| Margin utilization limit | 60% Reg-T / 40% PM |
| Daily loss halt | -3% |
| Daily emergency close-all | -5% |
| Weekly loss limit | -5% |
| Monthly loss limit | -10% |
| Max adjustments per position | 2 |

## Risk Layers

## Layer 1: Pre-trade risk checks

All checks must pass before order submission:

- per-trade max risk,
- deployment and margin limits,
- concentration limits,
- liquidity checks,
- account mode and safety checks.

## Layer 2: Continuous portfolio monitoring

During market hours (9:30 AM – 4:00 PM ET; 4:15 PM for SPX positions):

- aggregate Greeks,
- realized + unrealized PnL,
- margin cushion,
- position threat states.

## Layer 3: Temporal circuit breakers

- Daily -3%: halt new entries.
- Daily -5%: close all positions and enter lockdown.
- Weekly/monthly limits: halt additional risk until reset window.

## Layer 4: Existential safeguards

- defined-risk spreads only,
- emergency procedures documented and tested,
- manual unlock required after emergency stop.

## Emergency Close Procedure

When daily emergency threshold is hit:

1. Stop new order creation.
2. Queue all open positions for close.
3. Use aggressive pegged limits before market orders.
4. If no fill in extreme gap conditions, escalate per runbook.
5. Enter lockdown state and require explicit manual restart.

## Data Integrity Controls

Market data must be validated before risk calculations:

- bounds checks for prices and Greeks,
- staleness checks,
- per-leg timestamp coherence for combo positions,
- rejection and alerting on repeated integrity violations.

No raw broker tick may bypass validation.

## Instrument Policy

## SPX and XSP index options

- European exercise style.
- Cash-settled index options.
- Section 1256 tax treatment expected (subject to current tax rules).

## SPY/ETF and single-stock options

- Generally American-style exercise risk.
- Assignment risk exists before expiration.
- Non-1256 tax treatment and wash-sale considerations apply.

## Policy implications

1. Assignment-aware monitoring is mandatory for American-style instruments.
2. Position management and reporting must classify contracts by settlement and tax treatment.
3. Tax reports must separate 1256 and non-1256 logic paths.

## Testing Requirements

Safety-critical minimums:

1. High unit coverage for risk logic.
2. Integration tests for order flow with risk enforcement.
3. Scenario tests for crash, gap, and disconnection conditions.
4. Property-based tests for invariants (no limit bypass, defined-risk math stability).

## Operational Requirements

- All risk events logged with reason and context.
- Alerts for approaching limits and triggered breakers.
- Runbook drills before live mode.

## Governance

- Gate requirements: `docs/VALIDATION_GATES.md`.
- Risk-limit changes require ADR entry in `DECISIONS.md`.
