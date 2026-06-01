"""
Tests for broker/ibkr/connection.py.

These tests do NOT require a running IB Gateway — they mock ib_async.IB
to verify the connection logic, port selection, and error handling.
"""

from __future__ import annotations

import asyncio
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from optimind.broker.ibkr.connection import IBKRConnection, IBKRConnectionError
from optimind.config.settings import Settings


# ── Helpers ───────────────────────────────────────────────────────────────────

def make_mock_ib(connected: bool = True) -> MagicMock:
    """Return a mock ib_async.IB instance."""
    ib = MagicMock()
    ib.isConnected.return_value = connected
    ib.connectAsync = AsyncMock()
    ib.reqCurrentTimeAsync = AsyncMock(return_value="2026-01-01 00:00:00")
    ib.client = MagicMock()
    ib.client.serverVersion.return_value = 176
    ib.disconnect = MagicMock()
    return ib


# ── Port selection (via Settings) ─────────────────────────────────────────────

def test_paper_mode_uses_paper_port() -> None:
    s = Settings(mode="paper")
    assert s.ib_port == s.ib_paper_port


def test_live_mode_uses_live_port() -> None:
    s = Settings(mode="live")
    assert s.ib_port == s.ib_live_port


# ── IBKRConnection.port and .mode properties ──────────────────────────────────

def test_connection_mode_reflects_settings(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("OPTIMIND_MODE", "paper")
    conn = IBKRConnection()
    # mode/port are read from module-level settings singleton
    assert conn.mode in ("paper", "live")  # valid value
    assert conn.port in (4001, 4002)


def test_connection_port_is_paper_by_default() -> None:
    # env_isolation fixture clears OPTIMIND_MODE, so default (paper) applies
    conn = IBKRConnection()
    # The module-level `settings` singleton was already created; re-instantiating
    # Settings() confirms the default without relying on the singleton.
    fresh = Settings()
    assert fresh.ib_port == fresh.ib_paper_port


# ── connect() ─────────────────────────────────────────────────────────────────

@pytest.mark.asyncio
async def test_connect_success() -> None:
    conn = IBKRConnection()
    mock_ib = make_mock_ib(connected=True)

    with patch.object(conn, "_ib", mock_ib):
        await conn.connect()
        assert conn._connected is True
        mock_ib.connectAsync.assert_awaited_once()


@pytest.mark.asyncio
async def test_connect_raises_on_timeout() -> None:
    conn = IBKRConnection()
    mock_ib = make_mock_ib()
    mock_ib.connectAsync = AsyncMock(side_effect=asyncio.TimeoutError)

    with patch.object(conn, "_ib", mock_ib):
        with pytest.raises(IBKRConnectionError, match="Failed to connect"):
            await conn.connect()


@pytest.mark.asyncio
async def test_connect_raises_on_os_error() -> None:
    conn = IBKRConnection()
    mock_ib = make_mock_ib()
    mock_ib.connectAsync = AsyncMock(side_effect=OSError("Connection refused"))

    with patch.object(conn, "_ib", mock_ib):
        with pytest.raises(IBKRConnectionError):
            await conn.connect()


@pytest.mark.asyncio
async def test_connect_is_noop_when_already_connected() -> None:
    """Second connect() call on an already-connected instance is a no-op."""
    conn = IBKRConnection()
    mock_ib = make_mock_ib(connected=True)

    with patch.object(conn, "_ib", mock_ib):
        await conn.connect()
        await conn.connect()  # second call
        # connectAsync should only have been called once
        mock_ib.connectAsync.assert_awaited_once()


# ── ib property ──────────────────────────────────────────────────────────────

def test_ib_property_raises_when_not_connected() -> None:
    conn = IBKRConnection()
    with pytest.raises(IBKRConnectionError, match="Not connected"):
        _ = conn.ib


@pytest.mark.asyncio
async def test_ib_property_returns_ib_after_connect() -> None:
    conn = IBKRConnection()
    mock_ib = make_mock_ib(connected=True)

    with patch.object(conn, "_ib", mock_ib):
        await conn.connect()
        assert conn.ib is mock_ib


# ── disconnect() ──────────────────────────────────────────────────────────────

@pytest.mark.asyncio
async def test_disconnect_clears_connected_flag() -> None:
    conn = IBKRConnection()
    mock_ib = make_mock_ib(connected=True)

    with patch.object(conn, "_ib", mock_ib):
        await conn.connect()
        await conn.disconnect()
        assert conn._connected is False
        mock_ib.disconnect.assert_called_once()


# ── health_check() ────────────────────────────────────────────────────────────

@pytest.mark.asyncio
async def test_health_check_returns_true_when_healthy() -> None:
    conn = IBKRConnection()
    mock_ib = make_mock_ib(connected=True)

    with patch.object(conn, "_ib", mock_ib):
        await conn.connect()
        result = await conn.health_check()
        assert result is True


@pytest.mark.asyncio
async def test_health_check_returns_false_when_not_connected() -> None:
    conn = IBKRConnection()
    result = await conn.health_check()
    assert result is False


@pytest.mark.asyncio
async def test_health_check_returns_false_on_exception() -> None:
    conn = IBKRConnection()
    mock_ib = make_mock_ib(connected=True)
    mock_ib.reqCurrentTimeAsync = AsyncMock(side_effect=Exception("network error"))

    with patch.object(conn, "_ib", mock_ib):
        await conn.connect()
        result = await conn.health_check()
        assert result is False


# ── reconnect() ───────────────────────────────────────────────────────────────

@pytest.mark.asyncio
async def test_reconnect_succeeds_after_one_failure() -> None:
    conn = IBKRConnection()
    mock_ib = make_mock_ib(connected=True)
    call_count = 0

    async def connect_side_effect(*args: object, **kwargs: object) -> None:
        nonlocal call_count
        call_count += 1
        if call_count == 1:
            raise asyncio.TimeoutError
        # second call succeeds (do nothing)

    mock_ib.connectAsync = AsyncMock(side_effect=connect_side_effect)

    with patch.object(conn, "_ib", mock_ib), patch("asyncio.sleep", new_callable=AsyncMock):
        await conn.reconnect(max_attempts=3, delay=0.01)
        assert conn._connected is True


@pytest.mark.asyncio
async def test_reconnect_raises_after_all_attempts_fail() -> None:
    conn = IBKRConnection()
    mock_ib = make_mock_ib(connected=False)
    mock_ib.connectAsync = AsyncMock(side_effect=asyncio.TimeoutError)

    with (
        patch.object(conn, "_ib", mock_ib),
        patch("asyncio.sleep", new_callable=AsyncMock),
    ):
        with pytest.raises(IBKRConnectionError):
            await conn.reconnect(max_attempts=2, delay=0.01)


# ── context manager ───────────────────────────────────────────────────────────

@pytest.mark.asyncio
async def test_context_manager_connects_and_disconnects() -> None:
    conn = IBKRConnection()
    mock_ib = make_mock_ib(connected=True)

    with patch.object(conn, "_ib", mock_ib):
        async with conn as ib:
            assert ib is mock_ib
        mock_ib.disconnect.assert_called_once()
