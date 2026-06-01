"""
Hard-coded risk limits that CANNOT be overridden via config files.

These live in code (not YAML) to prevent accidental override. Changing them
requires a code change + PR review.
"""

# ── Per-trade risk ────────────────────────────────────────────────────────────
MAX_RISK_PER_TRADE_PCT: float = 2.5       # Max % of NLV at risk on any single trade
MAX_DEPLOYED_CAPITAL_PCT: float = 40.0    # Max % of NLV in open risk at any time

# ── Concentration limits ──────────────────────────────────────────────────────
MAX_POSITIONS_PER_UNDERLYING: int = 2     # e.g., max 2 iron condors on SPX
MAX_SECTOR_POSITIONS: int = 3             # e.g., max 3 tech-sector positions

# ── Portfolio Greeks ──────────────────────────────────────────────────────────
PORTFOLIO_DELTA_LIMIT_PCT: float = 10.0  # Max net delta as ±% of NLV

# ── Margin utilization ────────────────────────────────────────────────────────
MAX_MARGIN_UTILIZATION_REGT: float = 60.0  # Reg-T margin accounts
MAX_MARGIN_UTILIZATION_PM: float = 40.0    # Portfolio Margin accounts

# ── Loss circuit breakers ─────────────────────────────────────────────────────
DAILY_LOSS_HALT_PCT: float = 3.0          # Halt new entries at this daily loss %
DAILY_LOSS_EMERGENCY_PCT: float = 5.0     # Close ALL positions at this daily loss %
WEEKLY_LOSS_LIMIT_PCT: float = 5.0
MONTHLY_LOSS_LIMIT_PCT: float = 10.0

# ── Position lifecycle ────────────────────────────────────────────────────────
MAX_ADJUSTMENTS_PER_POSITION: int = 2     # Max rolls/transforms per position

# ── Watchlist ─────────────────────────────────────────────────────────────────
INDEX_UNDERLYINGS: tuple[str, ...] = ("SPX", "SPY", "QQQ", "IWM")
