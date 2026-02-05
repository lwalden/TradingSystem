# Development Roadmap

## Phase 1: Foundation (Weeks 1-8)

### Phase 1A: IBKR Integration ✓ Scaffold Complete
**Goal**: Connect to IBKR and sync account data

**Tasks**:
- [ ] Set up IBKR paper trading account
- [ ] Implement TWS API connection (EClientSocket)
- [ ] Implement account data retrieval (reqAccountSummary)
- [ ] Implement position sync (reqPositions)
- [ ] Implement quote retrieval (reqMktData)
- [ ] Implement historical data (reqHistoricalData)
- [ ] Test connection stability and error handling

**Deliverables**:
- Working IBKR connection
- Account/position sync
- Basic quote retrieval

### Phase 1B: Income Sleeve - Monthly Reinvest
**Goal**: Automate monthly dividend reinvestment

**Tasks**:
- [ ] Implement Cosmos DB repositories
- [ ] Build allocation drift calculator
- [ ] Implement quality gate checks (basic rules)
- [ ] Create buy-list generator
- [ ] Implement limit order placement
- [ ] Test with paper trading

**Deliverables**:
- Monthly reinvest function working
- Proper position tracking in Cosmos DB
- Basic logging/audit trail

### Phase 1C: Tactical Sleeve - Equity Scans
**Goal**: Scan for breakout/pullback candidates

**Tasks**:
- [ ] Implement technical indicator calculations
- [ ] Build breakout scan logic
- [ ] Build pullback scan logic
- [ ] Implement no-trade calendar (earnings)
- [ ] Implement position sizing
- [ ] Test equity order execution

**Deliverables**:
- Daily tactical scans
- Signal generation
- Paper trade execution

### Phase 1D: Claude AI Integration
**Goal**: Add AI analysis for complex decisions

**Tasks**:
- [ ] Implement Claude API service
- [ ] Create market regime analysis prompt
- [ ] Create tactical candidate ranking prompt
- [ ] Integrate with daily orchestrator
- [ ] Add cost tracking for API usage

**Deliverables**:
- Working Claude integration
- Market regime assessment
- Candidate ranking

## Phase 2: Options & Reporting (Weeks 9-12)

### Phase 2A: Options Strategies
**Tasks**:
- [ ] Implement options chain retrieval from IBKR
- [ ] Calculate IV rank/percentile
- [ ] Implement CSP strategy
- [ ] Implement bear call spread strategy
- [ ] Implement covered call overlay

### Phase 2B: Daily Reporting
**Tasks**:
- [ ] Build daily snapshot service
- [ ] Create daily report template
- [ ] Implement email/Teams notification
- [ ] Add performance metrics dashboard

## Phase 3: Backtesting (Weeks 13-16)

### Phase 3A: Historical Data Pipeline
**Tasks**:
- [ ] Source historical price data
- [ ] Source historical options data (challenging)
- [ ] Build data storage layer
- [ ] Implement data loading utilities

### Phase 3B: Backtest Engine
**Tasks**:
- [ ] Build event-driven backtest framework
- [ ] Implement realistic fill simulation
- [ ] Add slippage and commission modeling
- [ ] Create performance metrics calculator
- [ ] Build strategy comparison tools

## Phase 4: Production (Weeks 17-20)

### Phase 4A: Hardening
**Tasks**:
- [ ] Comprehensive error handling
- [ ] Circuit breakers and rate limiting
- [ ] Emergency stop mechanism
- [ ] Monitoring and alerting
- [ ] Security audit

### Phase 4B: Go-Live
**Tasks**:
- [ ] Paper trading validation (4+ weeks)
- [ ] Performance review vs thresholds
- [ ] 50% capital pilot
- [ ] Full deployment

---

## Success Criteria

### Before Paper Trading
- [ ] All unit tests passing
- [ ] Manual testing of all functions
- [ ] Error handling validated
- [ ] Logging comprehensive

### Before Live Trading
- [ ] 4+ weeks paper trading
- [ ] Profit Factor ≥ 1.2
- [ ] Max Drawdown < 5%
- [ ] No major bugs in paper trading
- [ ] Owner review and approval

### Ongoing Metrics
- CAGR ≥ 8%
- MaxDD ≤ 15%
- MAR ≥ 0.5
- Tactical hit rate ≥ 45%
- Tactical profit factor ≥ 1.3

---

## Time Estimates

| Phase | Estimated Hours | Calendar Weeks |
|-------|-----------------|----------------|
| 1A    | 10-15          | 2              |
| 1B    | 10-15          | 2              |
| 1C    | 12-15          | 2              |
| 1D    | 10-12          | 2              |
| 2A    | 15-20          | 2              |
| 2B    | 8-10           | 1              |
| 3A    | 10-12          | 2              |
| 3B    | 15-20          | 2              |
| 4A    | 10-15          | 2              |
| 4B    | 5-10           | 4 (paper)      |

**Total**: ~100-140 hours over ~20 weeks
