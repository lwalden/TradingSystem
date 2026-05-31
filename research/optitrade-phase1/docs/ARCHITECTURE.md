# OptiMind Technical Architecture

**Last Updated:** 2026-02-20
**Status:** Canonical architecture summary

---

## System Overview

OptiMind is an async event-driven Python system that:

1. reads market and account state,
2. produces strategy candidates,
3. enforces risk limits,
4. executes approved orders,
5. monitors open positions,
6. records performance and operational telemetry.

Modes:

- `paper` for development and validation.
- `live` for staged production execution.

Mode is controlled only by `OPTIMIND_MODE`.

## Design Principles

1. Safety before alpha.
2. Deterministic core, AI as optional enhancement.
3. One canonical parameter source.
4. Backtest/runtime parity is a first-class requirement.

## Technology Dependencies

| Category | Package | Notes |
|---|---|---|
| Runtime | Python 3.12+ | Primary language |
| Backtest | C# / LEAN | Backtesting only — not part of Python runtime |
| Dependency manager | `uv` | Locked — do not switch to pip or poetry |
| Storage | SQLite (dev) → PostgreSQL (prod) | Migrations via Alembic |
| Validation | `pydantic >= 2.0` | Data contracts across modules |
| ORM | `sqlalchemy >= 2.0`, `alembic` | DB access and migrations |
| Async SQLite | `aiosqlite >= 0.20` | **Required** — never use sync SQLite in async context |
| Broker | `ib_async >= 1.0` | IBKR API (successor to ib_insync) |
| Broker alt | `httpx` | Tradier REST API (Phase 4) |
| Options math | `py_vollib` | Black-Scholes Greeks and IV |
| Analytics | `numpy`, `pandas`, `scipy` | Data manipulation and statistics |
| AI | `anthropic >= 0.40` | Claude API SDK |
| Prompt templates | `jinja2` | AI prompt management |
| MCP | `mcp >= 1.0` | Model Context Protocol SDK |
| Dashboard | `streamlit >= 1.30`, `plotly` | Operational dashboard |
| Scheduling | `apscheduler` | Time-based task scheduling |
| CLI | `typer` | Command-line interface |
| Logging | `structlog` | Structured log output |
| Calendar | `exchange_calendars` | Market holiday handling |
| Backtesting | LEAN CLI (`pip install lean`) | C# LEAN engine orchestration |
| Testing | `pytest`, `pytest-asyncio`, `hypothesis`, `pytest-cov` | Test suite |
| Quality | `ruff`, `mypy` | Linting and type checking |
| Ops Automation | n8n (self-hosted) | Workflow orchestration for notifications, reports, health monitoring (ADR-019) |

---

## Project Structure

Legend:
- *(implemented)* — file exists with real code
- *[stub]* — directory and `__init__.py` exist; module file not yet created
- *[not yet created]* — file does not yet exist on disk; planned for the indicated sprint

```
optimind/
├── __main__.py                 # Entry point [stub]
├── config/
│   ├── settings.py             # Pydantic settings (env vars, defaults) (implemented)
│   ├── strategies.yaml         # Strategy configurations [canonical param source] [not yet created — Sprint 1.0]
│   ├── risk_limits.yaml        # Risk parameters (non-hardcoded overrides) [not yet created — Sprint 1.0]
│   ├── watchlist.yaml          # Underlyings to monitor [not yet created — Sprint 1.0]
│   └── sectors.yaml            # Sector correlation mapping [not yet created — Sprint 1.0]
│
├── core/
│   ├── models.py               # Pydantic data models (Position, TradeSetup, etc.) (implemented)
│   ├── constants.py            # Hard-coded limits (CANNOT be overridden by config) (implemented)
│   ├── events.py               # Event bus (pub/sub for system events) [not yet created — Sprint 2.1]
│   ├── database.py             # SQLAlchemy models and session management [not yet created — Sprint 2.1]
│   └── logging.py              # Structured logging configuration [not yet created — Sprint 1.1]
│
├── broker/
│   ├── base.py                 # Abstract BrokerAdapter interface [not yet created — Sprint 2.1]
│   ├── ibkr/
│   │   ├── connection.py       # IB Gateway connection management (implemented)
│   │   ├── adapter.py          # IBKRAdapter implementation [not yet created — Sprint 1.3]
│   │   ├── orders.py           # Order construction (combo/BAG orders) [not yet created — Sprint 1.3]
│   │   └── data.py             # Market data retrieval [not yet created — Sprint 1.2]
│   └── tradier/
│       └── adapter.py          # TradierAdapter (Phase 4) [not yet created — Phase 4]
│
├── data/
│   ├── market_data.py          # Real-time market data manager [not yet created — Sprint 1.2]
│   ├── options_chain.py        # Options chain retrieval and filtering [not yet created — Sprint 1.2]
│   ├── greeks.py               # Greeks calculation (py_vollib + IBKR validation) [not yet created — Sprint 1.2]
│   ├── iv_surface.py           # IV rank, percentile, surface analysis [not yet created — Sprint 1.2]
│   └── orats.py                # ORATS data integration (Phase 3) [not yet created — Phase 3]
│
├── strategies/
│   ├── base.py                 # StrategyBase abstract class [not yet created — Sprint 2.1]
│   ├── registry.py             # Strategy registration and discovery [not yet created — Sprint 2.1]
│   ├── iron_condor.py          # Iron condor strategy [not yet created — Sprint 1.3]
│   ├── butterfly.py            # Butterfly spread strategy [not yet created — Sprint 2.1]
│   ├── credit_spread.py        # Bull put / bear call spreads [not yet created — Sprint 2.1]
│   ├── calendar_spread.py      # Calendar/horizontal spreads (Phase 3) [not yet created — Phase 3]
│   └── straddle.py             # Pre-earnings straddle (Phase 3) [not yet created — Phase 3]
│
├── risk/
│   ├── manager.py              # Pre-trade risk checks [not yet created — Sprint 1.3]
│   ├── portfolio_greeks.py     # Aggregate portfolio Greeks monitoring [not yet created — Sprint 2.2]
│   ├── circuit_breakers.py     # Daily/weekly/monthly loss limits [not yet created — Sprint 2.2]
│   ├── margin.py               # Margin utilization tracking [not yet created — Sprint 2.2]
│   └── correlation.py          # Sector correlation enforcement [not yet created — Sprint 2.2]
│
├── execution/
│   ├── engine.py               # Order execution with SmartPricing [not yet created — Sprint 1.3]
│   ├── guided.py               # Guided execution mode (approve/reject) [not yet created — Sprint 2.4]
│   └── position_manager.py     # Position lifecycle management [not yet created — Sprint 1.3]
│
├── monitor/
│   ├── position_monitor.py     # Real-time position P&L and Greeks tracking [not yet created — Sprint 1.4]
│   ├── threat_detector.py      # Adjustment trigger detection [not yet created — Sprint 2.3]
│   ├── adjustment_engine.py    # Rolling and transformation logic [not yet created — Sprint 2.3]
│   └── scheduler.py            # Time-based task scheduling [not yet created — Sprint 1.4]
│
├── ai/
│   ├── client.py               # Claude API client wrapper [not yet created — Phase 3]
│   ├── regime.py               # Market regime detection (quant + AI) [not yet created — Phase 3]
│   ├── trade_rationale.py      # Trade reasoning generation [not yet created — Phase 3]
│   ├── portfolio_review.py     # AI portfolio assessment [not yet created — Phase 3]
│   └── prompts/                # Prompt templates (Jinja2) [not yet created — Phase 3]
│       ├── regime_analysis.j2
│       ├── trade_rationale.j2
│       ├── adjustment_reasoning.j2
│       └── portfolio_review.j2
│
├── mcp/
│   ├── server.py               # MCP server main [not yet created — Phase 3]
│   └── tools.py                # MCP tool definitions [not yet created — Phase 3]
│
├── dashboard/
│   ├── app.py                  # Streamlit dashboard [not yet created — Phase 2]
│   └── pages/                  # Dashboard pages [not yet created — Phase 2]
│
├── cli/
│   ├── main.py                 # CLI entry point (Typer) [not yet created — Sprint 1.4]
│   └── commands/               # CLI command modules [not yet created — Sprint 1.4]
│
└── tax/
    ├── lot_tracker.py          # Tax lot tracking [not yet created — Phase 2]
    ├── section_1256.py         # Section 1256 60/40 treatment [not yet created — Phase 2]
    ├── wash_sale.py            # Wash sale detection [not yet created — Phase 2]
    └── reports.py              # Tax report generation [not yet created — Phase 2]
```

Backtesting artifacts (separate from Python runtime):

```
backtests/
└── lean/
    ├── Algorithm/              # C# LEAN strategy implementation [not yet created — Sprint 1.0]
    ├── Config/
    │   └── StrategyConstants.cs  # Generated by config translator — do not edit manually [not yet created — Sprint 1.0]
    ├── results/
    │   └── phase1_baseline.json  # Backtest output consumed by gate acceptance [not yet created — Sprint 1.0]
    └── lean.json               # LEAN project config [not yet created — Sprint 1.0]
scripts/
├── generate_lean_config.py    # Config translator: strategies.yaml → StrategyConstants.cs [not yet created — Sprint 1.0]
├── evaluate_phase1_gate.py    # Gate evaluator: validates phase1_baseline.json against criteria [not yet created — Sprint 1.0]
└── smoke_test_connection.py   # IBKR connection smoke test (implemented)
```

---

## Event-Driven Architecture

The system uses an internal event bus (`core/events.py`) for loose coupling between modules. Modules publish events; other modules subscribe. No direct cross-module calls in the trading loop.

```
Event                    Producers → Consumers
─────────────────────────────────────────────────────────────
MARKET_DATA_UPDATED      data/ → monitor/, strategies/
SCAN_COMPLETE            strategies/ → execution/ (guided mode), monitor/
TRADE_PROPOSED           strategies/ → risk/
RISK_APPROVED            risk/ → execution/
RISK_REJECTED            risk/ → cli/ (notification)
ORDER_SUBMITTED          execution/ → monitor/
ORDER_FILLED             execution/ → monitor/, tax/
POSITION_UPDATED         monitor/ → risk/ (portfolio Greeks)
THREAT_DETECTED          monitor/ → execution/ (adjustment)
ADJUSTMENT_PROPOSED      monitor/ → execution/ (guided mode or auto)
EXIT_TRIGGERED           monitor/ → execution/
POSITION_CLOSED          execution/ → monitor/, tax/, dashboard/
CIRCUIT_BREAKER_FIRED    risk/ → execution/ (halt), cli/ (alert)
REGIME_CHANGED           ai/ → strategies/ (weight adjustment)
AI_CACHE_USED            ai/ → monitor/ (observability)
AI_FALLBACK_TRIGGERED    ai/ → monitor/ (observability)
```

Design principle: modules don't call each other directly. This prevents circular imports and makes unit testing straightforward — each module can be tested by publishing events and asserting on resulting events or state changes.

---

## Key Data Flows

### Trade entry flow (guided mode)

```
Scheduler (10:30 AM) → Scanner
  → retrieves options chains, calculates Greeks/IV
  → identifies candidates meeting strategy criteria
  → publishes SCAN_COMPLETE

SCAN_COMPLETE → Strategy Engine
  → selects best candidate, constructs TradeSetup
  → publishes TRADE_PROPOSED

TRADE_PROPOSED → Risk Manager
  → runs all pre-trade checks
  If APPROVED → publishes RISK_APPROVED
  If REJECTED → publishes RISK_REJECTED → notification

RISK_APPROVED → Guided Execution Mode
  → stores in pending_trades, notifies user
  User: `optimind approve <id>`
  → publishes to Execution Engine

Execution Engine → builds ComboOrder
  → submits to IBKR with SmartPricing
  → monitors for fill
  On fill → publishes ORDER_FILLED

ORDER_FILLED → Position Manager → begins monitoring cycle
```

### Position monitoring flow

```
Every 60 seconds during market hours:

Position Monitor → queries IBKR for position data
  → calculates current P&L, Greeks
  → publishes POSITION_UPDATED

POSITION_UPDATED → Threat Detector
  If threat_level changes → publishes THREAT_DETECTED

POSITION_UPDATED → Exit Logic
  If profit_target | stop_loss | DTE threshold hit → publishes EXIT_TRIGGERED

EXIT_TRIGGERED → Execution Engine
  → builds close order, executes with SmartPricing
  On fill → publishes POSITION_CLOSED

POSITION_CLOSED → Performance Tracker, Tax Lot Tracker, Trade Journal
```

### AI regime assessment flow

```
Twice daily (10:15 AM, 2:00 PM ET):

Market Context Collector → gathers all data points (via MarketDataValidator)
  → VIX, term structure, IV ranks, sector performance, etc.

Regime Engine (Quantitative) → applies rule-based classification [ALWAYS runs first]
  → produces regime_quantitative  ← permanent safety-net baseline

Regime Engine (AI) → asyncio.wait_for(Claude API call, timeout=5.0):

  [SUCCESS]:
    → formats MarketContext, calls Claude, parses JSON response
    → operative_regime = regime_ai

  [TIMEOUT or API error]:
    → regime_ai = None
    Is there a prior successful AI assessment < 2 hours old?
      [YES] → operative_regime = last_successful_regime_ai
              logs: "Using cached AI regime from {timestamp}"
              emits: AI_CACHE_USED
      [NO]  → operative_regime = regime_quantitative
              logs: "No recent AI regime — using quantitative baseline"
              emits: AI_FALLBACK_TRIGGERED

  [Invalid JSON / schema mismatch]:
    → same fallback path as timeout

If operative_regime changed → publishes REGIME_CHANGED
  → Strategy Weighting Engine adjusts allocation percentages
```

**Regime fallback ladder (graceful degradation / timeout-retry-fallback):**

1. Fresh AI response (< 5s, valid JSON) → use `regime_ai`
2. AI timeout/error, prior successful AI < 2 hours old → use cached `regime_ai`
3. No recent AI → use `regime_quantitative`

The 2-hour window aligns with the twice-daily assessment cadence: a timeout at 10:15 AM still has the prior 2:00 PM result. AI failure is always non-fatal — any `anthropic` SDK exception must be caught in `ai/client.py` and must not propagate to the main trading loop.

---

## Market Hours Definition

All references to "during market hours" in this document mean:

- **Position monitor, circuit breakers, risk checks:** 9:30 AM – 4:00 PM ET (US equity/options regular session).
- **Scan times:** Per `config/strategies.yaml` schedule (default: 10:30 AM and 2:00 PM ET).
- **AI regime assessment:** 10:15 AM and 2:00 PM ET (before each scan window).
- **SPX index options:** Trade until 4:15 PM ET; monitor loop runs until 4:15 PM for SPX positions.
- **Scheduler uses `exchange-calendars` library** to detect market holidays — no hardcoded date lists.

---

## Market Data Layer: Critical Rules

### IBKR API pacing

IBKR enforces a 50 message/second rate limit. Exceeding it causes disconnection.

- Throttle all `reqMktData` and `reqHistoricalData` calls to **40 messages/second** (20% safety margin).
- Batch options chain requests: retrieve all strikes for one expiry before moving to the next.
- Implement exponential backoff on `PACING_VIOLATION` errors from IBKR.
- Use a semaphore with max **10 concurrent market data subscriptions** — no unbounded concurrent requests.
- Log pacing near-misses (> 35 msg/sec) for monitoring.
- Historical data rate: **1 request/second** (IBKR enforced).

### Data caching (mandatory)

| Data | Cache TTL | Rationale |
|---|---|---|
| Options chain snapshots | 60 seconds | Chain data is not tick-by-tick; constant re-fetch wastes pacing budget |
| IV rank per underlying | 1 hour | IV rank is a slow-moving daily metric |
| Historical volatility (for IV rank) | 24 hours | Historical data does not change intraday |
| Position Greeks (open positions) | Never cache | Always use live tick data — stale Greeks cause incorrect risk decisions |
| Underlying price (for risk checks) | 5 seconds max | Short cache acceptable; Greeks validation needs recent underlying price |
| VIX spot and term structure | 30 seconds | Slow-moving relative to equity prices |

Cache implementation: in-memory dict with TTL tracking (Phase 1-3); SQLite-backed (Phase 4, survives restarts).

### Data integrity validation

All market data passes through `MarketDataValidator` before entering the event bus. No raw IBKR tick data reaches the Risk Manager directly. Validation covers:

- Field presence and type correctness.
- Value bounds (e.g., strike > 0, IV > 0, delta in [-1, 1]).
- Staleness checks (timestamp recency).
- Leg coherence for combo positions (all legs same timestamp ± tolerance).

---

## Data Models

Key Pydantic models in `optimind/core/models.py`:

```python
class MarketContext(BaseModel):
    timestamp: datetime
    vix_spot: float
    vix_3m: float
    vix_slope: Literal["contango", "backwardation", "flat"]
    spx_price: float
    spx_rv10: float          # 10-day realized volatility
    spx_rv30: float          # 30-day realized volatility
    iv_ranks: dict[str, float]          # {"SPX": 45.2, "QQQ": 62.1}
    sector_performance: dict[str, float]  # {"XLK": 1.2, "XLE": -1.5}
    regime_quantitative: str             # From rule-based engine
    regime_ai: str | None                # From Claude, if available

class Position(BaseModel):
    id: str
    strategy: str            # "iron_condor", "butterfly", etc.
    underlying: str
    legs: list[PositionLeg]
    entry_date: datetime
    entry_credit: float      # Positive = credit received
    current_pnl: float
    current_pnl_pct: float   # As % of max profit
    max_profit: float        # Always positive (Field(gt=0))
    max_loss: float          # Always negative (Field(lt=0)), e.g. -500.0
    greeks: PositionGreeks
    dte: int                 # Days to nearest expiration
    threat_level: Literal["GREEN", "YELLOW", "RED"]
    adjustment_count: int
    status: Literal["PENDING", "OPEN", "CLOSING", "CLOSED"]

class TradeSetup(BaseModel):
    strategy: str
    underlying: str
    legs: list[OrderLeg]
    expected_credit: float
    max_risk: float
    probability_of_profit: float
    greeks: PositionGreeks
    rationale: str           # AI-generated or rule-based
    risk_check_result: RiskCheckResult | None

class RiskCheckResult(BaseModel):
    approved: bool
    checks: list[RiskCheck]  # Each check with pass/fail and detail
    rejection_reason: str | None
    suggested_adjustment: str | None  # e.g. "Reduce to 2 contracts"
```

---

## Database Schema

SQLite in development; PostgreSQL in production. Migrations managed by Alembic.

```
positions
  id TEXT PK
  strategy TEXT NOT NULL
  underlying TEXT NOT NULL
  entry_date TIMESTAMP NOT NULL
  close_date TIMESTAMP
  entry_credit REAL
  close_debit REAL
  max_profit REAL
  max_loss REAL
  realized_pnl REAL
  status TEXT DEFAULT 'OPEN'
  adjustment_count INTEGER DEFAULT 0
  rationale TEXT
  regime_at_entry TEXT

position_legs
  id INTEGER PK
  position_id TEXT FK → positions.id
  contract_symbol TEXT
  right TEXT            -- 'C' or 'P'
  strike REAL
  expiry DATE
  action TEXT           -- 'BUY' or 'SELL'
  quantity INTEGER
  fill_price REAL
  close_price REAL

orders
  id TEXT PK
  position_id TEXT FK → positions.id
  order_type TEXT       -- 'ENTRY', 'EXIT', 'ADJUSTMENT'
  status TEXT           -- 'SUBMITTED', 'FILLED', 'CANCELLED', 'REJECTED'
  submitted_at TIMESTAMP
  filled_at TIMESTAMP
  limit_price REAL
  fill_price REAL
  price_adjustments INTEGER DEFAULT 0
  commission REAL

market_context
  id INTEGER PK
  timestamp TIMESTAMP NOT NULL
  vix_spot REAL
  vix_3m REAL
  vix_slope TEXT
  spx_price REAL
  regime_quantitative TEXT
  regime_ai TEXT
  regime_confidence REAL
  raw_data JSON         -- Full structured snapshot

iv_history
  id INTEGER PK
  underlying TEXT NOT NULL
  date DATE NOT NULL
  iv_rank REAL
  iv_percentile REAL
  iv_current REAL
  iv_52w_high REAL
  iv_52w_low REAL
  UNIQUE(underlying, date)

greeks_snapshots
  id INTEGER PK
  position_id TEXT FK → positions.id
  timestamp TIMESTAMP NOT NULL
  delta REAL
  gamma REAL
  theta REAL
  vega REAL
  pnl REAL
  underlying_price REAL

risk_events
  id INTEGER PK
  timestamp TIMESTAMP NOT NULL
  event_type TEXT       -- 'TRADE_CHECK', 'CIRCUIT_BREAKER', 'MARGIN_ALERT'
  result TEXT           -- 'APPROVED', 'REJECTED', 'TRIGGERED'
  detail JSON

tax_lots
  id INTEGER PK
  position_id TEXT FK → positions.id
  open_date DATE
  close_date DATE
  instrument TEXT
  proceeds REAL
  cost_basis REAL
  gain_loss REAL
  is_section_1256 BOOLEAN
  holding_period TEXT   -- 'SHORT', 'LONG', or '60/40' for Section 1256
  wash_sale_adjustment REAL DEFAULT 0
```

---

## Configuration System

`config/strategies.yaml` is the single canonical source for all strategy parameters. The config translator (`scripts/generate_lean_config.py`) reads it at build time and generates `backtests/lean/Config/StrategyConstants.cs`. Never edit `StrategyConstants.cs` manually.

```yaml
# config/strategies.yaml (structure)
strategies:
  iron_condor:
    enabled: true
    underlyings: [SPX, SPY, QQQ, IWM]
    params:
      target_dte: 45
      short_delta: 0.30
      wing_width_spx: 50      # $50 wide on SPX
      wing_width_spy: 5       # $5 wide on SPY/QQQ/IWM
      iv_rank_min: 50
      profit_target_pct: 50
      stop_loss_pct: 200
      dte_tighten: 21
      dte_close: 14
      max_concurrent: 3
    schedule:
      scan_times: ["10:30", "14:00"]
      timezone: "US/Eastern"

  butterfly:
    enabled: true
    underlyings: [AAPL, MSFT, GOOGL, AMZN, NVDA]
    params:
      target_dte: 30
      wing_width: 10
      iv_rank_max: 30
      profit_target_pct: 50
      dte_close: 14
      max_concurrent: 2
    schedule:
      scan_times: ["10:30"]
      timezone: "US/Eastern"
```

---

## Deployment Architecture

### Development

- Local runtime + IB Gateway paper session (port 4002)
- SQLite database (`./data/optimind.db`)
- Streamlit dashboard (`localhost:8501`)

### Production

- Azure VM (D2s v5, Ubuntu 24.04)
- IB Gateway live session (port 4001), managed by IBC for auto-restart
- OptiMind as systemd service with auto-restart
- PostgreSQL managed database (Azure flexible server)
- Streamlit dashboard behind nginx reverse proxy
- Azure Blob Storage for daily backups
- Azure Monitor for health metrics and alerting
- MCP server accessible via SSH tunnel for Claude Desktop

---

## Networking & Localization

### Why region matters for options execution

IBKR's options execution infrastructure is co-located in **Secaucus, NJ** (IBKR's primary data center). Round-trip latency from the trading process to IBKR directly affects fill quality on SmartPricing walks — each $0.05 adjustment step waits for an acknowledgment before submitting the next one.

| Deployment Location | RTT to IBKR Secaucus NJ | Impact |
|---|---|---|
| Local dev (Kirkland, WA) | ~70-90ms | Acceptable for paper trading (Phases 1-3) |
| Azure West US 2 (Washington) | ~65-75ms | No meaningful improvement over local |
| **Azure East US (Virginia)** | **~5-12ms** | **Required for production** |
| Azure East US 2 (Virginia) | ~8-15ms | Acceptable fallback |

### Production region: Azure East US (Virginia)

**Required region for the production VM (Phase 4 go-live).**

- 5-12ms RTT vs IBKR Secaucus NJ — approximately 10x improvement over local WA dev.
- Reduces per-step SmartPricing latency from ~80ms to ~10ms (meaningful for 60-second walk windows).
- Azure East US is IBKR's nearest Azure region; co-location is the single biggest latency lever.
- All Azure resources (VM, PostgreSQL, Blob Storage, Monitor) in East US to minimize inter-region transfer costs and latency.

### Local development exception

During Phases 1-3 (paper trading only), the ~80ms local WA latency is acceptable:
- Paper fills are simulated; IBKR does not fill paper orders at the speed of live markets.
- SmartPricing latency is not a correctness issue during paper trading — it only affects whether you get better mid-price fills in live trading.
- No financial risk from latency during paper phases; optimize for developer ergonomics.

**Transition rule:** Switch from local → Azure East US VM before switching `OPTIMIND_MODE=live`. Do not run live trading from a home internet connection.

### IB Gateway network requirements

- IB Gateway must run on the **same host** as OptiMind (loopback connection: `127.0.0.1:4001/4002`).
- IB Gateway cannot be exposed over the public internet; use SSH tunnel if debugging remotely.
- IBC (IB Controller) manages auto-login and restart of IB Gateway on the Azure VM.
- Outbound ports required: 4001 (live) / 4002 (paper) on localhost; IBKR requires outbound 443 for its own connections.

---

## Security Baseline

- Secrets only via env vars — no credentials in code or config files.
- No credentials committed to the repo.
- Dashboard exposure restricted (HTTPS + auth).
- Explicit safeguards against accidental live execution (`OPTIMIND_MODE` must be set explicitly).

---

## Operations Automation (n8n)

n8n provides workflow orchestration for operational concerns that sit outside the core trading loop. The trading runtime (Python/async) publishes events; n8n workflows consume them via webhooks and handle notification, reporting, and monitoring tasks. See ADR-019.

**Development:** n8n runs locally (v2.11.4, `n8n start` on port 5678). Workflow JSON version-controlled in `d:\Source\n8n-automation-hub`.

**Production:** n8n on Azure VM (same region as OptiMind for low-latency webhook delivery).

**Integration pattern:** OptiMind's event bus publishes to n8n via HTTP webhook endpoints. n8n workflows are triggered by these events and use the built-in Anthropic Chat Model node for Claude-powered analysis (e.g., market regime summaries, P&L commentary).

| Workflow | Trigger | Description | Phase |
|----------|---------|-------------|-------|
| Gate Evaluation Runner | Manual | Execute gate scripts, format results, email report | Phase 1 |
| Trade Alerts | Webhook (ORDER_FILLED, POSITION_CLOSED) | Format trade notification, email | Phase 2 |
| Daily P&L Report | Cron (market close + 30min) | Query positions DB, calculate P&L, email report | Phase 2 |
| Risk Alert Escalation | Webhook (CIRCUIT_BREAKER_FIRED) | Urgent notification on loss limit breach | Phase 2 |
| IBKR Health Monitor | Cron (1min during market hours) | Check IBKR connection, alert on disconnect | Phase 2 |
| Market Regime Monitor | Cron (daily pre-market) | Check VIX/IV, Claude-powered regime briefing | Phase 3 |

**Design constraint:** n8n workflows must never modify trading state. They are read-only consumers of events and database snapshots. All trade decisions remain in the Python runtime.

---

## Governance References

- Canonical roadmap: `docs/PROJECT_STRATEGY.md`
- Validation gates and acceptance procedure: `docs/VALIDATION_GATES.md`
- Risk framework: `docs/RISK_FRAMEWORK.md`
- Cost model: `docs/COST_MODEL.md`
- Performance model: `docs/PERFORMANCE_MODEL.md`
- Parity controls: `docs/BACKTEST_LIVE_PARITY.md`
