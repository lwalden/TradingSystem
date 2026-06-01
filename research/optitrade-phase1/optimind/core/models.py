"""
Core Pydantic data models shared across the system.

These define the data contracts between modules. Prefer these over raw dicts.
"""

from __future__ import annotations

from datetime import datetime
from typing import Annotated, Literal

from pydantic import BaseModel, Field


# ── Greeks ────────────────────────────────────────────────────────────────────

class PositionGreeks(BaseModel):
    delta: float = 0.0
    gamma: float = 0.0
    theta: float = 0.0
    vega: float = 0.0


# ── Order / Leg primitives ────────────────────────────────────────────────────

class OrderLeg(BaseModel):
    symbol: str
    right: Literal["C", "P"]
    strike: float
    expiry: Annotated[str, Field(pattern=r"^\d{8}$")]  # "YYYYMMDD" — enforced by regex
    action: Literal["BUY", "SELL"]
    quantity: int
    limit_price: float | None = None


class PositionLeg(OrderLeg):
    fill_price: float
    close_price: float | None = None
    contract_id: int | None = None   # IBKR conId


# ── Risk check ────────────────────────────────────────────────────────────────

class RiskCheck(BaseModel):
    name: str
    passed: bool
    detail: str


class RiskCheckResult(BaseModel):
    approved: bool
    checks: list[RiskCheck]
    rejection_reason: str | None = None
    suggested_adjustment: str | None = None


# ── Market context ────────────────────────────────────────────────────────────

class MarketContext(BaseModel):
    timestamp: datetime
    vix_spot: float
    vix_3m: float
    vix_slope: Literal["contango", "backwardation", "flat"]
    spx_price: float
    spx_rv10: float
    spx_rv30: float
    iv_ranks: dict[str, float] = Field(default_factory=dict)
    sector_performance: dict[str, float] = Field(default_factory=dict)
    regime_quantitative: str = "UNKNOWN"
    regime_ai: str | None = None


# ── Position ──────────────────────────────────────────────────────────────────

class Position(BaseModel):
    id: str
    strategy: str
    underlying: str
    legs: list[PositionLeg]
    entry_date: datetime
    entry_credit: float
    current_pnl: float = 0.0
    current_pnl_pct: float = 0.0
    max_profit: float = Field(gt=0)   # Always positive (max credit received)
    max_loss: float = Field(lt=0)     # Always negative (max dollar loss, e.g. -500.0)
    greeks: PositionGreeks = Field(default_factory=PositionGreeks)
    dte: int
    threat_level: Literal["GREEN", "YELLOW", "RED"] = "GREEN"
    adjustment_count: int = 0
    status: Literal["PENDING", "OPEN", "CLOSING", "CLOSED"] = "PENDING"


# ── Trade proposal ────────────────────────────────────────────────────────────

class TradeSetup(BaseModel):
    strategy: str
    underlying: str
    legs: list[OrderLeg]
    expected_credit: float
    max_risk: float
    probability_of_profit: float
    greeks: PositionGreeks = Field(default_factory=PositionGreeks)
    rationale: str = ""
    risk_check_result: RiskCheckResult | None = None
