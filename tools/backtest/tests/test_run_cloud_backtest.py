"""Tests for run_cloud_backtest.py hygiene fixes (S2-006a).

Covers:
  - QCClient.poll_backtest aborts the cloud job on timeout (no orphaned paid node).
  - QCClient.read_log pagination is bounded by MAX_LOG_PAGES.
  - abort_backtest is best-effort: it swallows transport errors and never masks
    the original TimeoutError.

Run: pytest tools/backtest/tests/ -q

These tests monkeypatch QCClient._post so no network / credentials are needed.
"""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

import pytest

# Import the module under test directly by path so the tests are independently
# runnable from any working directory (no package install, no sys.path hacks
# leaking into other test files).
_MODULE_PATH = Path(__file__).resolve().parent.parent / "run_cloud_backtest.py"
_spec = importlib.util.spec_from_file_location("run_cloud_backtest", _MODULE_PATH)
assert _spec is not None and _spec.loader is not None
rcb = importlib.util.module_from_spec(_spec)
sys.modules["run_cloud_backtest"] = rcb
_spec.loader.exec_module(rcb)


def _make_client() -> "rcb.QCClient":
    """Construct a QCClient without opening a real httpx.Client connection cost.

    httpx.Client() is cheap and offline (no connection until a request is made),
    so we just build a normal client with dummy creds; every test replaces _post.
    """
    return rcb.QCClient(user_id="u", api_token="t", project_id=12345)


# ---------------------------------------------------------------------------
# (a) poll_backtest aborts on timeout
# ---------------------------------------------------------------------------

def test_poll_timeout_aborts_backtest(monkeypatch):
    """On poll timeout the client must attempt to abort the cloud job before the
    TimeoutError propagates, so no paid node is left running."""
    client = _make_client()

    # Force an immediate timeout: zero-length deadline window.
    monkeypatch.setattr(rcb, "BACKTEST_TIMEOUT_S", 0)
    monkeypatch.setattr(rcb.time, "sleep", lambda *_a, **_k: None)

    abort_calls: list[str] = []

    def fake_post(endpoint: str, data: dict) -> dict:
        # backtests/read never reports completion -> drives the timeout branch.
        if endpoint == "backtests/read":
            return {"backtest": {"progress": 0.1, "completed": False}}
        # the abort endpoint (delete fallback) — record that it was invoked.
        abort_calls.append(endpoint)
        return {"success": True}

    monkeypatch.setattr(client, "_post", fake_post)

    with pytest.raises(TimeoutError):
        client.poll_backtest("bt-123")

    # The abort must have been attempted (exactly the backtest job we were polling).
    assert abort_calls, "poll_backtest timeout did not attempt to abort the cloud job"


# ---------------------------------------------------------------------------
# (b) read_log bounded by MAX_LOG_PAGES
# ---------------------------------------------------------------------------

def test_read_log_bounded_by_max_pages(monkeypatch, capsys):
    """When the log is effectively unbounded, read_log must stop after
    MAX_LOG_PAGES pages and emit a stderr warning rather than loop forever."""
    client = _make_client()

    assert hasattr(rcb, "MAX_LOG_PAGES"), "MAX_LOG_PAGES module constant missing"
    max_pages = rcb.MAX_LOG_PAGES

    call_count = {"n": 0}

    def fake_post(endpoint: str, data: dict) -> dict:
        call_count["n"] += 1
        # Report a huge total so offset never naturally reaches it.
        return {"length": 10_000_000, "logs": ["line"]}

    monkeypatch.setattr(client, "_post", fake_post)

    lines = client.read_log("bt-123")

    captured = capsys.readouterr()
    # Page count is bounded: 1 length probe + at most MAX_LOG_PAGES data pages.
    assert call_count["n"] <= max_pages + 1, (
        f"read_log made {call_count['n']} calls, exceeding the {max_pages}-page bound"
    )
    # Returned lines are bounded by the page cap too.
    assert len(lines) <= max_pages
    assert "MAX_LOG_PAGES" in captured.err or "max" in captured.err.lower(), (
        "expected a stderr warning when the page bound is hit"
    )


def test_read_log_nonadvancing_cursor_breaks(monkeypatch):
    """A non-advancing cursor (end == offset) must not spin forever."""
    client = _make_client()

    call_count = {"n": 0}

    def fake_post(endpoint: str, data: dict) -> dict:
        call_count["n"] += 1
        # total stays > offset but PAGE math is forced to 0 advance via tiny total.
        # Report length equal to the requested start so end == offset on page loop.
        return {"length": 1, "logs": ["only-line"]}

    monkeypatch.setattr(client, "_post", fake_post)

    lines = client.read_log("bt-123")
    # Must terminate quickly (length 1 => one page then done).
    assert call_count["n"] < 10
    assert lines == ["only-line"]


# ---------------------------------------------------------------------------
# (c) abort_backtest is best-effort
# ---------------------------------------------------------------------------

def test_abort_is_best_effort(monkeypatch):
    """abort_backtest must swallow a _post failure (best-effort) and never raise."""
    client = _make_client()

    def boom(endpoint: str, data: dict) -> dict:
        raise RuntimeError("network down")

    monkeypatch.setattr(client, "_post", boom)

    # Must not raise — best-effort cleanup.
    client.abort_backtest("bt-123")


def test_poll_timeout_abort_failure_still_raises_timeout(monkeypatch):
    """If the abort attempt itself fails, the ORIGINAL TimeoutError must still
    propagate — the cleanup failure must never mask the timeout."""
    client = _make_client()

    monkeypatch.setattr(rcb, "BACKTEST_TIMEOUT_S", 0)
    monkeypatch.setattr(rcb.time, "sleep", lambda *_a, **_k: None)

    def fake_post(endpoint: str, data: dict) -> dict:
        if endpoint == "backtests/read":
            return {"backtest": {"progress": 0.1, "completed": False}}
        raise RuntimeError("abort failed too")

    monkeypatch.setattr(client, "_post", fake_post)

    with pytest.raises(TimeoutError):
        client.poll_backtest("bt-123")
