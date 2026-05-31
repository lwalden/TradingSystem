# OptiMind Validation Gates

Last updated: 2026-02-20
Status: Canonical go/no-go gates

## Purpose

This file defines hard pass/fail gates for phase advancement.
No phase advances unless all required gates are passed or an explicit waiver is recorded in `DECISIONS.md`.

## Canonical Performance Framing

- The 8-15% annual return target is a hypothesis, not a guarantee.
- Risk objective is lower volatility and smaller drawdowns than SPX, not SPX outperformance.
- Evidence must be net of realistic commissions and slippage assumptions.

## Data Conventions (Required)

- `max_drawdown` is always a positive magnitude in [0.0, 1.0].
  - Example: 0.18 means 18% drawdown.
- Signed drawdown values (example: -0.18) are invalid for gate acceptance and must be normalized before evaluation.

## Phase 1 Gate (Strategy Viability)

Required evidence:

1. Historical backtest quality gate (2019-01-01 through 2025-12-31):
   - Net CAGR > 0%.
   - Max drawdown <= 25% in backtest context.
   - Metrics reported: CAGR, Sharpe, max drawdown, win rate, profit factor.
2. Overfitting gate:
   - In-sample/out-of-sample CAGR delta <= 3 percentage points.
3. Slippage-cost gate:
   - Slippage drag <= 30% of gross strategy profit over test period using baseline assumption.
4. Implementation gate:
   - Backtest parameter source is generated from `config/strategies.yaml`.
   - Parameter generation and type checks are reproducible.

Fail condition (hard stop):

- If Phase 1 fails gates 1 or 2, stop execution-engine expansion and redesign strategy parameters before continuing.

## Defect Severity Definitions

For gate evaluation purposes, defect severity is defined as follows:

- **P0 (Critical):** Any defect that could cause incorrect order execution, a hard risk-limit bypass, incorrect position state, incorrect P&L accounting, or uncontrolled live capital exposure. P0 defects block gate passage and must be resolved before the gate can be evaluated.
- **P1 (High):** Defects that degrade system reliability or observability but do not cause unsafe behavior (e.g., non-critical monitoring gaps, non-blocking CLI errors). Must be tracked but do not block gate passage.
- **P2 (Medium/Low):** Cosmetic, UX, or non-safety-impacting issues. Tracked in issue log but no gate impact.

## Phase 2 Gate (Execution and Risk Control)

Required evidence:

1. Continuous paper operation for >= 4 weeks.
2. Zero hard risk-limit violations.
3. No unresolved P0 defects in risk checks, order routing, or position state transitions.
4. Risk telemetry complete:
   - Daily/weekly/monthly loss tracking.
   - Portfolio delta tracking (warn at 7%, limit at 10% of NLV).
   - Margin utilization checks.

Fail condition:

- Any uncaught hard-limit violation or unresolved P0 defect resets the Phase 2 gate clock after remediation.

## Phase 3 Gate (AI Value and Safe Degradation)

Required evidence:

1. AI-vs-static allocation ablation over >= 8 weeks paper data.
2. Practical improvement thresholds:
   - Annualized net return delta (AI - static) >= +1.5 percentage points.
   - Max drawdown deterioration (AI - static) <= +2.0 percentage points.
3. Statistical confirmation (bootstrap CI):
   - Use block bootstrap on daily return delta with >= 2,000 resamples.
   - 90% confidence interval lower bound for return delta must be > 0.
4. AI failure mode verified:
   - Timeout and API failure degrade to cached or quantitative regime.
   - No main-loop failure from AI errors.

Fail condition:

- If practical thresholds or bootstrap confirmation fail, AI weighting is disabled and static allocation becomes canonical.

Note:

- 8-week ablation can be used for provisional gate decisions.
- Confidence remains provisional until longer samples are accumulated.

## Phase 4 Gate (Live Readiness)

Required evidence:

1. Pre-live ops gate:
   - Incident runbook completed.
   - Emergency close-all procedure tested.
   - Monitoring and alerting verified.
2. Backtest-to-paper parity gate:
   - Investigated divergence report completed.
   - No unexplained critical behavior divergence.
3. Production stability gate:
   - Deployment stable for >= 2 weeks.
4. Capital ramp gate:
   - Live week 1: one 1-contract iron condor only.
   - Live week 2-4: staged position scaling only if no critical incidents.

Fail condition:

- Any critical production failure pauses live scaling until root cause is fixed and validated.

## Waiver Policy

- Waivers are allowed only for schedule pressure, never for unresolved safety defects.
- Every waiver must include:
  - exact gate item waived,
  - rationale,
  - risk impact,
  - compensating controls,
  - expiry date.
- Waivers are recorded as ADR entries in `DECISIONS.md`.

## Gate Acceptance Procedure

This procedure defines how a gate moves from `in_progress` to `passed` in the scoreboard.

## Phase 1 Acceptance Procedure

### Step 1: Collect backtest report

The Phase 1 LEAN backtest must produce a JSON file at:

```text
backtests/lean/results/phase1_baseline.json
```

Required fields:

| Field | Type | Description |
|---|---|---|
| `cagr_net` | float | Annualized net return after commissions/slippage |
| `max_drawdown` | float | Positive drawdown magnitude, 0.0 to 1.0 |
| `sharpe` | float | Annualized Sharpe ratio |
| `win_rate` | float | Fraction of trades closed at profit |
| `profit_factor` | float | Gross profit / gross loss |
| `in_sample_cagr` | float | In-sample period CAGR |
| `oos_cagr` | float | Out-of-sample period CAGR |
| `slippage_drag_pct` | float | Slippage cost as % of gross strategy profit |
| `date_range_start` | str | ISO date string, e.g. "2019-01-01" |
| `date_range_end` | str | ISO date string, e.g. "2025-12-31" |
| `parameter_hash` | str | SHA256 of generated LEAN config |

### Step 2: Validate against gate criteria (Phase 1)

All criteria must pass:

- [ ] `cagr_net > 0.0`
- [ ] `0.0 <= max_drawdown <= 1.0`
- [ ] `max_drawdown <= 0.25`
- [ ] `abs(in_sample_cagr - oos_cagr) <= 0.03`
- [ ] `slippage_drag_pct <= 0.30`
- [ ] `parameter_hash` matches hash from re-running config translator against `config/strategies.yaml`

### Step 3: Record gate passage in DECISIONS.md

After all checks pass, add ADR entry:

```text
## ADR-XXX: Phase 1 Strategy Viability Gate - Passed

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

### Step 4: Update PROGRESS.md scoreboard

```text
| Phase 1 Strategy Viability | passed | ADR-XXX, CAGR X.X%, DD X.X% |
```

Set Phase 2 gate status to `in_progress`.

## Phase 2 Acceptance Procedure

### Step 1: Collect paper-ops report

Required JSON artifact:

```text
reports/gates/phase2_paper_ops.json
```

Required fields:

| Field | Type | Description |
|---|---|---|
| `window_start` | str | ISO date |
| `window_end` | str | ISO date |
| `trading_days` | int | Number of paper trading days in evaluation window |
| `hard_limit_violations` | int | Count of hard risk limit violations |
| `open_p0_defects` | int | Count of unresolved P0 defects |
| `risk_telemetry_complete` | bool | Daily/weekly/monthly and delta/margin telemetry complete |
| `notes` | str | Optional summary |

### Step 2: Validate against gate criteria (Phase 2)

All criteria must pass:

- [ ] `trading_days >= 20` (approximately 4 weeks)
- [ ] `hard_limit_violations == 0`
- [ ] `open_p0_defects == 0`
- [ ] `risk_telemetry_complete == true`

### Step 3: Record gate passage in DECISIONS.md

```text
## ADR-XXX: Phase 2 Execution and Risk Gate - Passed

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

### Step 4: Update PROGRESS.md scoreboard

```text
| Phase 2 Execution and Risk | passed | ADR-XXX, 0 hard violations, 0 P0 |
```

Set Phase 3 gate status to `in_progress`.

## Phase 3 Acceptance Procedure

### Step 1: Collect AI ablation report

Required JSON artifact:

```text
reports/gates/phase3_ai_ablation.json
```

Required fields:

| Field | Type | Description |
|---|---|---|
| `window_start` | str | ISO date |
| `window_end` | str | ISO date |
| `trading_days` | int | Number of days in ablation window |
| `return_delta_annualized` | float | AI minus static annualized net return |
| `max_dd_delta` | float | AI minus static max drawdown (positive means worse) |
| `bootstrap_resamples` | int | Number of block bootstrap resamples |
| `return_delta_ci90_low` | float | Lower bound of 90% CI for return delta |
| `return_delta_ci90_high` | float | Upper bound of 90% CI for return delta |
| `ai_fallback_test_passed` | bool | Fallback and timeout behavior verified |

### Step 2: Validate against gate criteria (Phase 3)

All criteria must pass:

- [ ] `trading_days >= 40`
- [ ] `return_delta_annualized >= 0.015`
- [ ] `max_dd_delta <= 0.02`
- [ ] `bootstrap_resamples >= 2000`
- [ ] `return_delta_ci90_low > 0.0`
- [ ] `ai_fallback_test_passed == true`

### Step 3: Record gate passage in DECISIONS.md

```text
## ADR-XXX: Phase 3 AI Value and Safety Gate - Passed

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

### Step 4: Update PROGRESS.md scoreboard

```text
| Phase 3 AI Value | passed | ADR-XXX, +X.Xpp return delta, CI low > 0 |
```

Set Phase 4 gate status to `in_progress`.

## Phase 4 Acceptance Procedure

### Step 1: Collect live-readiness report

Required JSON artifact:

```text
reports/gates/phase4_live_readiness.json
```

Required fields:

| Field | Type | Description |
|---|---|---|
| `runbook_complete` | bool | Incident runbook completed |
| `emergency_close_tested` | bool | Emergency close-all tested |
| `monitoring_verified` | bool | Alerting/monitoring validated |
| `parity_report_complete` | bool | Backtest-paper divergence report complete |
| `critical_parity_unknowns` | int | Count of unresolved critical parity issues |
| `prod_stability_days` | int | Continuous production stability days |
| `week1_live_constraints_passed` | bool | One 1-contract trade only and no critical incidents |
| `week2_4_staged_scaling_passed` | bool | Scaling followed staged policy and no critical incidents |

### Step 2: Validate against gate criteria (Phase 4)

All criteria must pass:

- [ ] `runbook_complete == true`
- [ ] `emergency_close_tested == true`
- [ ] `monitoring_verified == true`
- [ ] `parity_report_complete == true`
- [ ] `critical_parity_unknowns == 0`
- [ ] `prod_stability_days >= 14`
- [ ] `week1_live_constraints_passed == true`
- [ ] `week2_4_staged_scaling_passed == true`

### Step 3: Record gate passage in DECISIONS.md

```text
## ADR-XXX: Phase 4 Live Readiness Gate - Passed

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

### Step 4: Update PROGRESS.md scoreboard

```text
| Phase 4 Live Readiness | passed | ADR-XXX, staged go-live complete |
```

## Waiver Path (All Phases)

If any check fails, do not update scoreboard to `passed`.
Record waiver ADR in `DECISIONS.md` with:

- exact criterion that failed,
- actual vs threshold value,
- rationale and risk impact,
- compensating controls,
- expiry date.

See Waiver Policy section above.

---

## Gate Scoreboard Source

- `PROGRESS.md` contains the current gate scoreboard.
- This file defines gate criteria and acceptance procedures.

## Operator Shortcuts

- Runbook: `docs/GATE_OPERATIONS.md`
- ADR templates: `docs/templates/GATE_ADR_TEMPLATES.md`
- Scoreboard templates: `docs/templates/GATE_SCOREBOARD_TEMPLATES.md`
- JSON report skeletons: `reports/gates/templates/`
