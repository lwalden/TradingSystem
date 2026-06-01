"""
IB Gateway connection manager.

Wraps ib_async.IB with:
- paper/live port selection from settings
- connection lifecycle (connect, disconnect, reconnect)
- health check / connectivity probe
- structured logging

Usage:
    from optimind.broker.ibkr.connection import IBKRConnection

    conn = IBKRConnection()
    await conn.connect()
    ib = conn.ib   # ib_async.IB instance, ready to use
    await conn.disconnect()

Or as an async context manager:
    async with IBKRConnection() as ib:
        accounts = await ib.accountSummaryAsync()
"""

from __future__ import annotations

import asyncio
from types import TracebackType

import structlog
from ib_async import IB

from optimind.config.settings import settings

log: structlog.stdlib.BoundLogger = structlog.get_logger(__name__)


class IBKRConnectionError(Exception):
    """Raised when connection to IB Gateway fails or is lost."""


class IBKRConnection:
    """
    Manages a single ib_async.IB connection to IB Gateway.

    Port selection:
      paper → settings.ib_paper_port  (default 4002)
      live  → settings.ib_live_port   (default 4001)

    The mode comes from settings.mode which is set by OPTIMIND_MODE env var.
    """

    def __init__(self) -> None:
        self._ib = IB()
        self._connected = False

    @property
    def ib(self) -> IB:
        """The underlying ib_async.IB instance. Only valid after connect()."""
        if not self._connected:
            raise IBKRConnectionError("Not connected — call connect() first.")
        return self._ib

    @property
    def is_connected(self) -> bool:
        return self._connected and self._ib.isConnected()

    @property
    def mode(self) -> str:
        return settings.mode

    @property
    def port(self) -> int:
        return settings.ib_port

    async def connect(self) -> None:
        """
        Connect to IB Gateway using the configured host/port/client_id.

        No-op if already connected. Raises IBKRConnectionError if connection
        cannot be established within settings.ib_timeout seconds.
        """
        if self.is_connected:
            return

        host = settings.ib_host
        port = self.port
        client_id = settings.ib_client_id

        log.info(
            "connecting_to_ibkr",
            mode=self.mode,
            host=host,
            port=port,
            client_id=client_id,
        )

        try:
            await asyncio.wait_for(
                self._ib.connectAsync(host, port, clientId=client_id),
                timeout=settings.ib_timeout,
            )
        except (OSError, asyncio.TimeoutError) as exc:
            msg = (
                f"Failed to connect to IB Gateway ({self.mode}) "
                f"at {host}:{port} — {exc}"
            )
            log.error("ibkr_connection_failed", error=str(exc), mode=self.mode, port=port)
            raise IBKRConnectionError(msg) from exc

        self._connected = True
        log.info(
            "ibkr_connected",
            mode=self.mode,
            host=host,
            port=port,
            server_version=self._ib.client.serverVersion(),
        )

    async def disconnect(self) -> None:
        """Gracefully disconnect from IB Gateway."""
        if self._ib.isConnected():
            self._ib.disconnect()
            log.info("ibkr_disconnected", mode=self.mode)
        self._connected = False

    async def health_check(self) -> bool:
        """
        Probe the connection by requesting server time.
        Returns True if healthy, False otherwise.
        """
        if not self.is_connected:
            return False
        try:
            server_time = await self._ib.reqCurrentTimeAsync()
            log.debug("ibkr_health_ok", server_time=str(server_time))
            return True
        except Exception as exc:  # noqa: BLE001
            log.warning("ibkr_health_failed", error=str(exc))
            return False

    async def reconnect(self, max_attempts: int = 3, delay: float = 5.0) -> None:
        """
        Attempt to reconnect with exponential backoff.

        Raises IBKRConnectionError if all attempts fail.
        """
        await self.disconnect()
        for attempt in range(1, max_attempts + 1):
            log.info("ibkr_reconnect_attempt", attempt=attempt, max=max_attempts)
            try:
                await self.connect()
                return
            except IBKRConnectionError:
                if attempt == max_attempts:
                    raise
                wait = delay * (2 ** (attempt - 1))
                log.warning("ibkr_reconnect_waiting", seconds=wait)
                await asyncio.sleep(wait)

    # ── Async context manager ─────────────────────────────────────────────────

    async def __aenter__(self) -> IB:
        await self.connect()
        return self._ib

    async def __aexit__(
        self,
        exc_type: type[BaseException] | None,
        exc_val: BaseException | None,
        exc_tb: TracebackType | None,
    ) -> None:
        await self.disconnect()
