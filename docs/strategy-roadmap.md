# Trading System: Strategy & Technical Roadmap

> This document provides business context, goals, and technical architecture.
> Claude references this for the "why" behind development decisions.
> **Context budget:** Read on-demand, not every session. ~200 lines.

**Project Type:** api (complex multi-service application)

---

## Executive Summary

**Vision:** Build a fully automated trading system that manages a two-sleeve portfolio (70% high-yield income / 30% options) via Interactive Brokers. The system uses rule-based strategies for all execution and leverages Claude AI cost-effectively for daily market regime detection and quarterly income quality audits. The goal is consistent, risk-managed returns with minimal daily human intervention.

**Key Differentiators:**
1. **Custom strategy implementation** -- Executes YOUR specific two-sleeve strategy; no off-the-shelf product does this combination
2. **Full code ownership** -- No black box, no vendor lock-in, no subscription fees beyond infrastructure
3. **Options-first design** -- Multi-leg options strategies (credit spreads, iron condors, CSPs, calendars) with proper risk management, driven by IV/Greeks analysis
4. **Cost-effective AI** -- Claude only where it beats algorithmic rules (regime detection, quality audits); everything else is deterministic

**Success Criteria (6 months live):**
- **Returns:** Positive absolute returns OR outperform S&P 500
- **Income:** Consistent monthly income from dividends + options premiums, targeting 8-12% annualized yield
- **Risk:** Max drawdown ≤ 15%
- **Operational:** < 30 min/day of human oversight

**Cost Constraint:** Total monthly cost < $100 (Azure + Polygon.io + Claude API)

---

## Part 1: Product Strategy

### 1.1 Problem Statement

**The Problem:** Manual portfolio management for income-focused investing requires daily attention to dividend reinvestment, drift tracking, quality monitoring of income securities, scanning for options opportunities across all optionable stocks, managing multi-leg strategies, and enforcing risk limits across 20-40+ positions. This is time-consuming, error-prone, and emotionally challenging -- especially while working full-time.

**Current Approach & Gaps:**

| Current Approach | Limitation | How We're Better |
|------------------|------------|------------------|
| Manual trading | Time-intensive, emotional decisions, missed opportunities | Automated execution with consistent rules |
| Spreadsheet tracking | Reactive, no alerts, manual calculations | Real-time monitoring with proactive alerts |
| Ad-hoc research | Inconsistent, time-consuming | Systematic scans with AI-assisted regime detection |
| Mental risk management | Easy to override in the moment | Hard-coded circuit breakers that cannot be bypassed |

### 1.2 Target User

**Primary User:** Solo user (not multi-tenant)

**User Profile:**
- Senior Software Engineer with 12 years C#/.NET experience
- Strong Azure cloud services expertise
- Available ~2 hours/day for system development and oversight
- Risk-aware, values capital preservation over aggressive growth

**User Needs:**
1. **Consistent income generation** -- Automated dividend reinvestment and quality monitoring
2. **Options premium capture** -- Systematic multi-leg options strategies driven by IV and market regime
3. **Time efficiency** -- Minimal daily intervention once system is stable
4. **Risk protection** -- Automatic stops and position limits to prevent large losses
5. **Transparency** -- Clear audit trail of all decisions and trades

### 1.3 Core Feature Set

**Phase 1 -- Foundation (MVP):**

#### Feature: IBKR Integration
**Description:** Connect to paper/live trading, sync positions, account data, options chains, and historical data.
**Acceptance Criteria:**
- [ ] TWS connection established within 10 seconds
- [ ] Account NAV, cash, buying power retrieved and matching TWS display
- [ ] All positions synced with correct quantities and prices
- [ ] Options chains retrieved for any symbol
- [ ] Historical bars match TWS chart data

#### Feature: Income Sleeve Manager (70%)
**Description:** High-yield income portfolio -- CEFs, BDCs, preferreds, high-yield ETFs with quality filters. Monthly reinvestment, drift tracking, quality gates.
**Acceptance Criteria:**
- [ ] Allocation drift calculated accurately per category
- [ ] Buy list generated prioritizing most underweight categories
- [ ] Issuer cap (10%) and category cap (40%) violations detected and blocked
- [ ] Limit orders placed via IBKR API
- [ ] Full monthly reinvest cycle completes in paper trading

#### Feature: Options Sleeve Manager (30%)
**Description:** Scan all optionable stocks for IV/time decay opportunities. Execute credit spreads (verticals), iron condors/butterflies, CSPs, and calendar/diagonal spreads. Strategy selection driven by market regime.
**Acceptance Criteria:**
- [ ] Options chains scanned with IV rank/percentile calculated
- [ ] Credit spread candidates identified meeting all entry criteria
- [ ] Iron condor candidates identified in range-bound conditions
- [ ] CSP candidates identified in bullish/neutral conditions
- [ ] Calendar spread candidates identified with favorable term structure
- [ ] Multi-leg orders execute correctly via IBKR
- [ ] Rollover signals generated when approaching expiration

#### Feature: Risk Engine
**Description:** Position sizing, per-trade risk limits, daily/weekly stops, emergency halt, no-trade calendar.
**Acceptance Criteria:**
- [ ] Trade exceeding 0.4% risk per trade is rejected
- [ ] Daily stop (2% loss) halts all new trades + Discord alert
- [ ] Weekly stop (4% loss) halts all new trades until manual re-enable
- [ ] Position caps enforced (7.5% single equity, 3% single spread)
- [ ] Earnings no-trade window enforced via Polygon.io calendar

#### Feature: Claude AI -- Market Regime Detection
**Description:** Daily API call to determine market regime (trending up, trending down, range-bound, high volatility) which dictates which options strategies the system deploys.
**Acceptance Criteria:**
- [ ] Claude API call succeeds with structured JSON response
- [ ] Regime correctly maps to allowed strategy types
- [ ] System runs in rule-only fallback mode if Claude is unavailable
- [ ] API cost tracked and logged (target: ~$2-5/month)

#### Feature: Discord Reporting
**Description:** Daily trade summaries, P&L reports, alerts for stops/errors/regime changes.
**Acceptance Criteria:**
- [ ] Daily report contains all executed trades, P&L, positions
- [ ] Alert sent within 1 minute of stop trigger or error
- [ ] Regime change notification sent when Claude detects shift
- [ ] Messages formatted as rich Discord embeds

**Phase 2 -- AI Audits & Optimization:**
1. **Quarterly Income Quality Audits** -- Claude web search for NII, FFO, distribution coverage; red/yellow/green ratings; reduction signals
2. **Performance Analytics** -- Metrics tracking, benchmark comparison, strategy attribution
3. **Strategy Tuning** -- Parameter optimization based on paper trading results

**Phase 3 -- Validation & Go-Live:**
1. **12+ weeks paper trading validation** -- Full system running autonomously
2. **Performance assessment** -- Formal go/no-go against success criteria
3. **Live transition** -- Pilot with reduced capital, then full deployment

**Out of Scope:**
- Equity swing trades (potential future third sleeve)
- Backtesting engine (deferred to post-live)
- Multi-user / multi-tenant support
- Cryptocurrency trading
- Real-time intraday execution beyond options management

---

## Part 2: Technical Architecture

### 2.1 System Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        Azure Functions (Orchestration)                       │
│  - Pre-market daily scan (6:00 AM PT)                                       │
│  - End-of-day processing (1:30 PM PT)                                       │
│  - Monthly income reinvest (1st trading day)                                │
│  - Quarterly quality audit (1st week of Jan/Apr/Jul/Oct)                    │
└─────────────────────┬───────────────────────────────────────────────────────┘
                      │
        ┌─────────────┴─────────────┐
        ▼                           ▼
┌───────────────────┐       ┌───────────────────┐
│   Income Sleeve   │       │  Options Sleeve   │
│   Manager (70%)   │       │   Manager (30%)   │
│                   │       │                   │
│ - Monthly reinvest│       │ - IV/Greeks scan  │
│ - Drift tracking  │       │ - Credit spreads  │
│ - Quality gates   │       │ - Iron condors    │
│ - Cap enforcement │       │ - CSPs            │
└────────┬──────────┘       │ - Calendar spreads│
         │                  │ - Roll management │
         │                  └────────┬──────────┘
         └───────────┬───────────────┘
                     ▼
         ┌───────────────────────────┐
         │      Risk Manager         │
         │ - Per-trade limits        │
         │ - Daily/weekly stops      │
         │ - Position caps           │
         │ - No-trade calendar       │
         └───────────┬───────────────┘
                     ▼
         ┌───────────────────────────┐
         │   Execution Service       │
         │ - Order validation        │
         │ - IBKR order placement    │
         │ - Multi-leg orders        │
         │ - Fill tracking           │
         └───────────┬───────────────┘
                     │
        ┌────────────┼────────────┐
        ▼            ▼            ▼
┌─────────────┐ ┌─────────────┐ ┌─────────────┐
│ IBKR API    │ │ Claude API  │ │ Discord     │
│ (Broker)    │ │ (Regime)    │ │ (Alerts)    │
└─────────────┘ └─────────────┘ └─────────────┘
                     │
                     ▼
         ┌───────────────────────────┐
         │   Azure Cosmos DB         │
         │ - Trades & signals        │
         │ - Orders & fills          │
         │ - Daily snapshots         │
         │ - Configuration           │
         └───────────────────────────┘
```

### 2.2 Technology Choices

| Component | Technology | Rationale |
|-----------|------------|-----------|
| Language | C# / .NET 8 | Developer's 12-year expertise, strong typing |
| Orchestration | Azure Functions (Isolated Worker) | Serverless, cost-effective for scheduled tasks |
| Database | Azure Cosmos DB (Serverless) | Flexible schema, low cost at low volume |
| Broker API | Interactive Brokers TWS API | Full options support, paper trading, institutional grade |
| AI Analysis | Claude API | Cost-effective regime detection (~$2-5/mo) |
| Notifications | Discord Webhooks | Free, mobile-friendly, rich embeds |
| Secrets | Azure Key Vault | Enterprise-grade secret management |
| Monitoring | Application Insights | Built-in Azure integration |
| Market Data | Polygon.io | Reliable earnings calendar ($29/month) |
| Testing | xUnit, Moq | .NET standard |

### 2.3 Data Models

**Position**
```json
{
  "id": "uuid",
  "symbol": "SCHD",
  "securityType": "STK|OPT|COMBO",
  "quantity": 100,
  "averageCost": 75.50,
  "marketPrice": 78.25,
  "marketValue": 7825.00,
  "sleeve": "Income|Options",
  "category": "DividendGrowthETF|CreditSpread|IronCondor|CSP|CalendarSpread",
  "openedAt": "2026-01-15T14:30:00Z",
  "lastUpdated": "2026-02-04T20:00:00Z"
}
```

**Trade**
```json
{
  "id": "uuid",
  "partitionKey": "2026-02",
  "symbol": "ARCC",
  "action": "Buy|Sell|OpenSpread|CloseSpread|Roll",
  "legs": [
    { "symbol": "SPY 260321C550", "action": "Sell", "quantity": 1 },
    { "symbol": "SPY 260321C560", "action": "Buy", "quantity": 1 }
  ],
  "entryPrice": 1.50,
  "exitPrice": null,
  "realizedPnL": null,
  "sleeve": "Income|Options",
  "strategyId": "options-credit-spread",
  "rationale": "Bear call spread: IV rank 72, range-bound regime",
  "entryTime": "2026-02-03T14:35:00Z"
}
```

**Signal**
```json
{
  "id": "uuid",
  "strategyId": "options-iron-condor|options-csp|options-credit-spread|options-calendar",
  "symbol": "AAPL",
  "direction": "Neutral|Bullish|Bearish",
  "strength": "Strong|Moderate|Weak",
  "ivRank": 68,
  "ivPercentile": 72,
  "suggestedLegs": [...],
  "maxProfit": 150.00,
  "maxLoss": 350.00,
  "probabilityOfProfit": 0.68,
  "rationale": "Iron condor: high IV rank, range-bound regime, 30 DTE",
  "generatedAt": "2026-02-04T13:00:00Z",
  "expiresAt": "2026-02-04T21:00:00Z",
  "status": "Active|Expired|Executed|Rejected"
}
```

**DailySnapshot**
```json
{
  "id": "uuid",
  "partitionKey": "2026-02",
  "date": "2026-02-04",
  "netLiquidationValue": 102500.00,
  "incomeSleeveValue": 71750.00,
  "optionsSleeveValue": 28500.00,
  "cashValue": 2250.00,
  "dailyPnL": 325.00,
  "dailyPnLPercent": 0.32,
  "ytdReturn": 2.5,
  "maxDrawdown": -1.2,
  "tradesExecuted": 2,
  "dividendsReceived": 45.00,
  "optionsPremiumCollected": 150.00,
  "marketRegime": "RangeBound"
}
```

**Configuration** -- see ADR-009 for risk parameter defaults. Configuration stored in Cosmos DB, changeable without code deployment.

### 2.4 API Design

Azure Functions endpoints (HTTP triggers for manual operations, Timer triggers for automation):

| Trigger | Endpoint/Schedule | Description |
|---------|-------------------|-------------|
| Timer | 0 0 13 * * 1-5 (6AM PT) | Pre-market: regime detection, scans, signals |
| Timer | 0 30 20 * * 1-5 (1:30PM PT) | EOD: fill tracking, P&L calc, daily report |
| Timer | 0 0 14 1 * * (1st trading day) | Monthly income reinvest |
| Timer | 0 0 14 1-7 1,4,7,10 * (1st week of quarter) | Quarterly quality audit |
| HTTP | POST /api/emergency-halt | Manual emergency stop |
| HTTP | POST /api/config | Update configuration (requires validation) |
| HTTP | GET /api/status | Current system status, positions, P&L |

### 2.5 Security

**Authentication & Authorization:**
- IBKR: API connection via localhost (TWS/Gateway must be running on user's machine)
- Claude API: API key in Azure Key Vault
- Discord: Webhook URL in Azure Key Vault
- Azure: Managed Identity for Function App to access Key Vault and Cosmos DB

**Data Protection:**
- All data at rest encrypted (Cosmos DB default), all API calls over HTTPS
- No PII stored (trading data only), credentials never in code
- Paper trading mode enforced until explicit live switch with human approval

---

## Part 3: Risk Management

### 3.1 Technical Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| IBKR API connection drops | Medium | High | Retry logic, alerting, graceful degradation |
| Claude API rate limits/outages | Low | Medium | Fallback to rule-only mode (no regime = no new options trades) |
| Azure Function timeout | Low | Medium | Keep functions under 5 min, async patterns |
| Bad data from IBKR | Low | High | Validation checks, sanity bounds on prices/Greeks |
| Strategy logic bug | Medium | Critical | 12-week paper validation, rigorous unit tests, code review |
| Options pricing error | Medium | High | Cross-validate IV calculations against external sources |

### 3.2 Financial Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Single large loss | Medium | High | Per-trade risk limit (0.4%), defined-risk spreads only |
| Consecutive losses | Medium | High | Daily stop (2%), weekly stop (4%) |
| Overconcentration | Low | High | Position caps (7.5% equity, 3% spread), category caps (40%) |
| Black swan event | Low | Critical | Max gross leverage (1.2x), no naked options, diversification |
| Pin risk at expiration | Medium | Medium | Auto-close positions approaching expiration |
| Assignment risk | Low | Medium | Monitor ITM options, close or roll before expiration |

### 3.3 Operational Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Forgot to run TWS | Medium | Medium | Discord reminder if no connection by 6:15 AM |
| Missed earnings date | Low | High | Polygon.io calendar, no-trade window enforcement |
| Config mistake | Low | High | Validation on config changes, require confirmation |
| Monthly cost overrun | Low | Low | Cost tracking in logs, Claude API budget alerts |

### 3.4 Human Approval Requirements

The following actions **REQUIRE explicit human approval:**
1. Switching from SANDBOX to LIVE mode
2. Changing any risk cap or sleeve allocation
3. Trading within no-trade window (earnings) for special reasons
4. Rolling covered calls across ex-dividend dates

**Emergency Stop Behavior (Automatic):**
- Daily stop (2% loss): Halt ALL new trades for rest of day, Discord alert
- Weekly stop (4% loss): Halt ALL new trades until manual re-enable, Discord alert

---

## Part 4: Quality & Testing Strategy

**Quality Tier:** Split -- **Rigorous** for trading-critical code, **Standard** for the rest

**Rigorous Testing (Trading-Critical Code):**
- [x] Unit tests: Risk calculations, position sizing, options Greeks, IV rank, order generation, drift calculations
- [x] Integration tests: IBKR order execution, multi-leg orders, Cosmos DB operations
- [x] E2E tests: Full trade lifecycle from signal to execution to fill tracking
- [x] Cross-validation: IV calculations verified against external sources

**Standard Testing (Non-Critical Code):**
- [x] Unit tests: Discord formatting, report generation, config validation
- [x] Integration tests: Claude API calls, Polygon.io calendar
- [ ] E2E tests: Not required
- [ ] Security scanning: Consider for Phase 3

**CI/CD Quality Gates:**
- [x] All tests must pass before merge
- [x] Build must succeed
- [x] Trading-critical code changes require PR review

**Rationale:** Trading logic errors can lose real money. Rigorous testing for risk engine, order generation, position sizing, and options calculations is essential. Discord notifications and reporting are important but non-critical -- Standard tier is sufficient.

---

## Part 5: Development Timeline

### Phase 1: Foundation (Weeks 1-10)
**Duration:** 10 weeks (expanded from 8 to include options as core)

| Week | Focus | Deliverables |
|------|-------|--------------|
| 1-2 | IBKR Integration | TWS connection, account sync, position sync, options chains |
| 3-4 | Income Sleeve | Cosmos DB, drift calculator, quality gates, buy list, orders |
| 5-6 | Options Sleeve -- Scanning | IV rank/percentile calc, Greeks analysis, options screening, Polygon.io |
| 7-8 | Options Sleeve -- Execution | Credit spreads, iron condors, CSPs, calendars, multi-leg orders, roll signals |
| 9 | Risk Engine & AI Regime | Risk validation, stops, emergency halt, Claude regime detection |
| 10 | Reporting & Integration | Discord reports, alerts, daily orchestrator, end-to-end paper test |

### Phase 2: AI Audits & Optimization (Weeks 11-14)
**Duration:** 4 weeks

| Week | Focus | Deliverables |
|------|-------|--------------|
| 11-12 | Income Quality Audits | Quarterly audit with Claude web search, quality ratings, reduction signals |
| 13-14 | Performance & Tuning | Metrics tracking, benchmark comparison, parameter tuning |

### Phase 3: Validation & Go-Live (Weeks 15-28+)
**Duration:** 14+ weeks

| Week | Focus | Deliverables |
|------|-------|--------------|
| 15-26 | Paper Trading | 12+ weeks autonomous operation, performance analysis |
| 27-28 | Live Transition | Fund account, configure live, pilot with reduced capital |

---

## Part 6: Human Actions Required

| Action | What's Needed | When |
|--------|---------------|------|
| IBKR Account | Create account, enable paper trading, install TWS | Before Phase 1 |
| Claude API Key | Sign up at console.anthropic.com | Before Week 9 |
| Discord Setup | Create server/channel, create webhook | Before Week 10 |
| Polygon.io | Sign up for Stocks Starter ($29/month) | Before Week 5 |
| Azure Resources | Create Cosmos DB, Functions, Key Vault (or use Bicep) | Before Week 3 |

---

## Part 7: Cost Estimates

| Service | Estimated Monthly Cost | Purpose |
|---------|----------------------|---------|
| Azure Functions | ~$0-5 | Orchestration |
| Cosmos DB (Serverless) | ~$5-15 | State & config storage |
| Key Vault | ~$1 | Secret management |
| Application Insights | ~$1-5 | Monitoring |
| Storage Account | ~$1 | Function storage |
| Claude API | ~$2-10 | Regime detection + quarterly audits |
| Polygon.io | $29 | Earnings calendar |
| Discord | Free | Notifications |
| IBKR | $0 (waived with activity) | Brokerage |
| **Total** | **~$40-65/month** | **Under $100 ceiling** |

---

## Unknowns & TODOs

<!-- TODO: Intraday vs daily execution for options management
     WHEN: After Phase 1 paper trading results
     BLOCKER: Options roll/close timing
     OPTIONS: Daily batch only, intraday monitoring for near-expiry, hybrid
-->

<!-- TODO: Backtesting engine scope
     WHEN: Post-live Phase 3+ planning
     BLOCKER: Strategy optimization approach
     OPTIONS: Simple replay, realistic fills with slippage, full event-driven
-->

<!-- TODO: Swing trade third sleeve
     WHEN: After options sleeve proven in live trading
     BLOCKER: None (independent addition)
     OPTIONS: Momentum/breakout, mean reversion, multi-strategy
-->

---

## Appendix A: Income Universe

### Dividend Growth ETFs (25% target)
| Symbol | Name | Notes |
|--------|------|-------|
| VIG | Vanguard Dividend Appreciation | Large-cap dividend growers |
| SCHD | Schwab US Dividend Equity | Quality dividend focus |
| DGRO | iShares Core Dividend Growth | Broad dividend growth |
| VYM | Vanguard High Dividend Yield | Higher yield, value tilt |

### Covered Call ETFs (20% target)
| Symbol | Name | Notes |
|--------|------|-------|
| JEPI | JPMorgan Equity Premium Income | ELN-based, monthly distributions |
| JEPQ | JPMorgan Nasdaq Equity Premium | NASDAQ-focused |
| XYLD | Global X S&P 500 Covered Call | S&P 500 overlay |
| QYLD | Global X NASDAQ 100 Covered Call | Higher yield, NAV erosion risk |

### BDCs (20% target)
| Symbol | Name | Notes |
|--------|------|-------|
| ARCC | Ares Capital | Largest BDC, diversified |
| MAIN | Main Street Capital | Internal management, monthly div |
| HTGC | Hercules Capital | Tech/life sciences focus |
| OBDC | Blue Owl Capital | Large scale |
| GBDC | Golub Capital BDC | Conservative underwriting |

### Equity REITs (10% target)
| Symbol | Name | Notes |
|--------|------|-------|
| O | Realty Income | Monthly dividend, triple-net |
| STAG | STAG Industrial | Industrial/logistics |
| ADC | Agree Realty | Investment grade tenants |
| NNN | NNN REIT | Long dividend growth streak |
| VICI | VICI Properties | Gaming/hospitality |

### Mortgage REITs (10% target)
| Symbol | Name | Notes |
|--------|------|-------|
| AGNC | AGNC Investment | Agency MBS, monthly dividend |
| NLY | Annaly Capital | Largest mREIT |
| STWD | Starwood Property Trust | Commercial mortgage |
| BXMT | Blackstone Mortgage Trust | Senior commercial loans |

### Preferreds / IG Credit (10% target)
| Symbol | Name | Notes |
|--------|------|-------|
| PFF | iShares Preferred & Income | Broad preferred exposure |
| PGX | Invesco Preferred ETF | Financial sector focus |
| PFFD | Global X U.S. Preferred | Lower cost |
| LQD | iShares IG Corporate Bond | Ballast |
| VCIT | Vanguard Intermediate Corp Bond | Intermediate duration |

---

*Version: 2.0 | Last Updated: 2026-02-09*
