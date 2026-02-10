# Automated Trading System: Strategy & Technical Roadmap

> **Purpose:** This document provides business context, goals, and technical architecture for the automated trading system.
> Claude should reference this document to understand the "why" behind development decisions.

---

## Executive Summary

**Project Vision:** 
Build a fully automated trading system that manages a configurable multi-sleeve portfolio, combining passive income generation with tactical trading opportunities. The system executes trades via Interactive Brokers, uses rule-based strategies for routine decisions, and leverages Claude AI for complex analysis requiring judgment. The goal is consistent, risk-managed returns with minimal daily human intervention.

**Key Differentiators:**
1. **Hybrid AI + Rules Architecture** - Claude AI handles nuanced analysis (market regime, quality audits, candidate ranking) while deterministic rules handle execution, avoiding AI hallucination risks in critical paths
2. **Configurable Sleeve System** - Allocations, risk parameters, and strategies are modular and adjustable without code changes
3. **Income-First Design** - Prioritizes sustainable yield generation aligned with early retirement income goals
4. **Comprehensive Risk Management** - Multiple circuit breakers (per-trade, daily, weekly stops) with automatic halts and Discord alerts

**Target Outcomes:**
- **Paper Trading (12 weeks min):** Profitable OR outperform S&P 500 for the same period
- **Live Trading:** CAGR ≥ 8%, MaxDD ≤ 15%, MAR ≥ 0.5
- **Tactical Sleeve:** Hit rate ≥ 45%, Profit Factor ≥ 1.3
- **Operational:** < 30 min/day human oversight once stable

---

## Part 1: Product Strategy

### 1.1 Problem Statement

**The Problem:**
Manual portfolio management for income-focused early retirement requires daily attention to:
- Dividend reinvestment and allocation drift
- Quality monitoring of income securities (distribution coverage, leverage, NAV erosion)
- Tactical opportunities in equities and options
- Risk management across multiple positions
- Tax-efficient execution timing

This is time-consuming and error-prone, especially while still working full-time.

**Current Approach & Gaps:**

| Current Approach | Limitation | How We're Better |
|------------------|------------|------------------|
| Manual trading via E-Trade | Time-intensive, emotional decisions, missed opportunities | Automated execution with consistent rules |
| Spreadsheet tracking | Reactive, no alerts, manual calculations | Real-time monitoring with proactive alerts |
| Ad-hoc research | Inconsistent, time-consuming | Systematic scans with AI-assisted analysis |
| Mental risk management | Easy to override in the moment | Hard-coded circuit breakers |

### 1.2 Target User

**Primary User:** Laurance (solo user, not multi-tenant)

**User Profile:**
- Senior Software Engineer with 12 years C#/.NET experience
- Strong Azure cloud services expertise
- Planning early retirement in 2-3 years
- Available ~2 hours/day for system development and oversight
- Prefers afternoon work sessions (until 4 PM PT)
- Risk-aware, values capital preservation over aggressive growth

**User Needs:**
1. **Consistent income generation** - Automated dividend reinvestment and quality monitoring
2. **Time efficiency** - Minimal daily intervention once system is stable
3. **Risk protection** - Automatic stops and position limits to prevent large losses
4. **Transparency** - Clear audit trail of all decisions and trades
5. **Flexibility** - Ability to adjust strategies and parameters without code changes

### 1.3 Core Feature Set

**MVP Features (Phase 1):**
1. ✅ **IBKR Integration** - Connect to paper trading, sync positions/account data
2. ✅ **Income Sleeve Monthly Reinvest** - Allocate dividends to reduce drift from targets
3. ✅ **Tactical Equity Scans** - Breakout and pullback setups with position sizing
4. ✅ **Risk Management** - Position limits, daily/weekly stops, no-trade calendar
5. ✅ **Daily Reports via Discord** - Trade summaries, P&L, alerts

**Phase 2 Features:**
1. 🔄 **Claude AI Integration** - Market regime analysis, tactical candidate ranking
2. 🔄 **Income Quality Audits** - Quarterly review with Claude web search
3. 🔄 **Options Strategies** - CSPs, bear call spreads, covered call overlay
4. 🔄 **Performance Dashboard** - Web-based metrics visualization

**Phase 3 Features:**
1. 📊 **Backtesting Engine** - Historical validation of strategies
2. 📊 **Strategy Optimization** - Parameter tuning based on results
3. 📊 **Advanced Options** - Multi-leg strategies, rolling automation

**Future Considerations:**
- Real-time intraday execution (currently daily batch)
- Additional brokers beyond IBKR
- Cryptocurrency integration (separate from this system)

### 1.4 Success Metrics

| Metric | Paper Trading Target | Live Trading Target |
|--------|---------------------|---------------------|
| Duration | 12 weeks minimum | Ongoing |
| Profitability | Profitable OR beat S&P 500 | CAGR ≥ 8% |
| Max Drawdown | < 15% | ≤ 15% |
| MAR Ratio | ≥ 0.5 | ≥ 0.5 |
| Tactical Hit Rate | ≥ 45% | ≥ 45% |
| Tactical Profit Factor | ≥ 1.3 | ≥ 1.3 |
| Daily Oversight Time | Establish baseline | < 30 min |

---

## Part 2: Technical Architecture

### 2.1 System Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        Azure Functions (Orchestration)                       │
│  • Pre-market daily scan (6:00 AM PT)                                       │
│  • End-of-day processing (1:30 PM PT)                                       │
│  • Monthly income reinvest (1st trading day)                                │
│  • Quarterly quality audit (1st week of Jan/Apr/Jul/Oct)                    │
└─────────────────────┬───────────────────────────────────────────────────────┘
                      │
        ┌─────────────┴─────────────┐
        ▼                           ▼
┌───────────────────┐       ┌───────────────────┐
│   Income Sleeve   │       │  Tactical Sleeve  │
│   Manager (70%)   │       │   Manager (30%)   │
│                   │       │                   │
│ • Monthly reinvest│       │ • Breakout scans  │
│ • Drift tracking  │       │ • Pullback scans  │
│ • Quality gates   │       │ • Options (Ph2)   │
│ • Cap enforcement │       │ • Position sizing │
└────────┬──────────┘       └────────┬──────────┘
         │                           │
         └───────────┬───────────────┘
                     ▼
         ┌───────────────────────────┐
         │      Risk Manager         │
         │ • Per-trade limits        │
         │ • Daily/weekly stops      │
         │ • Position caps           │
         │ • No-trade calendar       │
         └───────────┬───────────────┘
                     ▼
         ┌───────────────────────────┐
         │   Execution Service       │
         │ • Order validation        │
         │ • IBKR order placement    │
         │ • Fill tracking           │
         │ • Slippage monitoring     │
         └───────────┬───────────────┘
                     │
        ┌────────────┼────────────┐
        ▼            ▼            ▼
┌─────────────┐ ┌─────────────┐ ┌─────────────┐
│ IBKR API    │ │ Claude API  │ │ Discord     │
│ (Broker)    │ │ (Analysis)  │ │ (Alerts)    │
└─────────────┘ └─────────────┘ └─────────────┘
                     │
                     ▼
         ┌───────────────────────────┐
         │   Azure Cosmos DB         │
         │ • Trades & signals        │
         │ • Orders & fills          │
         │ • Daily snapshots         │
         │ • Configuration           │
         └───────────────────────────┘
```

### 2.2 Technology Choices

| Component | Technology | Rationale |
|-----------|------------|-----------|
| Language | C# / .NET 8 | Laurance's 12-year expertise, excellent Azure integration |
| Orchestration | Azure Functions (Consumption) | Serverless, cost-effective for scheduled tasks, familiar to Laurance |
| Database | Azure Cosmos DB (Serverless) | Flexible schema, low cost at low volume, built-in TTL for cleanup |
| Broker API | Interactive Brokers TWS API | Full options support, paper trading, institutional grade |
| AI Analysis | Claude API (Sonnet) | Cost-effective for analysis tasks, good JSON output |
| Notifications | Discord Webhooks | Free, mobile-friendly, supports rich embeds |
| Secrets | Azure Key Vault | Secure credential storage, managed identity access |
| Monitoring | Application Insights | Integrated with Azure Functions, alerting built-in |
| Source Control | GitHub | Standard, integrates with Claude Code workflow |

### 2.3 Data Models

**Core Entities:**

**Position**
```json
{
  "id": "uuid",
  "symbol": "SCHD",
  "securityType": "STK",
  "quantity": 100,
  "averageCost": 75.50,
  "marketPrice": 78.25,
  "marketValue": 7825.00,
  "sleeve": "Income",
  "category": "DividendGrowthETF",
  "openedAt": "2025-01-15T14:30:00Z",
  "lastUpdated": "2025-02-04T20:00:00Z"
}
```

**Trade**
```json
{
  "id": "uuid",
  "partitionKey": "2025-02",
  "symbol": "ARCC",
  "action": "Buy",
  "quantity": 50,
  "entryPrice": 21.50,
  "exitPrice": null,
  "realizedPnL": null,
  "sleeve": "Income",
  "strategyId": "income-monthly-reinvest",
  "rationale": "Reinvest to reduce BDC drift of -3.2%",
  "entryTime": "2025-02-03T14:35:00Z"
}
```

**Signal**
```json
{
  "id": "uuid",
  "strategyId": "tactical-momentum-breakout",
  "symbol": "NVDA",
  "direction": "Long",
  "strength": "Strong",
  "suggestedEntryPrice": 725.00,
  "suggestedStopPrice": 695.00,
  "suggestedTargetPrice": 785.00,
  "rationale": "Breakout above pivot with 2.1x volume",
  "generatedAt": "2025-02-04T13:00:00Z",
  "expiresAt": "2025-02-04T21:00:00Z",
  "status": "Active"
}
```

**DailySnapshot**
```json
{
  "id": "uuid",
  "partitionKey": "2025-02",
  "date": "2025-02-04",
  "netLiquidationValue": 102500.00,
  "incomeSleeveValue": 71750.00,
  "tacticalSleeveValue": 28500.00,
  "cashValue": 2250.00,
  "dailyPnL": 325.00,
  "dailyPnLPercent": 0.32,
  "ytdReturn": 2.5,
  "maxDrawdown": -1.2,
  "tradesExecuted": 2,
  "dividendsReceived": 45.00
}
```

### 2.4 Configuration Schema

```json
{
  "mode": "Sandbox",
  "sleeveAllocations": {
    "income": 0.70,
    "tactical": 0.30
  },
  "risk": {
    "riskPerTradePercent": 0.004,
    "dailyStopPercent": 0.02,
    "weeklyStopPercent": 0.04,
    "maxSingleEquityPercent": 0.075,
    "maxSingleSpreadPercent": 0.03,
    "maxGrossLeverage": 1.2
  },
  "income": {
    "allocationTargets": {
      "DividendGrowthETF": 0.25,
      "CoveredCallETF": 0.20,
      "BDC": 0.20,
      "EquityREIT": 0.10,
      "MortgageREIT": 0.10,
      "PreferredsIGCredit": 0.10,
      "CashBuffer": 0.05
    },
    "maxIssuerPercent": 0.10,
    "maxCategoryPercent": 0.40
  },
  "tactical": {
    "minADV": 10000000,
    "minPrice": 5,
    "maxSpreadPercent": 0.005,
    "breakoutRSIRange": [45, 65],
    "breakoutVolumeMultiple": 1.5,
    "pullbackRSI2Threshold": 10
  },
  "calendar": {
    "earningsNoTradeDaysBefore": 2,
    "earningsNoTradeDaysAfter": 1,
    "exDivRollDaysBefore": 3
  }
}
```

### 2.5 Security Considerations

**Authentication & Authorization:**
- IBKR: API connection via localhost (TWS/Gateway must be running)
- Claude API: API key stored in Azure Key Vault
- Discord: Webhook URL stored in Azure Key Vault
- Azure: Managed Identity for Function App to access Key Vault and Cosmos DB

**Data Protection:**
- All data at rest encrypted (Cosmos DB default)
- All API calls over HTTPS
- No PII stored (trading data only)
- Credentials never in code or config files

**Operational Security:**
- IBKR TWS/Gateway runs on user's local machine (not cloud)
- Paper trading mode enforced until explicit live switch
- All mode changes require explicit human approval
- Audit log of all trades and configuration changes

### 2.6 Infrastructure & Hosting

**Azure Resources (Serverless/Consumption where possible):**

| Resource | SKU | Estimated Cost |
|----------|-----|----------------|
| Function App | Consumption | ~$0-5/month |
| Cosmos DB | Serverless | ~$5-15/month |
| Key Vault | Standard | ~$1/month |
| Application Insights | Pay-per-use | ~$1-5/month |
| Storage Account | Standard LRS | ~$1/month |
| **Total Azure** | | **~$10-30/month** |

**External Services:**

| Service | Cost | Purpose |
|---------|------|---------|
| Claude API | ~$15-50/month | AI analysis |
| Discord | Free | Notifications |
| IBKR | $0 (waived with activity) | Brokerage |
| **Total External** | | **~$15-50/month** |

**Total Estimated Monthly Cost: $25-80** (target under $50 initially)

---

## Part 3: Risk Management

### 3.1 Technical Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| IBKR API connection drops | Medium | High | Retry logic, alerting, graceful degradation |
| Claude API rate limits/outages | Low | Medium | Cache responses, fallback to rule-only mode |
| Azure Function timeout | Low | Medium | Keep functions under 5 min, async patterns |
| Bad data from IBKR | Low | High | Validation checks, sanity bounds on prices |
| Strategy logic bug | Medium | Critical | Paper trading validation, unit tests, code review |

### 3.2 Financial Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Single large loss | Medium | High | Per-trade risk limit (0.4%), stop losses |
| Consecutive losses | Medium | High | Daily stop (2%), weekly stop (4%) |
| Overconcentration | Low | High | Position caps (7.5% equity, 3% spread), category caps |
| Black swan event | Low | Critical | Max gross leverage (1.2x), diversification |
| Strategy underperformance | Medium | Medium | 12-week paper validation, S&P benchmark comparison |

### 3.3 Operational Risks

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Forgot to run TWS | Medium | Medium | Discord reminder if no connection by 6:15 AM |
| Missed earnings date | Low | High | Polygon.io calendar integration, no-trade enforcement |
| Config mistake | Low | High | Validation on config changes, require confirmation |
| Azure outage | Low | Medium | Alerts, manual intervention capability |

### 3.4 Human Approval Requirements

The following actions **REQUIRE explicit human approval** before proceeding:

1. ⚠️ Switching from SANDBOX to LIVE mode
2. ⚠️ Changing any risk cap or sleeve allocation
3. ⚠️ Trading within no-trade window (earnings) for special reasons
4. ⚠️ Proceeding despite data gaps blocking quality checks
5. ⚠️ Rolling covered calls across ex-dividend dates

**Emergency Stop Behavior (Automatic, No Approval Needed):**
- Daily stop (2% loss): Halt ALL new trades for rest of day, Discord alert
- Weekly stop (4% loss): Halt ALL new trades until manual re-enable, Discord alert

---

## Part 4: Development Phases

### Phase 1: Foundation (Weeks 1-8)

**Goal:** Working system that connects to IBKR paper trading, executes income reinvestment, runs tactical equity scans, and sends Discord reports.

**Exit Criteria:**
- [ ] IBKR paper trading connected and syncing
- [ ] Income monthly reinvest executing correctly
- [ ] Tactical equity scans generating signals
- [ ] Risk limits enforced
- [ ] Daily Discord reports sending
- [ ] All configuration via Cosmos DB (not hardcoded)

#### Week 1-2: IBKR Integration

| Task | Description | Acceptance Criteria |
|------|-------------|---------------------|
| 1.1 | Create IBKR account | Paper trading access confirmed |
| 1.2 | Implement TWS connection | Connect/disconnect without errors |
| 1.3 | Account data sync | Retrieve NAV, cash, buying power |
| 1.4 | Position sync | Retrieve all positions with current prices |
| 1.5 | Quote retrieval | Get real-time quotes for watchlist |
| 1.6 | Historical bars | Retrieve daily OHLCV for indicators |

**PR Test Points (1.1-1.6):**
- [ ] Function App starts without errors
- [ ] TWS connection established within 10 seconds
- [ ] Account data matches TWS display
- [ ] All positions retrieved with correct quantities
- [ ] Quotes within 1% of TWS display
- [ ] Historical bars match TWS chart data

#### Week 3-4: Income Sleeve - Monthly Reinvest

| Task | Description | Acceptance Criteria |
|------|-------------|---------------------|
| 2.1 | Cosmos DB setup | Containers created, CRUD operations work |
| 2.2 | Allocation drift calculator | Accurate drift percentages per category |
| 2.3 | Quality gate checks (basic) | Filter by issuer/category caps |
| 2.4 | Buy list generator | Prioritize underweight categories |
| 2.5 | Limit order placement | Orders placed via IBKR API |
| 2.6 | Paper trade validation | Full monthly cycle completes |

**PR Test Points (2.1-2.6):**
- [ ] Config saved/retrieved from Cosmos DB
- [ ] Drift calculation matches manual spreadsheet
- [ ] Issuer cap violations detected and blocked
- [ ] Buy list prioritizes most underweight category
- [ ] Order appears in TWS pending orders
- [ ] Order fills and position updates

#### Week 5-6: Tactical Sleeve - Equity Scans

| Task | Description | Acceptance Criteria |
|------|-------------|---------------------|
| 3.1 | Technical indicator calculations | RSI, SMA, ATR match reference |
| 3.2 | Breakout scan logic | Identifies qualifying setups |
| 3.3 | Pullback scan logic | Identifies qualifying setups |
| 3.4 | No-trade calendar (Polygon.io) | Earnings dates retrieved |
| 3.5 | Position sizing calculator | Correct shares for risk % |
| 3.6 | Signal generation and storage | Signals saved to Cosmos DB |

**PR Test Points (3.1-3.6):**
- [ ] RSI(14) within 0.5 of TradingView value
- [ ] Breakout candidates match manual scan
- [ ] Pullback candidates match manual scan
- [ ] Known earnings date correctly flags no-trade
- [ ] Position size math matches manual calculation
- [ ] Signals queryable in Cosmos DB

#### Week 7-8: Risk Management & Reporting

| Task | Description | Acceptance Criteria |
|------|-------------|---------------------|
| 4.1 | Per-trade risk validation | Rejects oversized positions |
| 4.2 | Daily/weekly stop tracking | Accurate P&L tracking |
| 4.3 | Emergency halt logic | Stops new trades when triggered |
| 4.4 | Discord webhook integration | Messages send successfully |
| 4.5 | Daily report generation | Comprehensive trade summary |
| 4.6 | Alert notifications | Immediate alerts for stops/errors |

**PR Test Points (4.1-4.6):**
- [ ] Trade exceeding 0.4% risk is rejected
- [ ] Daily P&L matches TWS account page
- [ ] 2% simulated loss triggers halt + alert
- [ ] Discord message appears in channel
- [ ] Report contains all executed trades
- [ ] Alert received within 1 minute of trigger

---

### Phase 2: AI Integration & Options (Weeks 9-14)

**Goal:** Add Claude AI for complex analysis, implement options strategies, create quarterly income audits.

**Exit Criteria:**
- [ ] Claude analyzing market regime daily
- [ ] Claude ranking tactical candidates
- [ ] Quarterly income quality audit with web search
- [ ] CSP and bear call spread execution
- [ ] Covered call overlay on positions

#### Week 9-10: Claude AI Integration

| Task | Description | Acceptance Criteria |
|------|-------------|---------------------|
| 5.1 | Claude API service | Successful API calls |
| 5.2 | Market regime analysis | JSON response parsed correctly |
| 5.3 | Tactical candidate ranking | Top 5 candidates prioritized |
| 5.4 | Cost tracking | API spend logged and monitored |
| 5.5 | Fallback handling | System works if Claude unavailable |

**PR Test Points (5.1-5.5):**
- [ ] Claude API call succeeds with valid response
- [ ] Market regime correctly identified (verify manually)
- [ ] Candidate ranking seems reasonable
- [ ] API cost appears in logs
- [ ] System runs (rule-only mode) if Claude times out

#### Week 11-12: Income Quality Audits

| Task | Description | Acceptance Criteria |
|------|-------------|---------------------|
| 6.1 | Quarterly audit trigger | Runs first week of quarter |
| 6.2 | Claude web search for metrics | NII, FFO, ROC data retrieved |
| 6.3 | Quality assessment | Red/yellow/green ratings |
| 6.4 | Reduction signal generation | Signals for problem securities |
| 6.5 | Audit report via Discord | Comprehensive quality summary |

**PR Test Points (6.1-6.5):**
- [ ] Audit runs on correct schedule
- [ ] Claude finds relevant quality data
- [ ] Ratings match manual assessment (spot check)
- [ ] Reduction signals generated for flagged securities
- [ ] Audit report readable and actionable

#### Week 13-14: Options Strategies

<!-- TODO: Options IV data source decision
     WHEN: Before implementing options strategies (Week 13)
     BLOCKER: CSP and bear call spread implementation
     OPTIONS: Calculate from IBKR historical data (chosen for now)
-->

| Task | Description | Acceptance Criteria |
|------|-------------|---------------------|
| 7.1 | Options chain retrieval | Full chain from IBKR |
| 7.2 | IV calculation from historical | IV Rank/Percentile computed |
| 7.3 | CSP strategy implementation | Identifies opportunities |
| 7.4 | Bear call spread implementation | Identifies opportunities |
| 7.5 | Options order placement | Spread orders execute |
| 7.6 | Covered call overlay | Writes calls on eligible positions |

**PR Test Points (7.1-7.6):**
- [ ] Options chain matches TWS display
- [ ] IV Rank within 10% of external source
- [ ] CSP candidates meet all criteria
- [ ] Bear call spread legs correct
- [ ] Spread order fills as combo
- [ ] Covered call written only on eligible shares

---

### Phase 3: Validation & Go-Live (Weeks 15-28+)

**Goal:** 12+ weeks of paper trading validation, performance analysis, and careful transition to live trading.

**Exit Criteria:**
- [ ] 12 weeks paper trading complete
- [ ] Profitable OR outperformed S&P 500
- [ ] All success metrics met or understood
- [ ] Human approval for live trading
- [ ] Live trading pilot (reduced capital) successful

#### Week 15-26: Paper Trading Validation

| Task | Description | Acceptance Criteria |
|------|-------------|---------------------|
| 8.1 | Daily monitoring | Check reports, address issues |
| 8.2 | Weekly performance review | Track vs S&P 500 benchmark |
| 8.3 | Strategy tuning | Adjust parameters if needed |
| 8.4 | Bug fixes | Address any issues found |
| 8.5 | 12-week assessment | Formal go/no-go evaluation |

#### Week 27-28: Live Transition (If Approved)

| Task | Description | Acceptance Criteria |
|------|-------------|---------------------|
| 9.1 | Fund IBKR live account | ~$100k deposited |
| 9.2 | Configuration for live mode | All settings reviewed |
| 9.3 | Switch to LIVE mode | Human approval obtained |
| 9.4 | Pilot period (reduced risk) | Lower position sizes initially |
| 9.5 | Full deployment | Normal parameters restored |

---

## Part 5: Human Actions Required

### Before Development Starts

| Action | Description | Deadline |
|--------|-------------|----------|
| ⚠️ Create IBKR account | Sign up at interactivebrokers.com, enable paper trading | Week 1 Day 1 |
| ⚠️ Get Claude API key | Sign up at console.anthropic.com, create API key | Week 1 Day 1 |
| ⚠️ Create Discord server/channel | Set up trading-bot channel, create webhook | Week 1 Day 1 |
| ⚠️ Sign up for Polygon.io | Create account, get API key ($29/month) | Week 5 |

### During Development

| Action | Description | When |
|--------|-------------|------|
| Install TWS on Windows PC | Download and configure Trader Workstation | Week 1 |
| Configure TWS API settings | Enable API connections, set port 7497 | Week 1 |
| Review and merge PRs | Code review before merging to main | Ongoing |
| Provide credentials when asked | API keys to store in .env | As needed |
| Test paper trading manually | Verify trades appear correctly in TWS | End of each phase |

### Ongoing Operations

| Action | Frequency | Description |
|--------|-----------|-------------|
| Check Discord daily | Daily | Review reports and alerts |
| Ensure TWS is running | Trading days | System needs TWS connection |
| Review weekly performance | Weekly | Compare vs benchmarks |
| Approve any configuration changes | As needed | Required for risk parameter changes |
| Quarterly quality audit review | Quarterly | Review Claude's security assessments |

---

## Part 6: Resources & References

### API Documentation
- [IBKR TWS API](https://interactivebrokers.github.io/tws-api/)
- [Claude API](https://docs.anthropic.com/en/api)
- [Polygon.io API](https://polygon.io/docs)
- [Discord Webhooks](https://discord.com/developers/docs/resources/webhook)

### Azure Documentation
- [Azure Functions](https://docs.microsoft.com/azure/azure-functions/)
- [Cosmos DB](https://docs.microsoft.com/azure/cosmos-db/)
- [Key Vault](https://docs.microsoft.com/azure/key-vault/)

### Trading References
- [TastyTrade Options Education](https://www.tastytrade.com/learn)
- [Investopedia Technical Analysis](https://www.investopedia.com/technical-analysis-4689657)

### Project Templates
- CLAUDE.md - Development orchestration
- PROGRESS.md - Session continuity
- DECISIONS.md - Architectural decisions

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

*Document Version: 1.0*  
*Last Updated: 2025-02-04*  
*Authors: Laurance, Claude*
