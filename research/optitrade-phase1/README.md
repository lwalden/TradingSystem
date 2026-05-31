# research/optitrade-phase1

Backtest research migrated from OptiTrade during repo consolidation, April 2026.

## What This Is

OptiTrade was a standalone Python options backtest system (repo: `lwalden/OptiTrade`,
archived as `v1.0-archive`). It was consolidated into TradingSystem per ADR-026 because
TradingSystem is the primary repo for all trading work. OptiTrade's only unique asset was
its SPY iron condor backtest pipeline. This directory preserves that work.

## What Happened (Phase 1 Summary)

- 8 backtest runs (2019-2025 SPY data) via QuantConnect cloud REST API
- Started from a negative-CAGR baseline, iterated to a Phase 1 gate pass on Run 7
- **Gate-passing result (Run 7):** CAGR +0.07%, Profit Factor 2.0, Win Rate 72.22%, Max Drawdown 1.07%, IS/OOS delta 0.42pp
- Key finding: SPY commission drag ($2.60/spread) consumes nearly all edge; SPX (100x multiplier) should solve this
- Full iteration narrative: `docs/backtest-iteration-log.md`

## Gate-Passing Parameters (locked in ADR-028)

| Parameter | Value |
|---|---|
| entry_min_atm_iv | 0.18 (18% annualized) |
| profit_target_pct | 0.70 (70% of max credit) |
| min_credit_to_width_ratio | 0.20 ($2.00 min on $10 wings) |
| stop_loss_credit_multiple | 2.0 (2x credit stop) |
| wing_width_spy | 10 (points) |
| short_delta_target | 0.16 (~1 SD) |
| parameter_hash | f32c6bb59a1432156890dca414c7bafc5fcd637cc6d8cd04a37351c500ebccbd |

## Directory Structure

```
research/optitrade-phase1/
  README.md                         — this file
  pyproject.toml                    — Python deps for backtest scripts (uv managed)
  results/
    phase1_baseline.json            — gate-passing backtest metrics (Run 7)
  backtests/
    QC_CloudBacktest.cs             — QC web IDE single-file algorithm (paste to quantconnect.com)
    lean/
      Config/StrategyConstants.cs   — auto-generated C# constants from strategies.yaml
      config.json                   — QC cloud project ID
  optimind/
    config/
      strategies.yaml               — canonical iron condor parameters (gate-passing version)
      risk_limits.yaml              — soft risk parameter overrides
      sectors.yaml                  — sector correlation mapping
      watchlist.yaml                — tradeable underlyings with symbol-specific overrides
      settings.py                   — pydantic-settings config class
    core/
      constants.py                  — hard-coded risk limits (require PR to change)
      models.py                     — pydantic data contracts
    broker/ibkr/
      connection.py                 — async IB Gateway connection manager
  scripts/
    generate_lean_config.py         — strategies.yaml -> StrategyConstants.cs translator
    evaluate_phase1_gate.py         — validates phase1_baseline.json against gate criteria
    parse_lean_results.py           — LEAN log -> phase1_baseline.json parser
    run_cloud_backtest.py           — end-to-end QC cloud backtest pipeline
    smoke_test_connection.py        — IB Gateway connectivity smoke test
  docs/
    backtest-iteration-log.md       — chronological 8-run log with findings
    ARCHITECTURE.md
    BACKTEST_LIVE_PARITY.md
    COST_MODEL.md
    GATE_OPERATIONS.md
    PERFORMANCE_MODEL.md
    PHASE_1_FOUNDATION.md
    PHASE_2_STRATEGIES.md
    PHASE_3_AI_LAYER.md
    PHASE_4_PRODUCTION.md
    PROJECT_STRATEGY.md
    RISK_FRAMEWORK.md
    VALIDATION_GATES.md
    strategy-roadmap.md
    templates/
      GATE_ADR_TEMPLATES.md
      GATE_SCOREBOARD_TEMPLATES.md
  tests/
    conftest.py
    broker/test_connection.py
    config/test_settings.py
    scripts/test_evaluate_phase1_gate.py
    scripts/test_generate_lean_config.py
```

## Current Status (as of consolidation)

- Phase 1 gate: PASSED (Run 7, 2026-04-07)
- Phase 2+: NOT APPLICABLE — this work is now superseded by ADR-030 (paper trading is
  the validation gate for options strategies, not backtesting)
- SPX variant: backtesting demoted to research aid per ADR-030; iron condor parameters
  from ADR-028 remain valid reference parameters for the options sleeve

## How to Use the QC Pipeline

1. Set `QC_USER_ID` and `QC_API_TOKEN` env vars (or store in Bitwarden as "QuantConnect API")
2. Edit `optimind/config/strategies.yaml` to test a parameter set
3. Run: `uv run python scripts/run_cloud_backtest.py`
   - Regenerates `StrategyConstants.cs` from YAML
   - Pushes algorithm to QC cloud, compiles, runs backtest (~60 min)
   - Downloads log, parses to `results/phase1_baseline.json`
   - Runs gate evaluator

Or for a quick one-off via QC web IDE:
1. Open `backtests/QC_CloudBacktest.cs`
2. Paste into a new C# algorithm on quantconnect.com
3. Run, download logs, run `parse_lean_results.py` then `evaluate_phase1_gate.py`
