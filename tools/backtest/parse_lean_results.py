"""
parse_lean_results.py

Parses LEAN backtest output and extracts metrics logged by IronCondorAlgorithm
from the algorithm log lines, producing backtests/lean/results/phase1_baseline.json
in the format required by evaluate_phase1_gate.py.

LEAN logs the summary block with lines like:
  IronCondorAlgorithm: cagr_net: 9.50%
  IronCondorAlgorithm: max_drawdown: 12.30%
  ...

Usage:
    uv run python scripts/parse_lean_results.py
    uv run python scripts/parse_lean_results.py --log path/to/log.txt --out path/to/output.json
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

TOOL_DIR    = Path(__file__).resolve().parent
RESULTS_DIR = TOOL_DIR / "results"
DEFAULT_OUT = RESULTS_DIR / "phase1_baseline.json"

# Map log field names -> JSON field names and types
FIELD_MAP = {
    "parameter_hash":    ("parameter_hash",   str),
    "date_range_start":  ("date_range_start",  str),
    "date_range_end":    ("date_range_end",    str),
    "cagr_net":          ("cagr_net",          float),
    "in_sample_cagr":    ("in_sample_cagr",    float),
    "oos_cagr":          ("oos_cagr",          float),
    "max_drawdown":      ("max_drawdown",      float),
    "win_rate":          ("win_rate",          float),
    "profit_factor":     ("profit_factor",     float),
    "slippage_drag_pct": ("slippage_drag_pct", float),
    "sharpe":            ("sharpe",            float),
}

# Pattern for percentage values logged as "9.50%"
PCT_RE = re.compile(r"^(-?\d+\.?\d*)%$")


def parse_value(raw: str, typ: type):
    raw = raw.strip()
    if typ is float:
        m = PCT_RE.match(raw)
        if m:
            return float(m.group(1)) / 100.0
        return float(raw)
    return raw


def find_lean_log(results_dir: Path) -> Path | None:
    """Find the most recent LEAN log file in results directory."""
    # LEAN CLI writes logs to results/<timestamp>/log.txt or similar
    candidates = sorted(results_dir.rglob("*.txt"), key=lambda p: p.stat().st_mtime, reverse=True)
    if candidates:
        return candidates[0]
    # Also check for direct log.txt
    direct = results_dir / "log.txt"
    if direct.exists():
        return direct
    return None


def parse_log(log_path: Path) -> dict:
    """Extract summary metrics from algorithm log lines."""
    extracted = {}

    with log_path.open(encoding="utf-8", errors="replace") as f:
        for line in f:
            # LEAN log format: "YYYY-MM-DD HH:MM:SS ... AlgorithmName: key: value"
            # or just "key:     value" after the prefix
            for log_key, (json_key, typ) in FIELD_MAP.items():
                # Match lines containing "key:  value" anywhere (handles LEAN prefix)
                pattern = rf"{re.escape(log_key)}:\s+(.+)"
                m = re.search(pattern, line)
                if m and json_key not in extracted:
                    try:
                        extracted[json_key] = parse_value(m.group(1), typ)
                    except ValueError:
                        pass  # skip malformed lines

    return extracted


def try_parse_lean_json(results_dir: Path) -> dict:
    """Try to extract Sharpe from LEAN's native JSON result file if present."""
    extras = {}
    for candidate in results_dir.rglob("*.json"):
        if candidate.name == "phase1_baseline.json":
            continue
        try:
            with candidate.open(encoding="utf-8") as f:
                data = json.load(f)
            # LEAN result JSON has Statistics -> Sharpe Ratio
            stats = data.get("Statistics", {}) or data.get("statistics", {})
            sharpe_raw = stats.get("Sharpe Ratio") or stats.get("sharpe_ratio")
            if sharpe_raw is not None:
                extras["sharpe"] = float(sharpe_raw)
                break
        except Exception:
            pass
    return extras


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Parse LEAN backtest output into phase1_baseline.json")
    parser.add_argument("--log", default=None, help="Path to LEAN log file (auto-detected if omitted)")
    parser.add_argument("--out", default=str(DEFAULT_OUT), help="Output path for phase1_baseline.json")
    args = parser.parse_args(argv)

    out_path = Path(args.out)

    if args.log:
        log_path = Path(args.log)
    else:
        log_path = find_lean_log(RESULTS_DIR)

    if log_path is None or not log_path.exists():
        print(f"ERROR: No LEAN log file found in {RESULTS_DIR}", file=sys.stderr)
        print("Run the backtest first: lean backtest backtests/lean", file=sys.stderr)
        return 2

    print(f"Parsing log: {log_path}")
    metrics = parse_log(log_path)

    # Try to supplement with Sharpe from LEAN JSON
    json_extras = try_parse_lean_json(RESULTS_DIR)
    for k, v in json_extras.items():
        if k not in metrics:
            metrics[k] = v

    # Check for missing required fields
    required = list(FIELD_MAP.values())
    missing = [json_key for json_key, _ in required if json_key not in metrics]
    if missing:
        print(f"WARNING: Could not extract fields: {', '.join(missing)}", file=sys.stderr)
        print("These will need to be filled in manually in the output JSON.", file=sys.stderr)
        for f in missing:
            metrics[f] = None

    # Write output
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with out_path.open("w", encoding="utf-8") as f:
        json.dump(metrics, f, indent=2)
        f.write("\n")

    print(f"Written: {out_path}")
    for k, v in metrics.items():
        print(f"  {k}: {v}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
