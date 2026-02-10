# DECISIONS.md - Architectural Decision Record

> **Purpose:** Track architectural and design decisions to avoid re-debating across sessions.
> **Format:** Each decision has context, options considered, decision made, and rationale.

---

## How to Use This File

When a significant decision is made during development, add it here with:
1. **Context** - What problem/question arose
2. **Options** - What alternatives were considered
3. **Decision** - What was chosen
4. **Rationale** - Why this choice was made
5. **Date** - When the decision was made

---

## Decisions

### ADR-001: Brokerage Platform Selection
**Date:** 2025-02-04
**Status:** Decided

**Context:** 
Need a brokerage with API access for automated trading, supporting both equities and options, with paper trading capability.

**Options Considered:**
1. **Alpaca** - Free, excellent API, easy to use
   - Pros: Modern REST API, free paper trading, great documentation
   - Cons: No options trading support via API
2. **E-Trade** - User already has account
   - Pros: Existing relationship
   - Cons: Older API, less documentation
3. **Interactive Brokers** - Institutional-grade
   - Pros: Full options support, paper trading, comprehensive API, industry standard
   - Cons: Complex API, steeper learning curve
4. **Tradier** - Options-focused
   - Pros: Simpler API than IBKR, good options support
   - Cons: Less comprehensive data, smaller ecosystem

**Decision:** 
Interactive Brokers (IBKR)

**Rationale:**
- Full options trading support required for tactical sleeve (CSPs, bear call spreads, covered calls)
- Free paper trading for extended validation
- Institutional-grade reliability for real money
- Comprehensive market data included
- Despite complexity, long-term benefit outweighs learning curve

**Consequences:**
- Need to create new IBKR account
- Must run TWS or IB Gateway on local machine
- More complex API integration than Alpaca

---

### ADR-002: Cloud Platform Selection
**Date:** 2025-02-04
**Status:** Decided

**Context:** 
Need cloud hosting for orchestration, data storage, and monitoring.

**Options Considered:**
1. **Azure** - Microsoft cloud
   - Pros: Laurance has strong Azure expertise, existing subscription, .NET integration excellent
   - Cons: Can be expensive if not careful
2. **AWS** - Amazon cloud
   - Pros: Market leader, comprehensive services
   - Cons: Less .NET-native, learning curve
3. **Local server** - Run on home machine
   - Pros: Free, full control
   - Cons: Reliability concerns, no redundancy, machine must be on

**Decision:** 
Azure (Functions, Cosmos DB, Key Vault, Application Insights)

**Rationale:**
- Laurance's existing expertise reduces development time
- Existing Azure subscription available
- Serverless consumption model keeps costs low
- Excellent .NET 8 support
- Built-in monitoring and alerting

**Consequences:**
- Monthly cost ~$20-40 for hosting
- Need to manage Azure resources
- Deployment via Bicep templates

---

### ADR-003: AI Analysis Approach
**Date:** 2025-02-04
**Status:** Decided

**Context:** 
Determining when and how to use Claude AI in the trading system.

**Options Considered:**
1. **Claude for all decisions** - AI makes all trading decisions
   - Pros: Maximum AI leverage
   - Cons: Expensive, latency issues, AI hallucination risk in critical paths
2. **Rules only, no AI** - Pure algorithmic approach
   - Pros: Deterministic, no API costs, fast
   - Cons: Misses nuanced analysis opportunities
3. **Hybrid approach** - Rules for execution, AI for analysis
   - Pros: Best of both worlds, cost-effective, AI for judgment where it adds value
   - Cons: More complex architecture

**Decision:** 
Hybrid approach - Claude AI for complex analysis, rule-based execution

**Rationale:**
- AI excels at nuanced analysis (market regime, quality assessment, candidate ranking)
- Rules are better for execution (deterministic, no hallucination risk)
- Cost-effective: only ~$15-50/month for periodic analysis
- Fallback to rule-only mode if Claude unavailable

**Where Claude is used:**
- Daily market regime assessment
- Tactical candidate ranking
- Quarterly income quality audits (with web search)

**Where rules are used:**
- Order execution
- Position sizing
- Stop loss enforcement
- Drift calculation
- Technical indicator signals

**Consequences:**
- Two parallel paths in strategy evaluation
- Need fallback handling if Claude API fails
- API costs scale with analysis frequency

---

### ADR-004: Notification System
**Date:** 2025-02-04
**Status:** Decided

**Context:** 
Need to notify human of daily reports, alerts, and emergency conditions.

**Options Considered:**
1. **Email** - Traditional approach
   - Pros: Universal, archivable
   - Cons: Can be slow, often ignored
2. **SMS/Text** - Direct mobile
   - Pros: Immediate attention
   - Cons: Cost per message, limited formatting
3. **Discord** - Chat platform
   - Pros: Free, rich formatting, mobile app, webhooks easy to implement
   - Cons: Requires Discord setup
4. **Microsoft Teams** - Enterprise chat
   - Pros: Professional
   - Cons: More complex webhook setup

**Decision:** 
Discord webhooks

**Rationale:**
- Free with no message limits
- Rich embed formatting for reports
- Mobile notifications via Discord app
- Simple webhook implementation
- Can create multiple channels (alerts vs daily reports)

**Consequences:**
- Human needs to create Discord server and webhook
- Reports formatted as Discord embeds

---

### ADR-005: Earnings Calendar Data Source
**Date:** 2025-02-04
**Status:** Decided

**Context:** 
Need earnings dates to enforce no-trade windows around earnings announcements.

**Options Considered:**
1. **Polygon.io** - Financial data API
   - Pros: Reliable, official data, reasonable cost ($29/month)
   - Cons: Additional monthly cost
2. **Alpha Vantage** - Free tier available
   - Pros: Free
   - Cons: Rate limits, may be unreliable
3. **Web scraping** - Scrape Yahoo Finance
   - Pros: Free
   - Cons: Fragile, may break, terms of service concerns
4. **Manual input** - User maintains spreadsheet
   - Pros: Free, accurate
   - Cons: Labor intensive, easy to miss

**Decision:** 
Polygon.io ($29/month)

**Rationale:**
- Earnings timing is critical for avoiding costly surprises
- Reliability worth the $29/month
- Professional-grade data
- Simple REST API

**Consequences:**
- $29/month additional cost
- Human needs to sign up for Polygon.io account

---

### ADR-006: Options IV Data Source
**Date:** 2025-02-04
**Status:** Decided (with TODO for implementation details)

**Context:** 
Options strategies require IV Rank and IV Percentile, which aren't directly available from IBKR.

**Options Considered:**
1. **Calculate from IBKR historical volatility** - DIY approach
   - Pros: Free (included with IBKR data), no additional service
   - Cons: More development work, need historical IV data
2. **Third-party service (Tradier, TastyTrade)** - Pre-calculated
   - Pros: Simpler implementation
   - Cons: Additional cost, another dependency
3. **Defer to Phase 2** - Start equity-only
   - Pros: Faster Phase 1
   - Cons: Delays options strategies

**Decision:** 
Calculate from IBKR historical volatility data

**Rationale:**
- Avoids additional monthly costs
- IBKR provides historical data
- More control over calculation methodology
- Aligns with budget constraints

**Consequences:**
- Need to implement IV Rank/Percentile calculation
- Need to store historical IV data
- Implementation in Phase 2 (Week 13-14)

<!-- TODO: Implementation details for IV calculation
     WHEN: Week 13 when implementing options strategies
     BLOCKER: CSP and bear call spread strategies
     OPTIONS: Consider whether 1-year lookback is sufficient for IV Rank
-->

---

### ADR-007: Income Quality Data Source
**Date:** 2025-02-04
**Status:** Decided

**Context:** 
Quarterly income audits need quality metrics (NII coverage for BDCs, FFO/AFFO for REITs, ROC breakdown for ETFs) that aren't available via standard market data APIs.

**Options Considered:**
1. **Claude web search** - AI searches for recent data
   - Pros: Free, can find current data, human-readable summaries
   - Cons: May not find all data, less structured
2. **Manual spreadsheet** - Human maintains data
   - Pros: Accurate, controlled
   - Cons: Labor intensive, delays audits
3. **Commercial data provider** - Bloomberg, FactSet
   - Pros: Comprehensive, reliable
   - Cons: Very expensive ($1000s/month)
4. **Skip automated gates** - Just generate alerts
   - Pros: Simpler
   - Cons: Less proactive risk management

**Decision:** 
Claude web search during quarterly audits

**Rationale:**
- Quarterly frequency means latency is acceptable
- Claude can synthesize information from multiple sources
- Free (part of existing Claude API budget)
- Can be supplemented with manual checks if needed
- "Good enough" for quarterly review cadence

**Consequences:**
- Quality data may not be 100% accurate - human should verify
- Audit reports should clearly cite sources
- Claude may not find data for smaller issuers

---

### ADR-008: Git Workflow
**Date:** 2025-02-04
**Status:** Decided

**Context:** 
Establishing how code changes flow from development to the repository.

**Decision:**
- **Branch Strategy:** Feature branches off main
- **Branch Naming:** `feature/short-description`, `fix/short-description`
- **PR Policy:** All changes via PR, human reviews and merges
- **Commit Style:** Descriptive messages, frequent commits

**Rationale:**
- Feature branches isolate work in progress
- PR reviews ensure code quality and human oversight
- Human merge control prevents accidental deployments

---

### ADR-009: Risk Parameter Defaults
**Date:** 2025-02-04
**Status:** Decided

**Context:** 
Setting initial risk parameters for the trading system.

**Decision:**
| Parameter | Value | Strategy Doc Default |
|-----------|-------|---------------------|
| Per-trade risk | 0.4% | 0.4% ✓ |
| Daily stop | 2% | 2% ✓ |
| Weekly stop | 4% | 4% ✓ |
| Max single equity | **7.5%** | 5% (changed) |
| Max single spread | **3%** | 2% (changed) |
| Max gross leverage | 1.2x | 1.2x ✓ |
| Issuer cap | 10% | 10% ✓ |
| Category cap | 40% | 40% ✓ |

**Rationale:**
- Per-trade, daily, weekly stops kept conservative per strategy doc
- Single equity increased to 7.5% to allow more concentrated income positions
- Single spread increased to 3% for better options position sizing
- Leverage kept low for capital preservation focus

**Consequences:**
- These are initial values; can be tuned based on paper trading results
- Any changes require human approval

---

### ADR-010: Paper Trading Validation Criteria
**Date:** 2025-02-04
**Status:** Decided

**Context:** 
Defining success criteria for paper trading before considering live deployment.

**Decision:**
- Minimum duration: 12 weeks
- Success threshold: Profitable OR outperform S&P 500 for the same period
- Additional metrics to track (informational, not gates):
  - Tactical hit rate ≥ 45%
  - Tactical profit factor ≥ 1.3
  - Max drawdown ≤ 15%

**Rationale:**
- 12 weeks provides reasonable sample size across market conditions
- "OR S&P 500" allows for validation even in down markets
- Conservative approach aligned with capital preservation goals

**Consequences:**
- Live trading earliest at Week 27 (15 + 12)
- Need to track S&P 500 benchmark alongside portfolio
- Human approval still required after criteria met

---

## Pending Decisions

### PDR-001: Intraday vs Daily Execution
**Date Added:** 2025-02-04
**Context:** Currently designing for daily batch execution (pre-market scan, EOD processing). Should we support intraday execution?

**Options to Consider:**
1. Daily batch only (current plan)
2. Intraday monitoring with immediate execution
3. Hybrid (daily for income, intraday for tactical)

**Considerations:**
- Azure Functions consumption plan has cold start latency
- IBKR connection needs to be maintained for intraday
- Cost implications of always-on infrastructure
- Complexity of real-time event handling

**Status:** Deferred to Phase 2 evaluation - start with daily batch, assess need based on paper trading results

---

### PDR-002: Backtesting Engine Scope
**Date Added:** 2025-02-04
**Context:** Backtesting mentioned in Phase 3. Need to define scope.

**Options to Consider:**
1. Simple: Replay historical data through strategies
2. Medium: Add realistic fills, slippage, commissions
3. Full: Event-driven backtester with optimization

**Considerations:**
- Historical options data is expensive/hard to get
- Overfitting risk with optimization
- Time investment vs value

**Status:** Defer until Phase 3 planning

---

*This file should be updated whenever significant architectural or design decisions are made.*
