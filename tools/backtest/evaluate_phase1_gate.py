"""
evaluate_phase1_gate.py — Sprint 1.0 [S1C-002]

Validates backtests/lean/results/phase1_baseline.json against all
Phase 1 gate criteria defined in docs/VALIDATION_GATES.md.

Prints a per-criterion pass/fail table and exits 0 on overall PASS,
1 on overall FAIL, 2 on input error.

Usage:
    uv run python scripts/evaluate_phase1_gate.py
    uv run python scripts/evaluate_phase1_gate.py --report path/to/report.json
    uv run python scripts/evaluate_phase1_gate.py --json   # machine-readable output
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parent
DEFAULT_REPORT = TOOL_DIR / "results" / "phase1_baseline.json"
GENERATE_SCRIPT = TOOL_DIR / "generate_lean_config.py"

REQUIRED_FIELDS = [
    "cagr_net",
    "max_drawdown",
    "sharpe",
    "win_rate",
    "profit_factor",
    "in_sample_cagr",
    "oos_cagr",
    "slippage_drag_pct",
    "date_range_start",
    "date_range_end",
    "parameter_hash",
]


@dataclass
class CheckResult:
    name: str
    passed: bool
    actual: str
    threshold: str
    note: str = ""


def load_report(path: Path) -> dict:
    if not path.exists():
        print(f"ERROR: Report not found: {path}", file=sys.stderr)
        sys.exit(2)
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def validate_fields(report: dict) -> list[str]:
    missing = [f for f in REQUIRED_FIELDS if f not in report]
    return missing


def get_expected_hash() -> str | None:
    """Re-run generate_lean_config.py --hash-only and return hash.

    Uses `uv run python` so the subprocess inherits the project virtualenv
    and can import yaml, regardless of how this script was invoked.
    Falls back to sys.executable if uv is not on PATH.
    """
    for cmd in ([sys.executable, str(GENERATE_SCRIPT), "--hash-only"],):
        result = subprocess.run(cmd, capture_output=True, text=True)
        if result.returncode == 0:
            return result.stdout.strip()
    return None


def evaluate(report: dict) -> list[CheckResult]:
    results: list[CheckResult] = []

    # 1. Net CAGR > 0
    cagr = report["cagr_net"]
    results.append(CheckResult(
        name="Net CAGR > 0%",
        passed=cagr > 0.0,
        actual=f"{cagr:.2%}",
        threshold="> 0%",
    ))

    # 2. max_drawdown in [0, 1] (sign check)
    dd = report["max_drawdown"]
    dd_valid = 0.0 <= dd <= 1.0
    results.append(CheckResult(
        name="Drawdown magnitude valid [0, 1]",
        passed=dd_valid,
        actual=str(dd),
        threshold="0.0 to 1.0",
        note="Negative drawdown values are invalid per VALIDATION_GATES.md" if not dd_valid else "",
    ))

    # 3. max_drawdown <= 25%
    results.append(CheckResult(
        name="Max drawdown <= 25%",
        passed=dd <= 0.25,
        actual=f"{dd:.2%}",
        threshold="<= 25%",
    ))

    # 4. IS/OOS CAGR delta <= 3 pp
    is_cagr = report["in_sample_cagr"]
    oos_cagr = report["oos_cagr"]
    delta = abs(is_cagr - oos_cagr)
    results.append(CheckResult(
        name="IS/OOS CAGR delta <= 3 pp",
        passed=delta <= 0.03,
        actual=f"{delta:.2%}",
        threshold="<= 3 pp",
        note=f"IS={is_cagr:.2%} OOS={oos_cagr:.2%}",
    ))

    # 5. Slippage drag <= 30%
    slip = report["slippage_drag_pct"]
    results.append(CheckResult(
        name="Slippage drag <= 30%",
        passed=slip <= 0.30,
        actual=f"{slip:.2%}",
        threshold="<= 30%",
    ))

    # 6. parameter_hash matches config
    report_hash = report["parameter_hash"]
    expected_hash = get_expected_hash()
    if expected_hash is None:
        results.append(CheckResult(
            name="parameter_hash matches config",
            passed=False,
            actual=report_hash,
            threshold="SHA256 from generate_lean_config.py",
            note="ERROR: Could not run generate_lean_config.py to get expected hash",
        ))
    else:
        hash_match = report_hash == expected_hash
        results.append(CheckResult(
            name="parameter_hash matches config",
            passed=hash_match,
            actual=report_hash[:16] + "...",
            threshold=expected_hash[:16] + "...",
            note="" if hash_match else f"Expected: {expected_hash}",
        ))

    return results


def print_table(results: list[CheckResult], report: dict) -> None:
    col_name = max(len(r.name) for r in results)
    col_actual = max(len(r.actual) for r in results)
    col_thresh = max(len(r.threshold) for r in results)

    header = f"{'Criterion':<{col_name}}  {'Actual':<{col_actual}}  {'Threshold':<{col_thresh}}  Pass"
    print(header)
    print("-" * len(header))
    for r in results:
        status = "PASS" if r.passed else "FAIL"
        row = f"{r.name:<{col_name}}  {r.actual:<{col_actual}}  {r.threshold:<{col_thresh}}  {status}"
        print(row)
        if r.note:
            print(f"  NOTE: {r.note}")

    print()
    # Additional metrics from report (informational)
    for field in ("sharpe", "win_rate", "profit_factor"):
        if field in report:
            print(f"  {field}: {report[field]}")
    print(f"  date_range: {report.get('date_range_start')} to {report.get('date_range_end')}")
    print()

    overall = all(r.passed for r in results)
    marker = "PASS" if overall else "FAIL"
    print(f"Overall: {marker}")


def print_json(results: list[CheckResult], report: dict) -> None:
    output = {
        "overall": "PASS" if all(r.passed for r in results) else "FAIL",
        "checks": [
            {
                "name": r.name,
                "passed": r.passed,
                "actual": r.actual,
                "threshold": r.threshold,
                "note": r.note,
            }
            for r in results
        ],
        "report_fields": {k: report.get(k) for k in REQUIRED_FIELDS},
    }
    print(json.dumps(output, indent=2))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Evaluate Phase 1 gate criteria against backtest report")
    parser.add_argument("--report", default=str(DEFAULT_REPORT), help="Path to phase1_baseline.json")
    parser.add_argument("--json", action="store_true", dest="json_output", help="Machine-readable JSON output")
    args = parser.parse_args(argv)

    report_path = Path(args.report)
    report = load_report(report_path)

    missing = validate_fields(report)
    if missing:
        print(f"ERROR: Report is missing required fields: {', '.join(missing)}", file=sys.stderr)
        return 2

    results = evaluate(report)

    if args.json_output:
        print_json(results, report)
    else:
        print_table(results, report)

    return 0 if all(r.passed for r in results) else 1


if __name__ == "__main__":
    sys.exit(main())
