"""
tests/scripts/test_generate_lean_config.py — S1C-004

Tests for scripts/generate_lean_config.py:
- YAML → C# output correctness
- SHA256 hash stability (same input → same hash)
- Key constants appear in generated output
- hash-only mode
- dry-run mode (no file written)
"""

from __future__ import annotations

import hashlib
import json
import textwrap
from pathlib import Path

import pytest
import yaml

# Import the script as a module
import importlib.util
import sys

SCRIPTS_DIR = Path(__file__).resolve().parents[2] / "scripts"


def load_script(name: str):
    spec = importlib.util.spec_from_file_location(name, SCRIPTS_DIR / f"{name}.py")
    mod = importlib.util.module_from_spec(spec)
    sys.modules[name] = mod
    spec.loader.exec_module(mod)
    return mod


@pytest.fixture(scope="module")
def gen():
    return load_script("generate_lean_config")


@pytest.fixture
def minimal_yaml(tmp_path: Path) -> Path:
    """Minimal strategies.yaml with enough fields to generate without error."""
    data = {
        "iron_condor": {
            "entry": {
                "dte_min": 30,
                "dte_target": 45,
                "dte_max": 60,
                "short_delta_target": 0.16,
            },
            "exit": {
                "profit_target_pct": 0.50,
                "stop_loss_credit_multiple": 2.0,
            },
            "backtest": {
                "date_range_start": "2019-01-01",
                "date_range_end": "2025-12-31",
                "initial_capital_usd": 400000,
                "slippage_per_leg_usd": 0.05,
                "commission_per_contract_usd": 0.65,
            },
        }
    }
    p = tmp_path / "strategies.yaml"
    p.write_text(yaml.dump(data), encoding="utf-8")
    return p


class TestComputeHash:
    def test_hash_is_hex_string(self, gen, minimal_yaml):
        data = yaml.safe_load(minimal_yaml.read_text())
        h = gen.compute_hash(data["iron_condor"])
        assert len(h) == 64
        int(h, 16)  # raises ValueError if not valid hex

    def test_hash_is_stable(self, gen, minimal_yaml):
        """Same YAML content must produce the same hash on repeated calls."""
        data = yaml.safe_load(minimal_yaml.read_text())
        h1 = gen.compute_hash(data["iron_condor"])
        h2 = gen.compute_hash(data["iron_condor"])
        assert h1 == h2

    def test_hash_changes_on_value_change(self, gen, minimal_yaml):
        data = yaml.safe_load(minimal_yaml.read_text())
        h1 = gen.compute_hash(data["iron_condor"])
        data["iron_condor"]["entry"]["dte_min"] = 25  # change a value
        h2 = gen.compute_hash(data["iron_condor"])
        assert h1 != h2

    def test_hash_deterministic_across_key_order(self, gen):
        """JSON sort_keys ensures insertion order doesn't affect hash."""
        d1 = {"a": 1, "b": 2}
        d2 = {"b": 2, "a": 1}
        assert gen.compute_hash(d1) == gen.compute_hash(d2)


class TestRenderCs:
    def test_namespace_and_class_present(self, gen, minimal_yaml):
        data = yaml.safe_load(minimal_yaml.read_text())
        ic = data["iron_condor"]
        h = gen.compute_hash(ic)
        cs = gen.render_cs(ic, h)
        assert "namespace OptiMind.Backtests" in cs
        assert "public static class StrategyConstants" in cs

    def test_parameter_hash_embedded(self, gen, minimal_yaml):
        data = yaml.safe_load(minimal_yaml.read_text())
        ic = data["iron_condor"]
        h = gen.compute_hash(ic)
        cs = gen.render_cs(ic, h)
        assert f'public const string ParameterHash = "{h}"' in cs

    def test_int_constant_rendered(self, gen, minimal_yaml):
        data = yaml.safe_load(minimal_yaml.read_text())
        ic = data["iron_condor"]
        h = gen.compute_hash(ic)
        cs = gen.render_cs(ic, h)
        assert "public const int EntryDteMin = 30;" in cs
        assert "public const int EntryDteTarget = 45;" in cs

    def test_double_constant_rendered(self, gen, minimal_yaml):
        data = yaml.safe_load(minimal_yaml.read_text())
        ic = data["iron_condor"]
        h = gen.compute_hash(ic)
        cs = gen.render_cs(ic, h)
        assert "public const double EntryShortDeltaTarget = 0.16d;" in cs

    def test_string_constant_rendered(self, gen, minimal_yaml):
        data = yaml.safe_load(minimal_yaml.read_text())
        ic = data["iron_condor"]
        h = gen.compute_hash(ic)
        cs = gen.render_cs(ic, h)
        assert 'public const string BacktestDateRangeStart = "2019-01-01";' in cs

    def test_generated_header_comment(self, gen, minimal_yaml):
        data = yaml.safe_load(minimal_yaml.read_text())
        ic = data["iron_condor"]
        h = gen.compute_hash(ic)
        cs = gen.render_cs(ic, h)
        assert "AUTO-GENERATED" in cs
        assert "strategies.yaml" in cs


class TestMainCli:
    def test_hash_only_prints_hash(self, gen, minimal_yaml, capsys):
        rc = gen.main(["--yaml", str(minimal_yaml), "--hash-only"])
        assert rc == 0
        captured = capsys.readouterr()
        h = captured.out.strip()
        assert len(h) == 64
        int(h, 16)

    def test_dry_run_no_file_written(self, gen, minimal_yaml, tmp_path, capsys):
        out_cs = tmp_path / "StrategyConstants.cs"
        rc = gen.main(["--yaml", str(minimal_yaml), "--output", str(out_cs), "--dry-run"])
        assert rc == 0
        assert not out_cs.exists()

    def test_output_writes_file(self, gen, minimal_yaml, tmp_path, capsys):
        out_cs = tmp_path / "StrategyConstants.cs"
        rc = gen.main(["--yaml", str(minimal_yaml), "--output", str(out_cs)])
        assert rc == 0
        assert out_cs.exists()
        content = out_cs.read_text(encoding="utf-8")
        assert "StrategyConstants" in content

    def test_missing_yaml_returns_error(self, gen, tmp_path):
        rc = gen.main(["--yaml", str(tmp_path / "nonexistent.yaml"), "--hash-only"])
        assert rc == 1

    def test_missing_iron_condor_key_returns_error(self, gen, tmp_path):
        bad_yaml = tmp_path / "bad.yaml"
        bad_yaml.write_text(yaml.dump({"other_strategy": {}}), encoding="utf-8")
        rc = gen.main(["--yaml", str(bad_yaml), "--hash-only"])
        assert rc == 1
