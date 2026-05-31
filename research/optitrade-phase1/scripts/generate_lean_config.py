"""
generate_lean_config.py — Sprint 1.0 [S1C-001]

Reads optimind/config/strategies.yaml and emits
backtests/lean/Config/StrategyConstants.cs with all strategy
parameters as C# constants. Prints the SHA256 parameter_hash to stdout.

Usage:
    uv run python scripts/generate_lean_config.py
    uv run python scripts/generate_lean_config.py --dry-run   # print only, no file write
    uv run python scripts/generate_lean_config.py --hash-only  # print hash and exit
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

import yaml

REPO_ROOT = Path(__file__).resolve().parent.parent
STRATEGIES_YAML = REPO_ROOT / "optimind" / "config" / "strategies.yaml"
OUTPUT_CS = REPO_ROOT / "backtests" / "lean" / "Config" / "StrategyConstants.cs"


def load_strategies(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as f:
        return yaml.safe_load(f)


def _cs_value(v: object) -> str:
    """Format a Python value as a C# literal."""
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, float):
        # Ensure trailing 'm' for decimal or 'd' — use double literals
        return f"{v}d"
    if isinstance(v, int):
        return str(v)
    if isinstance(v, str):
        return f'"{v}"'
    raise TypeError(f"Unsupported type for C# literal: {type(v)} ({v!r})")


def _cs_type(v: object) -> str:
    if isinstance(v, bool):
        return "bool"
    if isinstance(v, float):
        return "double"
    if isinstance(v, int):
        return "int"
    if isinstance(v, str):
        return "string"
    raise TypeError(f"Unsupported type for C# constant: {type(v)}")


def flatten(data: dict, prefix: str = "") -> list[tuple[str, object]]:
    """Recursively flatten nested dict into (name, value) pairs.

    Skips list values (e.g. dte_management schedules) — complex structures
    are documented in comments rather than emitted as constants.
    """
    items: list[tuple[str, object]] = []
    for key, value in data.items():
        name = f"{prefix}{key}" if prefix else key
        if isinstance(value, dict):
            items.extend(flatten(value, prefix=f"{name}_"))
        elif isinstance(value, list):
            # Skip lists; add a comment placeholder handled in render
            items.append((name, value))
        else:
            items.append((name, value))
    return items


def to_pascal(snake: str) -> str:
    """Convert snake_case to PascalCase."""
    return "".join(part.capitalize() for part in snake.split("_"))


def render_cs(ic: dict, parameter_hash: str) -> str:
    """Render the C# StrategyConstants class from strategies.yaml iron_condor section."""
    lines: list[str] = []
    lines.append("// StrategyConstants.cs — AUTO-GENERATED. Do not edit manually.")
    lines.append("// Source: optimind/config/strategies.yaml")
    lines.append(f'// parameter_hash: {parameter_hash}')
    lines.append("//")
    lines.append("// Regenerate with: uv run python scripts/generate_lean_config.py")
    lines.append("")
    lines.append("namespace OptiMind.Backtests")
    lines.append("{")
    lines.append("    public static class StrategyConstants")
    lines.append("    {")

    flat = flatten(ic)
    for name, value in flat:
        pascal = to_pascal(name)
        if isinstance(value, list):
            # Render list items as individual indexed constants where possible,
            # or as a structured comment block.
            lines.append(f"        // {pascal}: complex schedule — see strategies.yaml")
            for i, item in enumerate(value):
                if isinstance(item, dict):
                    for k, v in item.items():
                        if not isinstance(v, (bool, int, float, str)):
                            continue
                        sub_name = f"{pascal}_{i}_{to_pascal(k)}"
                        if isinstance(v, str):
                            lines.append(f'        public const string {sub_name} = "{v}";')
                        elif isinstance(v, int) and not isinstance(v, bool):
                            lines.append(f"        public const int {sub_name} = {v};")
                        elif isinstance(v, float):
                            lines.append(f"        public const double {sub_name} = {v}d;")
        else:
            try:
                cs_t = _cs_type(value)
                cs_v = _cs_value(value)
                lines.append(f"        public const {cs_t} {pascal} = {cs_v};")
            except TypeError:
                lines.append(f"        // {pascal}: unsupported type — see strategies.yaml")

    lines.append("")
    lines.append(f'        public const string ParameterHash = "{parameter_hash}";')
    lines.append("    }")
    lines.append("}")
    lines.append("")
    return "\n".join(lines)


def compute_hash(ic: dict) -> str:
    """Compute a stable SHA256 hash of the iron_condor config dict.

    Uses JSON with sorted keys for determinism across Python versions.
    """
    canonical = json.dumps(ic, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Generate LEAN StrategyConstants.cs from strategies.yaml")
    parser.add_argument("--dry-run", action="store_true", help="Print generated C# to stdout without writing file")
    parser.add_argument("--hash-only", action="store_true", help="Print parameter_hash and exit")
    parser.add_argument("--yaml", default=str(STRATEGIES_YAML), help="Path to strategies.yaml")
    parser.add_argument("--output", default=str(OUTPUT_CS), help="Path for generated .cs file")
    args = parser.parse_args(argv)

    yaml_path = Path(args.yaml)
    output_path = Path(args.output)

    if not yaml_path.exists():
        print(f"ERROR: strategies.yaml not found at {yaml_path}", file=sys.stderr)
        return 1

    data = load_strategies(yaml_path)

    if "iron_condor" not in data:
        print("ERROR: 'iron_condor' key not found in strategies.yaml", file=sys.stderr)
        return 1

    ic = data["iron_condor"]
    parameter_hash = compute_hash(ic)

    if args.hash_only:
        print(parameter_hash)
        return 0

    cs_content = render_cs(ic, parameter_hash)

    if args.dry_run:
        print(cs_content)
        print(f"# parameter_hash: {parameter_hash}", file=sys.stderr)
        return 0

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(cs_content, encoding="utf-8")
    print(f"Written: {output_path}")
    print(f"parameter_hash: {parameter_hash}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
