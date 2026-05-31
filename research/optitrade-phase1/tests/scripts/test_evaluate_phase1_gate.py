"""
tests/scripts/test_evaluate_phase1_gate.py — S1C-004

Tests for scripts/evaluate_phase1_gate.py:
- Pass fixture: all criteria met → overall PASS, exit 0
- Fail fixtures: one criterion failing at a time → overall FAIL, exit 1
- Missing field → exit 2
- JSON output mode
"""

from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path
from unittest.mock import patch

import pytest

SCRIPTS_DIR = Path(__file__).resolve().parents[2] / "scripts"

# Use the real hash from the live strategies.yaml so parameter_hash check passes
_REAL_HASH_CACHE: str | None = None


def _real_hash() -> str:
    global _REAL_HASH_CACHE
    if _REAL_HASH_CACHE is None:
        spec = importlib.util.spec_from_file_location(
            "generate_lean_config", SCRIPTS_DIR / "generate_lean_config.py"
        )
        mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(mod)
        import yaml
        data = yaml.safe_load((Path(__file__).resolve().parents[2] / "optimind" / "config" / "strategies.yaml").read_text())
        _REAL_HASH_CACHE = mod.compute_hash(data["iron_condor"])
    return _REAL_HASH_CACHE


def load_script(name: str):
    spec = importlib.util.spec_from_file_location(name, SCRIPTS_DIR / f"{name}.py")
    mod = importlib.util.module_from_spec(spec)
    sys.modules[name] = mod  # register before exec so dataclass __module__ lookup works
    spec.loader.exec_module(mod)
    return mod


@pytest.fixture(scope="module")
def ev():
    return load_script("evaluate_phase1_gate")


def passing_report(parameter_hash: str | None = None) -> dict:
    """A report that satisfies all 6 Phase 1 gate criteria."""
    return {
        "cagr_net": 0.10,
        "max_drawdown": 0.15,
        "sharpe": 0.9,
        "win_rate": 0.65,
        "profit_factor": 1.4,
        "in_sample_cagr": 0.11,
        "oos_cagr": 0.09,          # delta = 0.02 <= 0.03
        "slippage_drag_pct": 0.20,  # <= 0.30
        "date_range_start": "2019-01-01",
        "date_range_end": "2025-12-31",
        "parameter_hash": parameter_hash or _real_hash(),
    }


class TestPassFixture:
    def test_all_pass(self, ev):
        report = passing_report()
        results = ev.evaluate(report)
        assert all(r.passed for r in results), [r for r in results if not r.passed]

    def test_main_exits_0(self, ev, tmp_path):
        report = passing_report()
        p = tmp_path / "phase1_baseline.json"
        p.write_text(json.dumps(report), encoding="utf-8")
        rc = ev.main(["--report", str(p)])
        assert rc == 0


class TestFailFixtures:
    def test_negative_cagr_fails(self, ev):
        report = passing_report()
        report["cagr_net"] = -0.01
        results = ev.evaluate(report)
        names = {r.name: r for r in results}
        assert not names["Net CAGR > 0%"].passed

    def test_drawdown_too_large_fails(self, ev):
        report = passing_report()
        report["max_drawdown"] = 0.30
        results = ev.evaluate(report)
        names = {r.name: r for r in results}
        assert not names["Max drawdown <= 25%"].passed

    def test_negative_drawdown_fails_sign_check(self, ev):
        report = passing_report()
        report["max_drawdown"] = -0.15
        results = ev.evaluate(report)
        names = {r.name: r for r in results}
        assert not names["Drawdown magnitude valid [0, 1]"].passed

    def test_is_oos_delta_too_large_fails(self, ev):
        report = passing_report()
        report["in_sample_cagr"] = 0.15
        report["oos_cagr"] = 0.05   # delta = 0.10 > 0.03
        results = ev.evaluate(report)
        names = {r.name: r for r in results}
        assert not names["IS/OOS CAGR delta <= 3 pp"].passed

    def test_slippage_too_high_fails(self, ev):
        report = passing_report()
        report["slippage_drag_pct"] = 0.35
        results = ev.evaluate(report)
        names = {r.name: r for r in results}
        assert not names["Slippage drag <= 30%"].passed

    def test_wrong_parameter_hash_fails(self, ev):
        report = passing_report()
        report["parameter_hash"] = "0" * 64
        results = ev.evaluate(report)
        names = {r.name: r for r in results}
        assert not names["parameter_hash matches config"].passed

    def test_any_fail_causes_exit_1(self, ev, tmp_path):
        report = passing_report()
        report["cagr_net"] = -0.01
        p = tmp_path / "phase1_baseline.json"
        p.write_text(json.dumps(report), encoding="utf-8")
        rc = ev.main(["--report", str(p)])
        assert rc == 1


class TestMissingFields:
    def test_missing_field_returns_exit_2(self, ev, tmp_path):
        report = passing_report()
        del report["cagr_net"]
        p = tmp_path / "phase1_baseline.json"
        p.write_text(json.dumps(report), encoding="utf-8")
        rc = ev.main(["--report", str(p)])
        assert rc == 2

    def test_missing_report_file_exits_with_sysexit(self, ev, tmp_path):
        with pytest.raises(SystemExit) as exc:
            ev.main(["--report", str(tmp_path / "nonexistent.json")])
        assert exc.value.code == 2


class TestJsonOutput:
    def test_json_mode_outputs_valid_json(self, ev, tmp_path, capsys):
        report = passing_report()
        p = tmp_path / "phase1_baseline.json"
        p.write_text(json.dumps(report), encoding="utf-8")
        rc = ev.main(["--report", str(p), "--json"])
        captured = capsys.readouterr()
        parsed = json.loads(captured.out)
        assert parsed["overall"] in ("PASS", "FAIL")
        assert "checks" in parsed
        assert len(parsed["checks"]) == 6

    def test_json_mode_pass_overall(self, ev, tmp_path, capsys):
        report = passing_report()
        p = tmp_path / "phase1_baseline.json"
        p.write_text(json.dumps(report), encoding="utf-8")
        ev.main(["--report", str(p), "--json"])
        captured = capsys.readouterr()
        parsed = json.loads(captured.out)
        assert parsed["overall"] == "PASS"

    def test_json_mode_fail_overall(self, ev, tmp_path, capsys):
        report = passing_report()
        report["cagr_net"] = -0.05
        p = tmp_path / "phase1_baseline.json"
        p.write_text(json.dumps(report), encoding="utf-8")
        ev.main(["--report", str(p), "--json"])
        captured = capsys.readouterr()
        parsed = json.loads(captured.out)
        assert parsed["overall"] == "FAIL"
