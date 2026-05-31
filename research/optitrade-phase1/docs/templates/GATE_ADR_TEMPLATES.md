# Gate ADR Templates

Use these blocks when a gate passes. Paste into `DECISIONS.md`, then fill placeholders.

## Phase 1 -> ADR-015

```md
## ADR-015: Phase 1 Strategy Viability Gate - Passed
**Status:** Accepted
**Date:** YYYY-MM-DD
**Gate:** Phase 1 Strategy Viability

| Check | Value | Threshold | Pass |
|---|---|---|---|
| Net CAGR | X.X% | > 0% | yes |
| Max drawdown | X.X% | <= 25% | yes |
| IS/OOS delta | X.X pp | <= 3 pp | yes |
| Slippage drag | X.X% | <= 30% | yes |

**Data range:** 2019-01-01 to 2025-12-31
**Backtest report:** backtests/lean/results/phase1_baseline.json
**Parameter hash:** <sha256>
```

## Phase 2 -> ADR-016

```md
## ADR-016: Phase 2 Execution and Risk Gate - Passed
**Status:** Accepted
**Date:** YYYY-MM-DD
**Gate:** Phase 2 Execution and Risk

| Check | Value | Threshold | Pass |
|---|---|---|---|
| Trading days | XX | >= 20 | yes |
| Hard limit violations | X | = 0 | yes |
| Open P0 defects | X | = 0 | yes |
| Risk telemetry complete | true/false | true | yes |

**Report:** reports/gates/phase2_paper_ops.json
```

## Phase 3 -> ADR-017

```md
## ADR-017: Phase 3 AI Value and Safety Gate - Passed
**Status:** Accepted
**Date:** YYYY-MM-DD
**Gate:** Phase 3 AI Value and Safety

| Check | Value | Threshold | Pass |
|---|---|---|---|
| Return delta (annualized) | X.X pp | >= 1.5 pp | yes |
| Drawdown delta | X.X pp | <= 2.0 pp | yes |
| Bootstrap resamples | XXXX | >= 2000 | yes |
| 90% CI lower bound | X.XXXX | > 0 | yes |
| Fallback test | true/false | true | yes |

**Report:** reports/gates/phase3_ai_ablation.json
```

## Phase 4 -> ADR-018

```md
## ADR-018: Phase 4 Live Readiness Gate - Passed
**Status:** Accepted
**Date:** YYYY-MM-DD
**Gate:** Phase 4 Live Readiness

| Check | Value | Threshold | Pass |
|---|---|---|---|
| Runbook complete | true/false | true | yes |
| Emergency close tested | true/false | true | yes |
| Monitoring verified | true/false | true | yes |
| Critical parity unknowns | X | = 0 | yes |
| Production stability days | XX | >= 14 | yes |
| Staged ramp compliance | true/false | true | yes |

**Report:** reports/gates/phase4_live_readiness.json
```

