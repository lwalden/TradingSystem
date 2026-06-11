#!/usr/bin/env bash
# =============================================================================
# tools/ci/host-boot-smoke.sh - Azure Functions host-boot smoke test (S6-002)
#
# Proves the isolated worker actually boots and registers all 4 timer
# functions. The xUnit suite never boots the Functions host, so a dead host
# can ship behind 590 green tests (S5-004r lesson) - this script closes that
# blind spot, in CI and locally.
#
# What it does:
#   1. Starts `func start` for src/TradingSystem.Functions in the background,
#      teeing all output to a log file.
#   2. Polls GET http://127.0.0.1:$PORT/admin/functions (curl --max-time 5
#      per probe) until the response lists every expected function name or
#      the overall deadline (default 180 s) expires.
#   3. Scans the captured host log for boot-failure signatures:
#      TypeLoadException, "Failed to start a new language worker", and
#      worker-restart loops (>= 3 worker start/exit lines).
#   4. Always kills the func process tree on exit (trap); exits non-zero on
#      any assertion failure, failure signature, or deadline.
#
# Preconditions:
#   - Solution built (`dotnet build`); func start reuses/refreshes the build.
#   - Azurite reachable (the blob endpoint is probed before starting the
#     host, so a missing Azurite fails fast with a clear message).
#   - Azure Functions Core Tools v4 (`func`) on PATH.
#   - jq (optional): enables the unexpected-function WARN check. When jq is
#     absent that check is silently skipped - the required-function
#     assertions are grep-based and unaffected.
#
# Environment (all optional; defaults shown; values are never echoed because
# the storage connection string can carry an account key):
#   PORT=7071                  host port. Locally, if the
#                              TradingSystem-FuncHost scheduled task already
#                              owns 7071, pick a free one (e.g. PORT=7188).
#   DEADLINE_SECONDS=180       overall boot deadline (never hangs past this).
#   PROBE_INTERVAL_SECONDS=3   pause between admin-endpoint probes.
#   AzureWebJobsStorage="UseDevelopmentStorage=true"
#                              storage connection string (CI default; no
#                              local.settings.json is required or read).
#   EXPECTED_FUNCTIONS="DailyOrchestrator_PreMarket DailyOrchestrator_EndOfDay
#                       IncomeSleeve_MonthlyReinvest IncomeSleeve_QuarterlyAudit"
#                              space-separated list of names that MUST appear.
#                              A future 5th timer must be added here
#                              deliberately; extras only warn (see below).
#   SMOKE_LOG=<mktemp>         captured host log path (CI uploads on failure).
#
# Local invocation (dev box, git-bash, Azurite already running via the
# TradingSystem-Azurite scheduled task):
#   bash tools/ci/host-boot-smoke.sh
#   # If the local host is live on 7071 (paper-validation run), use a free
#   # port so the live worker is untouched:
#   PORT=7188 bash tools/ci/host-boot-smoke.sh
#
# CI invocation: the host-boot-smoke job in .github/workflows/ci.yml
# (ubuntu-latest, Core Tools + Azurite pinned via npm, timeout-minutes: 10).
#
# This automates docs/paper-validation-runbook.md section 2 preflight steps
# 4-5 (worker boots, all 4 timers register, no TypeLoadException, no
# worker-restart loop) as a standing merge gate for src-touching PRs.
#
# Subcommand (used by the PR test plan to exercise the scan in isolation):
#   bash tools/ci/host-boot-smoke.sh scan-log <logfile>
#     runs only the log-signature scan against an existing log file.
#
# Exit codes: 0 = pass; 1 = any failure (missing function, failure signature,
# deadline expired, host died early, Azurite unreachable). Failure modes are
# NOT differentiated by exit code - stderr output is the diagnostic.
# =============================================================================
set -euo pipefail

# --- Configuration (env-overridable) -----------------------------------------
PORT="${PORT:-7071}"
DEADLINE_SECONDS="${DEADLINE_SECONDS:-180}"
PROBE_INTERVAL_SECONDS="${PROBE_INTERVAL_SECONDS:-3}"
# Source of truth for this default: the four [Function]-decorated methods in
# src/TradingSystem.Functions (DailyOrchestrator.cs, IncomeSleeveFunction.cs).
# When a function is added there, this list must be updated in the same PR -
# the unexpected-extras WARN below is the drift detector for that.
EXPECTED_FUNCTIONS="${EXPECTED_FUNCTIONS:-DailyOrchestrator_PreMarket DailyOrchestrator_EndOfDay IncomeSleeve_MonthlyReinvest IncomeSleeve_QuarterlyAudit}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
FUNC_DIR="$REPO_ROOT/src/TradingSystem.Functions"

fail() { echo "FAIL: $*" >&2; exit 1; }

# --- Log-signature scan ------------------------------------------------------
# Kept as a standalone function so it is testable in isolation via the
# `scan-log` subcommand (RED path of the S6-002 test plan).
scan_log_for_failures() {
  local log_file="$1"
  local rc=0

  if [[ ! -f "$log_file" ]]; then
    echo "FAIL: log file not found: $log_file" >&2
    return 1
  fi

  if grep -q "TypeLoadException" "$log_file"; then
    echo "FAIL: TypeLoadException found in host log:" >&2
    grep -n -m 3 "TypeLoadException" "$log_file" >&2
    rc=1
  fi

  if grep -q "Failed to start a new language worker" "$log_file"; then
    echo "FAIL: 'Failed to start a new language worker' found in host log" >&2
    rc=1
  fi

  local restarts
  restarts=$(grep -cE "Starting worker process|Restarting worker process|Language Worker Process exited" "$log_file" || true)
  if [[ "$restarts" -ge 3 ]]; then
    echo "FAIL: worker restart loop suspected ($restarts worker start/exit lines; threshold 3)" >&2
    rc=1
  fi

  return $rc
}

# --- Subcommand: scan-log <file> ----------------------------------------------
if [[ "${1:-}" == "scan-log" ]]; then
  [[ $# -eq 2 ]] || fail "usage: $0 scan-log <logfile>"
  scan_log_for_failures "$2"
  echo "scan-log: no failure signatures in $2"
  exit 0
fi

# --- Preconditions -------------------------------------------------------------
command -v func >/dev/null 2>&1 || fail "Azure Functions Core Tools (func) not on PATH"
command -v curl >/dev/null 2>&1 || fail "curl not on PATH"
[[ -d "$FUNC_DIR" ]] || fail "functions project directory not found: $FUNC_DIR"

SMOKE_LOG="${SMOKE_LOG:-$(mktemp "${TMPDIR:-/tmp}/host-boot-smoke.XXXXXX")}"

# CI supplies these via env only - no local.settings.json, zero secrets.
export FUNCTIONS_WORKER_RUNTIME="${FUNCTIONS_WORKER_RUNTIME:-dotnet-isolated}"
export AzureWebJobsStorage="${AzureWebJobsStorage:-UseDevelopmentStorage=true}"

# Storage reachability: derive the blob endpoint from an explicit connection
# string, else assume the well-known local Azurite blob port. Only the
# endpoint URL is printed - never the connection string (it may carry a key).
blob_probe_url="http://127.0.0.1:10000/"
if [[ "$AzureWebJobsStorage" == *"BlobEndpoint="* ]]; then
  blob_probe_url="$(printf '%s' "$AzureWebJobsStorage" | grep -oE 'BlobEndpoint=[^;]+' | cut -d= -f2-)"
fi
curl --max-time 5 -s -o /dev/null "$blob_probe_url" \
  || fail "storage (Azurite) not reachable at $blob_probe_url (expected blob port: 10000 unless AzureWebJobsStorage overrides it). Locally: ensure the TradingSystem-Azurite scheduled task is running, or point AzureWebJobsStorage at your Azurite instance"

# --- Start the host -------------------------------------------------------------
echo "Starting Functions host on port $PORT (log: $SMOKE_LOG)"
( cd "$FUNC_DIR" && exec func start --port "$PORT" ) >"$SMOKE_LOG" 2>&1 &
FUNC_PID=$!

cleanup() {
  local ec=$?
  if [[ -n "${FUNC_PID:-}" ]] && kill -0 "$FUNC_PID" 2>/dev/null; then
    case "$(uname -s)" in
      MINGW*|MSYS*|CYGWIN*)
        # git-bash: map the MSYS pid to the Windows pid, kill the whole tree.
        local winpid
        winpid="$(ps -p "$FUNC_PID" 2>/dev/null | awk 'NR==2 {print $4}')"
        if [[ -n "$winpid" ]]; then
          taskkill //PID "$winpid" //T //F >/dev/null 2>&1 || true
        fi
        kill "$FUNC_PID" 2>/dev/null || true
        ;;
      *)
        # Linux/macOS: children first, then the leader; escalate to KILL.
        pkill -TERM -P "$FUNC_PID" 2>/dev/null || true
        kill -TERM "$FUNC_PID" 2>/dev/null || true
        sleep 2
        pkill -KILL -P "$FUNC_PID" 2>/dev/null || true
        kill -KILL "$FUNC_PID" 2>/dev/null || true
        ;;
    esac
    wait "$FUNC_PID" 2>/dev/null || true
  fi
  exit "$ec"
}
trap cleanup EXIT

# --- Poll the admin endpoint until all expected functions register --------------
admin_url="http://127.0.0.1:$PORT/admin/functions"
echo "Polling $admin_url (deadline ${DEADLINE_SECONDS}s, probe timeout 5s)"
deadline=$((SECONDS + DEADLINE_SECONDS))
response=""
all_present=0

has_function() {
  # $1 = response JSON, $2 = function name
  printf '%s' "$1" | grep -qE "\"name\"[[:space:]]*:[[:space:]]*\"$2\""
}

while [[ $SECONDS -lt $deadline ]]; do
  if ! kill -0 "$FUNC_PID" 2>/dev/null; then
    echo "--- host log tail ---" >&2
    tail -n 60 "$SMOKE_LOG" >&2 || true
    scan_log_for_failures "$SMOKE_LOG" || true
    echo "full captured host log: $SMOKE_LOG" >&2
    fail "func host process exited before the admin endpoint came up"
  fi

  if response="$(curl --max-time 5 -fsS "$admin_url" 2>/dev/null)"; then
    all_present=1
    for fn in $EXPECTED_FUNCTIONS; do
      if ! has_function "$response" "$fn"; then
        all_present=0
        break
      fi
    done
    if [[ $all_present -eq 1 ]]; then
      break
    fi
  fi
  sleep "$PROBE_INTERVAL_SECONDS"
done

if [[ $all_present -ne 1 ]]; then
  echo "--- host log tail ---" >&2
  tail -n 60 "$SMOKE_LOG" >&2 || true
  echo "full captured host log: $SMOKE_LOG" >&2
  if [[ -n "$response" ]]; then
    for fn in $EXPECTED_FUNCTIONS; do
      has_function "$response" "$fn" \
        || echo "FAIL: expected function missing from host registration: $fn" >&2
    done
    fail "host responded but did not register all expected functions within ${DEADLINE_SECONDS}s"
  fi
  fail "admin endpoint did not respond within ${DEADLINE_SECONDS}s"
fi

# Warn (don't fail) on unexpected extras so a future 5th timer cannot pass
# this gate silently - it must be added to EXPECTED_FUNCTIONS deliberately.
# Top-level name extraction needs jq (binding entries also carry "name");
# skipped quietly when jq is absent - the must-be-present assertions above
# are grep-based and unaffected.
if command -v jq >/dev/null 2>&1; then
  while IFS= read -r fn; do
    fn="${fn%$'\r'}" # jq emits CRLF on Windows/git-bash
    [[ -n "$fn" ]] || continue
    case " $EXPECTED_FUNCTIONS " in
      *" $fn "*) ;;
      *) echo "WARN: unexpected function registered: $fn (add it to EXPECTED_FUNCTIONS deliberately)" ;;
    esac
  done < <(printf '%s' "$response" | jq -r '.[].name' 2>/dev/null || true)
fi

# --- Log-signature scan (boot can "succeed" while the log shows a sick host) ----
scan_log_for_failures "$SMOKE_LOG" \
  || fail "failure signatures found in host log (full captured host log: $SMOKE_LOG)"

echo "OK: host booted on port $PORT; all expected functions registered:"
for fn in $EXPECTED_FUNCTIONS; do
  echo "  - $fn"
done
echo "OK: no failure signatures in host log ($SMOKE_LOG)"
