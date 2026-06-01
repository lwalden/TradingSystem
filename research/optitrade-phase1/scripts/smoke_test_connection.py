"""
Smoke test: verify IBKRConnection can connect to IB Gateway.

Run with:
    uv run python scripts/smoke_test_connection.py

Requires IB Gateway running in paper mode on port 4002.
OPTIMIND_MODE must be "paper" (the default).
"""

import asyncio
import sys

import structlog

structlog.configure(
    processors=[
        structlog.dev.ConsoleRenderer(),
    ]
)

log = structlog.get_logger()


async def main() -> None:
    from optimind.broker.ibkr.connection import IBKRConnection, IBKRConnectionError
    from optimind.config.settings import settings

    log.info("smoke_test_start", mode=settings.mode, port=settings.ib_port)

    conn = IBKRConnection()

    # ── Connect ───────────────────────────────────────────────────────────────
    try:
        await conn.connect()
    except IBKRConnectionError as e:
        log.error("connect_failed", error=str(e))
        log.error(
            "hint",
            message="Is IB Gateway running in paper mode on port 4002?",
        )
        sys.exit(1)

    log.info("connect_ok")

    # ── Health check ──────────────────────────────────────────────────────────
    healthy = await conn.health_check()
    log.info("health_check", result="OK" if healthy else "FAILED")

    # ── Account summary ───────────────────────────────────────────────────────
    ib = conn.ib
    accounts = ib.managedAccounts()
    log.info("managed_accounts", accounts=accounts)

    # ── Server time ───────────────────────────────────────────────────────────
    server_time = await ib.reqCurrentTimeAsync()
    log.info("server_time", time=str(server_time))

    # ── Disconnect ────────────────────────────────────────────────────────────
    await conn.disconnect()
    log.info("smoke_test_passed")


if __name__ == "__main__":
    asyncio.run(main())
