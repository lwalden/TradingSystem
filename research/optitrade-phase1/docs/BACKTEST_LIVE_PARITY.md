# Backtest-Live Parity Specification

Last updated: 2026-02-20
Status: Canonical parity controls

## Purpose

Define controls that keep LEAN backtests and Python runtime behavior aligned.
Without this, backtest confidence is not transferable to paper/live.

## Parity Surface

The following must match by design:

1. Strategy parameters:
   - Single source of truth: `config/strategies.yaml`.
   - Generated artifacts for LEAN must be reproducible.
2. Entry/exit rules:
   - Delta selection, DTE windows, IV filters, profit/stop logic.
3. Position sizing:
   - 2.5% max risk-per-trade logic and contract rounding.
4. Execution assumptions:
   - Slippage and commission modeling assumptions documented and versioned.
5. Risk controls:
   - Hard limits from code constants and equivalent simulation assumptions.

## Known Unavoidable Differences

1. Backtest cannot fully replicate live microstructure and queue position.
2. Backtest may not replicate real-time circuit-breaker operational behavior.
3. Paper fills differ from live fills.

These differences must be explicitly tracked, not ignored.

## Required Artifacts Per Parity Review

1. Parameter parity report:
   - YAML values and generated LEAN constants hash.
2. Logic parity checklist:
   - Entry, exit, adjustment rule mapping table.
3. Cost parity report:
   - Backtest slippage assumption vs observed paper/live slippage.
4. Divergence summary:
   - Return, drawdown, and fill-quality deltas with root-cause hypotheses.

## Divergence Thresholds

If any threshold is breached, open a parity investigation before live scaling:

1. Net CAGR divergence > 2 percentage points over compared window.
2. Max drawdown divergence > 3 percentage points.
3. Average slippage divergence > 25% vs modeled baseline.
4. Rule behavior mismatch confirmed in even one safety-critical scenario.

## Investigation Workflow

1. Classify divergence:
   - model assumption,
   - execution microstructure,
   - code behavior mismatch,
   - data quality issue.
2. Propose corrective action with expected impact.
3. Re-run parity comparison.
4. Record decision in `DECISIONS.md`.

## Go-Live Rule

- Live scaling is blocked unless parity investigation is complete and no unresolved critical divergence remains.

