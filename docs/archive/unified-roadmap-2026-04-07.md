# Unified Trading System Roadmap (Revised 2026-04-07)

## Context

OptiTrade consolidated into TradingSystem. Iron condor backtesting proved the strategy barely breaks even on SPY (+0.07% CAGR) and QC can't reliably simulate SPX (market order fills on illiquid index options). Research spike (April 2026) identified validated patterns, tools, and approaches from the AI trading community that should be incorporated.

## Completed (this session)
- [x] OptiTrade archived (tagged v1.0-archive)
- [x] Backtest pipeline migrated to TradingSystem tools/backtest/
- [x] 3 ADRs recorded (consolidation, dual-mode, iron condor findings)
- [x] Claude regime detection wired with rule-based fallback
- [x] SPX backtest completed and analyzed (QC unsuitable for index options)
- [x] Research spike on AI trading strategies 2026

---

## Phase 1: Foundation Completion (Sprint 1 — 1 week)

**Goal:** Close out Week 10, merge consolidation branch, wire gateway integration.

| Item | Status |
|------|--------|
| PR and merge `feature/consolidate-optitrade` (4 commits) | Ready |
| Route AI calls through Claude Gateway (localhost:3131), fall back to direct API | To do |
| Add Claude API key from Bitwarden to TradingSystem local config | To do |
| Set up Discord server/channel, configure webhook URL | **User action needed** |
| End-to-end orchestrator smoke test in paper mode | To do |
| Gate: build clean, 418+ tests pass, orchestrator runs E2E | |

---

## Phase 2A: Strategy Backtests on SPY (Sprint 2 — 1 week)

**Goal:** Establish per-strategy baselines on QC for strategies that work with SPY options data.

SPX is off the table for QC backtesting (proven unreliable). SPY options are liquid and QC data is good. Run 2-3 targeted backtests:

| Backtest | Rationale |
|----------|-----------|
| Bull put spreads on SPY | Higher frequency than iron condors (bullish/neutral regime only) |
| Covered calls on SPY | Tests premium capture on underlying holdings |
| CSPs on SPY | Tests put-selling expectancy, willing to take assignment |

Each ~60-90 min automated. Any strategy with CAGR > 0% and IS/OOS <= 3pp gets enabled. Failures get disabled.

**Gate:** Per-strategy pass/fail recorded. Enabled strategy list locked for paper validation.

---

## Phase 2B: AI Integration + Research Patterns (Sprint 3 — 1-2 weeks)

**Goal:** Incorporate validated 2026 AI patterns into the system before paper validation.

### Must-do (validated by research)

| Item | Source | What |
|------|--------|------|
| Install QuantConnect MCP server | taylorwilsdon/quantconnect-mcp | Claude iterates backtests via natural language — productivity multiplier |
| Add GEX/gamma exposure as regime signal | aiflowtrader.com pattern | Positive gamma → premium selling safe. Negative gamma → sit out. More actionable than VIX alone for options |
| Wire Claude as conviction filter | Community consensus pattern | Traditional strategy generates signal → Claude scores conviction 0-1.0 → suppress below 0.3. Not a signal generator |
| Install IBKR MCP server (read-only) | Hellek1/ib-mcp or equivalent | Gives Claude visibility into positions/P&L for analysis, not execution |

### Evaluate (promising but lower priority)

| Item | Source | What |
|------|--------|------|
| claude-trading-skills library | tradermonty/claude-trading-skills | 50 skills including Options Advisor, Regime Detector, Position Sizer. Evaluate which are useful |
| Self-improving prompts | Karpathy/ATLAS pattern | Treat regime prompts as optimizable parameters, evolve winners. Evaluate during paper validation |
| Multi-agent debate | TradingAgents pattern | Bull/bear agents argue before final decision. Evaluate for weekly frequency |

### Architecture decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| AI call routing | Gateway first (CLI/subscription), fall back to direct API | Cost efficiency, centralized model config |
| AI role | Conviction filter + regime detection | Community consensus: don't predict direction, filter and score |
| Model flexibility | Configurable model, abstract interface | Next-gen frontier model expected ~2-3 months |
| Options regime signal | GEX + VIX combined | GEX is more actionable for options than VIX alone |

**Gate:** Gateway integration tested. GEX signal wired into regime detection. Conviction filter implemented. MCP servers installed and verified.

---

## Phase 3: Paper Validation (12+ weeks, runs in background)

**Goal:** Full system running autonomously in IBKR paper mode.

- Both sleeves active, all enabled strategies running simultaneously
- Regime detection (Claude via gateway + GEX + VIX rules) drives strategy selection
- Conviction filter scores every entry signal
- Risk engine enforces all limits
- Discord alerts for stops, regime changes, daily summaries
- Self-improving prompt evaluation (compare prompt variants for regime detection)

**Configuration for Mode A (alpha-seeking):**
- Sleeve allocation: 50/50 or configurable
- Options sleeve: all strategies that passed QC baseline
- Income sleeve: total return focus (growth + yield), not pure yield
- Position sizing: aggressive within risk limits

**Gate:** 12+ weeks continuous operation, zero hard risk-limit violations, risk telemetry complete, sleeve-level readiness scorecards.

---

## Phase 4: Live Transition (4+ weeks staged)

- Pre-live: runbook, emergency close-all tested, monitoring verified
- Week 1: minimal positions. Weeks 2-4: staged scaling.
- Human approval at each step.

---

## Phase 5: Stabilization (3+ months)

- Monthly performance vs SPY benchmark
- AI ablation study (regime detection on vs off)
- Prompt optimization based on live outcomes
- Revisit model when next-gen frontier model available (~June-July 2026)

---

## Dual-Mode Strategy

### Mode A: Alpha-Seeking (build first, next ~5 years)
- Beat SPY on total return
- Options sleeve is primary alpha engine
- Higher allocation to options (50/50 or 30/70 income/options)
- Aggressive within risk limits

### Mode B: Income + Protection (retirement, 5+ years out)
- 5-8% consistent yield, drawdown protection
- Income sleeve primary, options sleeve conservative
- 70/30 income/options

Both use the same infrastructure, different configuration profiles.

---

## Sprint-Ready Items

**Sprint 1 (next):** Phase 1 Foundation Completion
- Merge consolidation PR
- Gateway integration (ClaudeService → try gateway, fall back to API)
- Claude API key in local config
- Discord setup (user action)
- Smoke test

**Sprint 2:** Phase 2A SPY backtests (bull puts, covered calls, CSPs)

**Sprint 3:** Phase 2B AI integration (QC MCP, GEX regime signal, conviction filter, IBKR MCP)

**Sprint 4+:** Phase 3 paper validation launch and monitoring
