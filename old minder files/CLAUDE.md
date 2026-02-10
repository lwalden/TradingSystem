# CLAUDE.md - Trading System Development Orchestration

> **Purpose:** This file instructs Claude Code on how to work on this project.
> Claude should read this file first when starting work on the repository.

## Reference Documents

- **Strategy & Roadmap:** See `/docs/strategy-roadmap.md` for full business context, goals, and technical architecture decisions.
- **Progress Tracking:** See `/PROGRESS.md` for current development state and session continuity
- **Decision Log:** See `/DECISIONS.md` for architectural decisions made

---

## Session Resume Protocol

**IMPORTANT: At the start of EVERY new session, follow these steps:**

1. **Read `/PROGRESS.md` first** - This tells you current phase, completed tasks, blockers, and what to do next
2. **Check `/DECISIONS.md`** - Review any architectural decisions so you don't re-ask resolved questions
3. **Run `git status`** - Check for any uncommitted work from previous sessions
4. **Run `gh pr list`** - Check for any open PRs awaiting review
5. **Check the "Blocked / Awaiting Human Action" section** - Ask the user about any items pending their input
6. **Resume from "Next Session Should" in PROGRESS.md**

**At the END of every session:**
1. Update `/PROGRESS.md` with:
   - Any completed tasks (add to Completed Tasks table)
   - Any new blockers (add to Blocked section)
   - What the next session should focus on
   - A brief session summary
2. Update `/DECISIONS.md` if any architectural decisions were made
3. Commit progress tracking changes if meaningful work was done

**When resuming and the user says "continue where we left off":**
- Read PROGRESS.md
- Summarize the current state briefly
- Ask about any blocked items
- Propose next steps based on "Next Session Should"

**Mid-Session Token Cap Handling:**

Sessions may end unexpectedly when Claude Pro token limits are reached. To minimize lost work:

1. **Write code to files immediately** - Don't accumulate large changes in memory; write each file as it's completed
2. **Commit at natural checkpoints:**
   - After solution/project compiles successfully
   - After tests pass
   - After completing a logical unit of work (one task/feature)
3. **Update PROGRESS.md before multi-step operations** - If starting a complex task, note what you're about to do
4. **Prefer smaller, frequent commits** over one large commit at the end

**If session ends mid-task:**
- Files already written to disk are preserved
- Uncommitted changes in staged files are preserved
- Conversation context is NOT preserved (but can be scrolled back in the UI)
- Next session: run `git status` and `git diff` to see partial work, then continue

---

## Project Overview

**Project Name:** Automated Trading System
**Description:** A fully automated trading system managing a two-sleeve portfolio (70% income, 30% tactical) via Interactive Brokers API, with Claude AI for complex analysis decisions.

**Human Developer Profile:**
- Senior Software Engineer with 12 years C#/.NET experience
- Strong Azure cloud services expertise
- Available ~2 hours/day, prefers afternoon sessions (until 4 PM PT)
- Risk-aware, values capital preservation
- Located in Pacific Time zone

**Communication Preferences:**
- Provide TL;DR summary first, then detailed explanation
- Assume always available when working (no need to batch questions)
- Risk tolerance: Conservative
- Notifications via Discord

---

## Your Capabilities & Boundaries

### You CAN:
- Create files and directory structures
- Search the internet for documentation, APIs, tutorials
- Install packages (NuGet, npm, etc.)
- Use CLI and bash commands
- Create branches and pull requests in GitHub
- Create sub-agents to parallelize work
- Scaffold code, write implementations, create tests
- Run tests and fix failing tests
- Manage dependencies and package versions

### You SHOULD ASK the human to:
- Create new GitHub repositories
- Merge pull requests
- Provide API keys and credentials (IBKR, Claude API, Polygon.io, Discord webhook)
- Make billing/payment decisions
- Approve major architectural changes
- Review and approve PRs before merge
- **Switch from SANDBOX to LIVE trading mode**
- **Change any risk parameters or sleeve allocations**
- **Trade within no-trade windows for special reasons**

### Credential Handling:
- NEVER store credentials in code files
- Use environment variables, .env files, or Azure Key Vault
- When you need a credential, ask: "Please provide your [SERVICE] API key. I'll store it in .env (gitignored) for local development."
- Create `.env.example` files showing required variables WITHOUT values

### Git Workflow:
- **NEVER commit directly to main** - Always use feature branches
- Branch naming: `feature/short-description` or `fix/short-description`
- Claude CAN: create branches, commit, push to remote, open PRs via `gh pr create`
- Claude CANNOT: merge PRs to main (user does this manually)
- After user merges PR: switch back to main, pull, then start next feature branch

**Typical flow:**
```bash
git checkout -b feature/my-feature
# ... make changes, commit frequently ...
git push -u origin feature/my-feature
gh pr create --title "Add my feature" --body "..."
# Wait for user to review and merge
# User says "merged" → git checkout main && git pull
```

### Pull Request Format:
- PRs should include:
  - Clear title describing the change
  - Summary of what was changed and why
  - Test plan (manual testing steps or automated test references)
  - Any follow-up items or known limitations
- Keep PRs reasonably sized - prefer multiple smaller PRs over one massive PR
- After PR is merged, update PROGRESS.md with completed task

### Test Plan Format:
```markdown
## Test Plan
- [ ] Step 1: Description of what to test
- [ ] Step 2: Expected outcome
- [ ] Verify: Specific verification steps

**Prerequisites:** TWS running on port 7497, paper trading enabled
**Environment:** Local / Azure (Sandbox mode)
```

---

## Technology Stack

```
Backend:        C# / .NET 8
Orchestration:  Azure Functions (Consumption, Isolated Worker)
Database:       Azure Cosmos DB (Serverless)
Secrets:        Azure Key Vault
Monitoring:     Application Insights
Broker API:     Interactive Brokers TWS API
AI Analysis:    Claude API (Sonnet)
Notifications:  Discord Webhooks
Data:           Polygon.io (earnings calendar)
Testing:        xUnit, Moq
CI/CD:          GitHub Actions (future)
```

---

## Project Structure

```
/TradingSystem
├── /src
│   ├── /TradingSystem.Core           # Domain models, interfaces, configuration
│   │   ├── /Models                   # Position, Trade, Signal, Order, etc.
│   │   ├── /Interfaces               # IBrokerService, IStrategy, IRiskManager
│   │   └── /Configuration            # TradingSystemConfig, IncomeUniverse
│   ├── /TradingSystem.Functions      # Azure Functions orchestration
│   ├── /TradingSystem.Brokers.IBKR   # Interactive Brokers implementation
│   ├── /TradingSystem.Strategies     # Strategy implementations
│   │   ├── /Income                   # MonthlyReinvest, QuarterlyAudit
│   │   ├── /Tactical                 # Breakout, Pullback, Options
│   │   └── /Common                   # Base classes
│   └── /TradingSystem.AI             # Claude API integration
├── /tests
│   └── /TradingSystem.Tests          # Unit and integration tests
├── /docs
│   └── strategy-roadmap.md           # Business context and roadmap
├── /infrastructure
│   └── main.bicep                    # Azure IaC templates
├── /config                           # Configuration templates
├── .env.example                      # Environment variable template
├── .gitignore
├── CLAUDE.md                         # This file
├── PROGRESS.md                       # Session continuity tracking
├── DECISIONS.md                      # Architectural decisions
├── README.md
└── TradingSystem.sln
```

---

## Development Phases

### PHASE 1: Foundation (Weeks 1-8)
**Goal:** Working system with IBKR connection, income reinvest, tactical scans, risk management, Discord reports
**Duration:** 8 weeks at ~2 hrs/day

### PHASE 2: AI & Options (Weeks 9-14)
**Goal:** Claude AI integration, quarterly audits, options strategies (CSP, bear call spreads, covered calls)
**Duration:** 6 weeks

### PHASE 3: Validation & Go-Live (Weeks 15-28+)
**Goal:** 12+ weeks paper trading, performance analysis, live transition (if approved)
**Duration:** 14+ weeks

---

## Phase 1 Detailed Tasks

### Week 1-2: IBKR Integration

#### Task 1.1: IBKR Account Setup
```
STATUS: NOT STARTED
REQUIRES HUMAN: Yes - account creation

PROMPT TO HUMAN:
"Please create an Interactive Brokers account:
1. Go to https://www.interactivebrokers.com
2. Sign up for an individual account
3. Enable paper trading
4. Download and install Trader Workstation (TWS)
5. Configure API: File → Global Configuration → API → Settings
   - Enable ActiveX and Socket Clients
   - Socket port: 7497 (paper)
   - Allow connections from localhost only
6. Let me know when TWS is running and ready"
```

#### Task 1.2: TWS API Connection
```
STATUS: NOT STARTED
REQUIRES HUMAN: No

Execute:
- Implement EClientSocket connection
- Handle connection events (connect, disconnect, error)
- Implement reconnection logic
- Add connection health monitoring
```

#### Task 1.3: Account Data Sync
```
STATUS: NOT STARTED
REQUIRES HUMAN: No

Execute:
- Implement reqAccountSummary
- Parse account values (NAV, cash, buying power)
- Store in Account model
- Add unit tests for parsing
```

### Week 3-4: Income Sleeve

#### Task 2.1: Cosmos DB Setup
```
STATUS: NOT STARTED
REQUIRES HUMAN: Maybe - Azure resource creation

Execute:
- Create Cosmos DB via Bicep or Portal
- Create containers: trades, signals, orders, snapshots, config
- Implement repository interfaces
- Add connection configuration
```

#### Task 2.2: Monthly Reinvest Strategy
```
STATUS: NOT STARTED
REQUIRES HUMAN: No

Execute:
- Implement allocation drift calculator
- Implement quality gate checks
- Implement buy list generator
- Implement limit order placement
- Add comprehensive unit tests
```

### Week 5-6: Tactical Sleeve

#### Task 3.1: Polygon.io Integration
```
STATUS: NOT STARTED
REQUIRES HUMAN: Yes - Polygon.io signup

PROMPT TO HUMAN:
"Please sign up for Polygon.io:
1. Go to https://polygon.io
2. Sign up for Stocks Starter plan ($29/month)
3. Get your API key from the dashboard
4. Provide me with the API key to store in .env"
```

#### Task 3.2: Technical Indicators
```
STATUS: NOT STARTED
REQUIRES HUMAN: No

Execute:
- Implement SMA (20, 50, 200)
- Implement RSI (2, 14)
- Implement ATR (14)
- Implement volume analysis
- Validate against TradingView
```

### Week 7-8: Risk & Reporting

#### Task 4.1: Discord Integration
```
STATUS: NOT STARTED
REQUIRES HUMAN: Yes - Discord setup

PROMPT TO HUMAN:
"Please set up Discord for notifications:
1. Create a Discord server (or use existing)
2. Create a channel called #trading-bot
3. Go to channel settings → Integrations → Webhooks
4. Create a webhook, copy the URL
5. Provide me with the webhook URL to store in .env"
```

#### Task 4.2: Risk Manager Implementation
```
STATUS: NOT STARTED
REQUIRES HUMAN: No

Execute:
- Implement per-trade risk validation
- Implement daily/weekly stop tracking
- Implement emergency halt logic
- Add Discord alerts for stops
```

---

## Sub-Agent Delegation

When appropriate, you may spawn sub-agents to parallelize work:

**Appropriate sub-agent tasks:**
- Research IBKR API documentation for specific endpoints
- Generate test data/mock responses
- Create unit tests for completed code
- Research technical indicator formulas
- Generate TypeScript types from C# models (if needed)

**NOT appropriate for sub-agents:**
- Decisions requiring human approval
- Tasks involving credentials
- Major architectural changes
- Trading logic implementation (too critical)

---

## Checkpoint Protocol

After completing each major task, create a checkpoint:

```
CHECKPOINT: [Task Name]
STATUS: COMPLETE | IN_PROGRESS | BLOCKED
FILES CREATED/MODIFIED: [List]
NEXT STEPS: [What comes next]
BLOCKERS: [Any issues requiring human input]
```

---

## Risk Parameters (For Reference)

These are the configured risk limits. Do NOT change without human approval:

| Parameter | Value | Description |
|-----------|-------|-------------|
| Per-trade risk | 0.4% | Max risk per single trade |
| Daily stop | 2% | Halt trading if daily loss exceeds |
| Weekly stop | 4% | Halt trading if weekly loss exceeds |
| Max single equity | 7.5% | Max position size (single stock) |
| Max single spread | 3% | Max position size (options spread) |
| Max gross leverage | 1.2x | Max total exposure |
| Issuer cap | 10% | Max exposure to single issuer |
| Category cap | 40% | Max exposure to single category |

---

## External Services Needed

| Service | Purpose | Cost | When Needed |
|---------|---------|------|-------------|
| Interactive Brokers | Brokerage API | $0 (waived w/ activity) | Week 1 |
| Claude API | AI analysis | ~$15-50/month | Week 9 |
| Polygon.io | Earnings calendar | $29/month | Week 5 |
| Discord | Notifications | Free | Week 7 |
| Azure | Hosting | ~$20-40/month | Week 1 |

---

## Error Recovery

If you encounter errors:

1. **Build errors:** Check package versions, ensure all dependencies installed with `dotnet restore`
2. **IBKR connection errors:** Verify TWS is running on port 7497, API enabled
3. **Cosmos DB errors:** Check connection string, verify container exists
4. **Claude API errors:** Verify API key, check rate limits
5. **Test failures:** Read error messages carefully, check test data

If stuck, ask the human for help with:
- "I'm encountering [error]. I've tried [solutions]. Could you check [specific thing]?"

---

## Success Criteria

**Phase 1 Complete When:**
- [ ] IBKR paper trading connected and syncing
- [ ] Income monthly reinvest executing correctly
- [ ] Tactical equity scans generating valid signals
- [ ] Risk limits enforced (positions rejected if over limit)
- [ ] Daily Discord reports sending
- [ ] All configuration stored in Cosmos DB
- [ ] No hardcoded values for configurable parameters

**Phase 2 Complete When:**
- [ ] Claude AI analyzing market regime daily
- [ ] Claude AI ranking tactical candidates
- [ ] Quarterly income quality audit running
- [ ] Options strategies (CSP, bear call spread) executing
- [ ] Covered call overlay on eligible positions

**Phase 3 Complete When:**
- [ ] 12 weeks paper trading completed
- [ ] Profitable OR outperformed S&P 500
- [ ] Human approval for live trading obtained
- [ ] Live trading pilot successful

---

## Quick Commands

```bash
# Build solution
dotnet build

# Run tests
dotnet test

# Run functions locally
cd src/TradingSystem.Functions
func start

# Check Azure deployment
az functionapp list --query "[].name"

# View Cosmos DB data
# Use Azure Portal or Azure Data Explorer
```

---

*This document should be updated as the project progresses.*
*Last updated: 2025-02-04*
