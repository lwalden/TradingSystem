# Backtest Pipeline Tools

Automated QuantConnect cloud backtest pipeline. Migrated from OptiTrade (archived).

## Setup

```
pip install -r tools/backtest/requirements.txt
```

Requires Bitwarden CLI with `BW_SESSION` set and a "QuantConnect API" login item.

## Usage

```
python tools/backtest/run_cloud_backtest.py --name "my-run"
python tools/backtest/evaluate_phase1_gate.py
```

## Files

| File | Purpose |
|------|---------|
| `run_cloud_backtest.py` | End-to-end: push algorithm to QC, compile, run, download results, evaluate gate |
| `evaluate_phase1_gate.py` | Validate results/phase1_baseline.json against gate criteria |
| `generate_lean_config.py` | Generate C# constants from config/strategies.yaml |
| `parse_lean_results.py` | Parse QC log into phase1_baseline.json |
| `algorithms/IronCondorBaseline.cs` | Gate-passing SPY iron condor (CAGR +0.07%, PF 2.0, 72% WR) |
| `config/strategies.yaml` | Canonical strategy parameters |
| `config/config.json` | QC cloud project ID |
| `results/` | Backtest output (JSON results, logs, baseline) |

## Iron Condor Findings (8 runs, 2026-04-03 to 2026-04-07)

SPY iron condors barely break even (+0.07% CAGR on $400K over 7 years).
Next priority: SPX iron condors (10x contract multiplier eliminates commission drag).
Full iteration log: docs/archive/iron-condor-backtest-log.md
