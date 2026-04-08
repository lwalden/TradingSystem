"""
run_cloud_backtest.py — Automated QuantConnect cloud backtest pipeline.

Pipeline:
  1. Read strategies.yaml, regenerate SC constants in QC_CloudBacktest.cs
  2. Push updated algorithm to QC cloud project via REST API
  3. Compile (with polling)
  4. Create and run backtest (with polling + progress)
  5. Download algorithm log from QC API
  6. Save raw results + log to backtests/lean/results/
  7. Parse log → update phase1_baseline.json
  8. Run gate evaluator → print summary

Credentials (one of):
  - Bitwarden Login item "QuantConnect API": username=userId, password=apiToken
  - Env vars: QC_USER_ID and QC_API_TOKEN

QC project ID is read from backtests/lean/config.json (cloud-id).

Usage:
    uv run python scripts/run_cloud_backtest.py
    uv run python scripts/run_cloud_backtest.py --no-push     # skip file push
    uv run python scripts/run_cloud_backtest.py --no-eval     # skip gate evaluator at end
    uv run python scripts/run_cloud_backtest.py --name "my-run"  # custom backtest name
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import time
from base64 import b64encode
from datetime import datetime, timezone
from pathlib import Path

import httpx
import yaml

TOOL_DIR         = Path(__file__).resolve().parent
REPO_ROOT        = TOOL_DIR.parent.parent
CONFIG_JSON      = TOOL_DIR / "config" / "config.json"
CLOUD_CS         = TOOL_DIR / "algorithms" / "IronCondorBaseline.cs"
STRATEGIES_YAML  = TOOL_DIR / "config" / "strategies.yaml"
RESULTS_DIR      = TOOL_DIR / "results"
GENERATE         = TOOL_DIR / "generate_lean_config.py"
PARSE            = TOOL_DIR / "parse_lean_results.py"
EVALUATE         = TOOL_DIR / "evaluate_phase1_gate.py"

QC_API_BASE        = "https://www.quantconnect.com/api/v2"
POLL_INTERVAL_S    = 30
COMPILE_TIMEOUT_S  = 180
BACKTEST_TIMEOUT_S = 5400  # 90 min ceiling; minute-resolution runs take ~60 min


# ---------------------------------------------------------------------------
# SC constant generation from strategies.yaml
# ---------------------------------------------------------------------------

def _compute_hash(ic: dict) -> str:
    canonical = json.dumps(ic, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def _cs_literal(v: object) -> str:
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, float):
        return f"{v}d"
    if isinstance(v, int):
        return str(v)
    if isinstance(v, str):
        return f'"{v}"'
    raise TypeError(f"Unsupported: {type(v)}")


def _cs_type(v: object) -> str:
    if isinstance(v, bool):
        return "bool"
    if isinstance(v, float):
        return "double"
    if isinstance(v, int):
        return "int"
    if isinstance(v, str):
        return "string"
    raise TypeError(f"Unsupported: {type(v)}")


def generate_sc_block(ic: dict, parameter_hash: str) -> str:
    """Generate the SC class body from the iron_condor config dict.

    Maps strategies.yaml paths to the hand-curated SC constant names
    used by QC_CloudBacktest.cs.
    """
    entry = ic["entry"]
    exit_ = ic["exit"]
    sizing = ic["sizing"]
    bt = ic["backtest"]

    # Find the mandatory close DTE from dte_management schedule
    mandatory_close_dte = 7  # default
    for rule in exit_.get("dte_management", []):
        if rule.get("action") == "close":
            mandatory_close_dte = rule["dte"]

    constants = [
        ("int",    "EntryDteMin",                entry["dte_min"]),
        ("int",    "EntryDteTarget",             entry["dte_target"]),
        ("int",    "EntryDteMax",                entry["dte_max"]),
        ("double", "EntryShortDeltaTarget",      entry["short_delta_target"]),
        ("double", "EntryShortDeltaTolerance",   entry["short_delta_tolerance"]),
        ("int",    "EntryWingWidthSpy",          entry["wing_width_spy"]),
        ("double", "EntryMinCreditToWidthRatio", entry["min_credit_to_width_ratio"]),
        ("double", "EntryMinAtmIv",              entry["entry_min_atm_iv"]),
        ("double", "ExitProfitTargetPct",        exit_["profit_target_pct"]),
        ("double", "ExitStopLossCreditMultiple", exit_["stop_loss_credit_multiple"]),
        ("int",    "ExitDteMandatoryClose",      mandatory_close_dte),
        ("int",    "SizingDefaultContracts",     sizing["default_contracts"]),
        ("double", "SlippagePerLegUsd",          bt["slippage_per_leg_usd"]),
        ("double", "CommissionPerContractUsd",   bt["commission_per_contract_usd"]),
        ("int",    "InitialCapitalUsd",          bt["initial_capital_usd"]),
        ("string", "DateRangeStart",             bt["date_range_start"]),
        ("string", "DateRangeEnd",               bt["date_range_end"]),
        ("string", "InSampleEnd",                bt["in_sample_end"]),
        ("string", "OosStart",                   bt["oos_start"]),
        ("string", "ParameterHash",              parameter_hash),
    ]

    # Align columns for readability
    max_name = max(len(name) for _, name, _ in constants)
    lines = []
    for cs_type, name, value in constants:
        pad_type = f"{cs_type:<6}"
        pad_name = f"{name:<{max_name}}"
        lines.append(f"        public const {pad_type} {pad_name} = {_cs_literal(value)};")
    return "\n".join(lines)


def update_cloud_cs(cs_path: Path, yaml_path: Path) -> str:
    """Regenerate the SC constants block in QC_CloudBacktest.cs.

    Returns the parameter_hash for logging.
    """
    with yaml_path.open(encoding="utf-8") as f:
        data = yaml.safe_load(f)
    ic = data["iron_condor"]
    param_hash = _compute_hash(ic)
    sc_block = generate_sc_block(ic, param_hash)

    content = cs_path.read_text(encoding="utf-8")

    # Replace the SC class body (between "public static class SC" and closing "}")
    pattern = (
        r"(// ── Inlined strategy constants.*?\n"
        r"    // parameter_hash: )\S+\n"
        r"    public static class SC\n"
        r"    \{[^}]+\}"
    )
    replacement = (
        f"\\g<1>{param_hash}\n"
        f"    public static class SC\n"
        f"    {{\n"
        f"{sc_block}\n"
        f"    }}"
    )
    new_content, count = re.subn(pattern, replacement, content, count=1, flags=re.DOTALL)
    if count == 0:
        raise RuntimeError("Could not find SC class block in QC_CloudBacktest.cs to update")

    cs_path.write_text(new_content, encoding="utf-8")
    return param_hash


# ---------------------------------------------------------------------------
# Credentials
# ---------------------------------------------------------------------------

def _find_bw() -> str | None:
    """Locate the bw executable. Falls back to known npm-global path on Windows."""
    found = shutil.which("bw")
    if found:
        return found
    for candidate in [
        os.path.expandvars(r"%LOCALAPPDATA%\npm\bw.cmd"),
        r"D:\DevCache\npm-global\bw.cmd",
    ]:
        if os.path.isfile(candidate):
            return candidate
    return None


def _bw_get(field: str, item_name: str) -> str | None:
    """Retrieve a field from a Bitwarden item. Returns None on any failure."""
    session = os.environ.get("BW_SESSION", "")
    if not session:
        return None
    bw = _find_bw()
    if not bw:
        return None
    try:
        raw = subprocess.run(
            [bw, "get", "item", item_name, "--session", session],
            capture_output=True, text=True, timeout=15,
        )
        if raw.returncode != 0:
            return None
        item = json.loads(raw.stdout)
        if field == "username":
            return item.get("login", {}).get("username") or None
        if field == "password":
            return item.get("login", {}).get("password") or None
    except Exception:
        return None
    return None


def get_credentials() -> tuple[str, str]:
    """Return (userId, apiToken). Prefers Bitwarden; falls back to env vars."""
    bw_item = "QuantConnect API"
    user_id   = _bw_get("username", bw_item) or os.environ.get("QC_USER_ID", "")
    api_token = _bw_get("password", bw_item) or os.environ.get("QC_API_TOKEN", "")
    if not user_id or not api_token:
        print(
            "ERROR: QuantConnect credentials not found.\n"
            "  Add a Bitwarden Login item named 'QuantConnect API' with:\n"
            "    Username: your QC userId (numeric)\n"
            "    Password: your QC apiToken\n"
            "  Or set env vars: QC_USER_ID and QC_API_TOKEN",
            file=sys.stderr,
        )
        sys.exit(2)
    return user_id.strip(), api_token.strip()


# ---------------------------------------------------------------------------
# QC REST client
# ---------------------------------------------------------------------------

class QCClient:
    def __init__(self, user_id: str, api_token: str, project_id: int) -> None:
        self.user_id    = user_id
        self.api_token  = api_token
        self.project_id = project_id
        self._http      = httpx.Client(timeout=30)

    def _auth_headers(self) -> dict[str, str]:
        timestamp = str(int(time.time()))
        hash_str  = hashlib.sha256(f"{self.api_token}:{timestamp}".encode()).hexdigest()
        creds     = b64encode(f"{self.user_id}:{hash_str}".encode()).decode()
        return {"Authorization": f"Basic {creds}", "Timestamp": timestamp}

    def _post(self, endpoint: str, data: dict) -> dict:
        url  = f"{QC_API_BASE}/{endpoint}"
        resp = self._http.post(url, data=data, headers=self._auth_headers())
        resp.raise_for_status()
        body = resp.json()
        if not body.get("success", True):
            errors = body.get("errors", [body])
            raise RuntimeError(f"QC API error on {endpoint}: {errors}")
        return body

    # Files ----------------------------------------------------------------

    def list_files(self) -> list[dict]:
        body = self._post("files/read", {"projectId": self.project_id})
        return body.get("files", [])

    def _upsert_file(self, name: str, content: str, existing_names: set[str]) -> None:
        if name in existing_names:
            self._post("files/update", {
                "projectId": self.project_id,
                "name":      name,
                "content":   content,
            })
        else:
            self._post("files/create", {
                "projectId": self.project_id,
                "name":      name,
                "content":   content,
            })

    def push_file(self, qc_name: str, content: str) -> None:
        """Push a single file to the cloud project."""
        existing = {f["name"] for f in self.list_files()}
        action = "Updating" if qc_name in existing else "Creating"
        print(f"  {action}: {qc_name}")
        self._upsert_file(qc_name, content, existing)

    # Compile --------------------------------------------------------------

    def compile(self) -> str:
        """Create a compilation job and poll until done. Returns compileId."""
        print("Creating compilation job...")
        body       = self._post("compile/create", {"projectId": self.project_id})
        compile_id = body["compileId"]
        print(f"  compileId: {compile_id}")

        deadline = time.monotonic() + COMPILE_TIMEOUT_S
        while True:
            time.sleep(5)
            status = self._post("compile/read", {
                "projectId": self.project_id,
                "compileId": compile_id,
            })
            state = status.get("state", "")
            print(f"  compile state: {state}")
            if state == "BuildSuccess":
                return compile_id
            if state in ("BuildError", "BuildTimeout"):
                logs = "\n".join(status.get("logs", []))
                raise RuntimeError(f"Compilation failed ({state}):\n{logs}")
            if time.monotonic() > deadline:
                raise TimeoutError("Compilation timed out")

    # Backtest -------------------------------------------------------------

    def create_backtest(self, compile_id: str, name: str) -> str:
        """Create a backtest and return backtestId."""
        print(f"Creating backtest '{name}'...")
        body = self._post("backtests/create", {
            "projectId": self.project_id,
            "compileId": compile_id,
            "backtestName": name,
        })
        # Response nests the id inside a "backtest" object
        bt = body.get("backtest", body)
        bt_id = bt.get("backtestId") or body.get("backtestId")
        if not bt_id:
            raise RuntimeError(f"No backtestId in response: {list(body.keys())}")
        print(f"  backtestId: {bt_id}")
        return bt_id

    def poll_backtest(self, backtest_id: str) -> dict:
        """Poll until the backtest completes. Returns the final backtest dict."""
        print("Backtest running", end="", flush=True)
        deadline = time.monotonic() + BACKTEST_TIMEOUT_S
        while True:
            time.sleep(POLL_INTERVAL_S)
            status = self._post("backtests/read", {
                "projectId":  self.project_id,
                "backtestId": backtest_id,
            })
            bt = status.get("backtest", status)
            progress = bt.get("progress", 0)
            completed = bt.get("completed", False)
            print(f"\r  Progress: {progress:.0%}   ", end="", flush=True)
            if completed:
                print()  # newline
                return bt
            if time.monotonic() > deadline:
                raise TimeoutError(f"Backtest polling timed out after {BACKTEST_TIMEOUT_S // 60} minutes")

    def read_log(self, backtest_id: str) -> list[str]:
        """Download the algorithm log lines for a completed backtest.

        QC API requires: start (line offset), end (line offset), query (string).
        Max 200 lines per page.
        """
        PAGE = 200
        lines: list[str] = []

        # Get total line count
        try:
            body = self._post("backtests/read/log", {
                "projectId":  self.project_id,
                "backtestId": backtest_id,
                "start":      0,
                "end":        1,
                "query":      "",
            })
            total = body.get("length", 0)
        except Exception:
            return lines

        # Page through all lines
        offset = 0
        while offset < total:
            end = min(offset + PAGE, total)
            try:
                body = self._post("backtests/read/log", {
                    "projectId":  self.project_id,
                    "backtestId": backtest_id,
                    "start":      offset,
                    "end":        end,
                    "query":      "",
                })
            except Exception:
                break
            chunk = body.get("logs", [])
            if isinstance(chunk, str):
                chunk = [chunk]
            lines.extend(chunk)
            offset = end
        return lines

    def close(self) -> None:
        self._http.close()


# ---------------------------------------------------------------------------
# Pipeline steps
# ---------------------------------------------------------------------------

def step_update_constants() -> str:
    """Regenerate SC constants in QC_CloudBacktest.cs and local StrategyConstants.cs."""
    print("\n[1/5] Updating strategy constants from strategies.yaml...")

    # Update local StrategyConstants.cs too (for generate_lean_config hash consistency)
    result = subprocess.run(
        [sys.executable, str(GENERATE)],
        capture_output=True, text=True,
    )
    if result.returncode != 0:
        print(result.stderr, file=sys.stderr)
        raise RuntimeError("generate_lean_config.py failed")

    # Update the cloud file's SC block
    param_hash = update_cloud_cs(CLOUD_CS, STRATEGIES_YAML)
    print(f"  QC_CloudBacktest.cs updated (hash: {param_hash[:16]}...)")
    return param_hash


def step_push_file(client: QCClient) -> None:
    print("\n[2/5] Pushing QC_CloudBacktest.cs to QC cloud as Main.cs...")
    content = CLOUD_CS.read_text(encoding="utf-8")
    client.push_file("Main.cs", content)
    print("  File pushed.")


def step_compile(client: QCClient) -> str:
    print("\n[3/5] Compiling...")
    return client.compile()


def step_run_backtest(client: QCClient, compile_id: str, name: str) -> tuple[str, dict]:
    print("\n[4/5] Running backtest...")
    bt_id  = client.create_backtest(compile_id, name)
    bt     = client.poll_backtest(bt_id)
    return bt_id, bt


def step_collect_results(client: QCClient, bt_id: str, bt: dict, run_name: str) -> Path:
    print("\n[5/5] Collecting results...")

    # Save raw QC JSON result
    raw_path = RESULTS_DIR / f"{run_name}.json"
    raw_path.write_text(json.dumps(bt, indent=2), encoding="utf-8")
    print(f"  Raw result: {raw_path.name}")

    # Download log
    print("  Downloading algorithm log...")
    log_lines = client.read_log(bt_id)
    if not log_lines:
        print("  WARNING: Log endpoint returned empty — check QC account log access.", file=sys.stderr)
    log_path = RESULTS_DIR / f"{run_name}_logs.txt"
    log_path.write_text("\n".join(log_lines), encoding="utf-8")
    print(f"  Log saved:  {log_path.name} ({len(log_lines)} lines)")

    # Parse log → phase1_baseline.json
    print("  Parsing log into phase1_baseline.json...")
    result = subprocess.run(
        [sys.executable, str(PARSE), "--log", str(log_path)],
        capture_output=True, text=True,
    )
    if result.returncode != 0:
        print(result.stderr, file=sys.stderr)
        print("  WARNING: parse_lean_results.py failed — phase1_baseline.json not updated.", file=sys.stderr)
    else:
        print(f"  {result.stdout.strip()}")

    return log_path


def step_evaluate_gate() -> int:
    print("\n--- Gate Evaluation ---")
    result = subprocess.run(
        [sys.executable, str(EVALUATE)],
        text=True,
    )
    return result.returncode


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def read_project_id() -> int:
    if not CONFIG_JSON.exists():
        return 0
    with CONFIG_JSON.open() as f:
        data = json.load(f)
    return int(data.get("cloud-id", 0))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Run a QuantConnect cloud backtest end-to-end")
    parser.add_argument("--no-push", action="store_true", help="Skip file push (use current cloud state)")
    parser.add_argument("--no-eval", action="store_true", help="Skip gate evaluation at the end")
    parser.add_argument("--name",    default=None,         help="Backtest name (default: auto-<timestamp>)")
    parser.add_argument("--project-id", type=int, default=None, help="Override QC project ID")
    args = parser.parse_args(argv)

    project_id = args.project_id or read_project_id()
    if not project_id:
        print("ERROR: No project ID found in backtests/lean/config.json and --project-id not set.", file=sys.stderr)
        return 2

    run_ts   = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    run_name = args.name or f"auto-{run_ts}"

    print(f"=== OptiMind Cloud Backtest ===")
    print(f"  Project ID : {project_id}")
    print(f"  Run name   : {run_name}")
    print(f"  Started    : {run_ts}")

    user_id, api_token = get_credentials()
    client = QCClient(user_id, api_token, project_id)

    try:
        step_update_constants()

        if not args.no_push:
            step_push_file(client)
        else:
            print("\n[2/5] Skipping file push (--no-push).")

        compile_id = step_compile(client)
        bt_id, bt  = step_run_backtest(client, compile_id, run_name)
        step_collect_results(client, bt_id, bt, run_name)
    finally:
        client.close()

    if not args.no_eval:
        gate_rc = step_evaluate_gate()
        return gate_rc

    return 0


if __name__ == "__main__":
    sys.exit(main())
