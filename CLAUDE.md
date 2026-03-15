# CLAUDE.md - Project Instructions

> Claude reads this file automatically at the start of every session.
> Keep it concise -- every line here costs context tokens on every session.
>
> **File Reading Order:** PROGRESS.md first → DECISIONS.md when making architectural choices → other docs on-demand.

---

## Session Protocol

### Starting a Session
1. Read `PROGRESS.md` -- understand current state and what to work on
2. Run `git status` -- check for uncommitted work from previous sessions
3. Run `gh pr list` -- check for open PRs awaiting review
4. Check "Blocked / Awaiting Human" section -- ask user about pending items
5. Resume from "Next Session Should" in PROGRESS.md

### During a Session
- Write code to files immediately -- don't accumulate large changes in memory
- Commit at natural checkpoints (compiles, tests pass, logical unit complete)
- Prefer smaller, frequent commits over one large commit at the end
- Update PROGRESS.md before starting complex multi-step operations

### Ending a Session
Run `/checkpoint` or manually:
1. Update PROGRESS.md with completed tasks, blockers, and next priorities
2. Update DECISIONS.md if any architectural decisions were made
3. Commit tracking changes

### If Session Ends Unexpectedly (Token Limits)
Files on disk and staged changes are preserved. Next session: `git status` and `git diff` show partial work.

---

## Project Identity

**Project:** trading-system
**Description:** A fully automated trading system managing a two-sleeve portfolio (70% income, 30% tactical) via Interactive Brokers API, with Claude AI for complex analysis decisions.
**Type:** api (complex multi-service application)
**Stack:** C# / .NET 8 / Azure Functions (Isolated Worker) / Azure Cosmos DB / Azure Key Vault / Application Insights / IBKR TWS API / Claude API / Discord Webhooks / Polygon.io / xUnit

**Developer Profile:**
- Senior Software Engineer, 12 years C#/.NET experience, strong Azure expertise
- Medium autonomy: routine changes autonomous, ask for architectural decisions
- Conservative risk tolerance -- capital preservation first
- Available ~2 hours/day, afternoon sessions (until 4 PM PT)

---

## Behavioral Rules

### Git Workflow
- **Never commit directly to main** -- always use feature branches
- Branch naming: `feature/short-description`, `fix/short-description`, `chore/short-description`
- All changes via PR. Claude creates PRs; human reviews and merges
- After human merges: `git checkout main && git pull`, then start next branch

### PR Format
- Clear title (under 70 chars), summary of what/why, test plan with verification steps
- Keep PRs reasonably sized -- prefer multiple smaller PRs over one massive PR

### Credentials
- Never store credentials in code files
- Use `.env` files (gitignored) for local development
- Azure Key Vault for production secrets
- When you need a credential, ask: "Please provide your [SERVICE] API key. I'll store it in .env."

### Autonomy Boundaries
**You CAN autonomously:** Create files, install NuGet packages, run builds/tests, create branches and PRs, scaffold code, manage dependencies
**Ask the human first:** Create GitHub repos, merge PRs, sign up for services, provide API keys, approve major architectural changes, make billing decisions, switch from sandbox to live trading, change risk parameters or sleeve allocations

### Communication
- Lead with a TL;DR, then provide detail
- Be proactive about dependencies: "Before X, we need Y"
- Flag risks early rather than discovering them mid-implementation

### Verification-First Development
- Before implementing a feature, confirm requirements by restating what you'll build
- Write tests appropriate to the project's quality tier (see strategy-roadmap.md)
- When the quality tier is Standard or above: write failing tests first, then implement
- Every PR should reference the acceptance criteria from strategy-roadmap.md

### Trading-Specific Rules
- **NEVER** switch from SANDBOX to LIVE trading mode without explicit human approval
- **NEVER** change risk parameters or sleeve allocations without explicit human approval
- **NEVER** trade within no-trade windows for special reasons without explicit human approval
- All trading logic must be deterministic (rule-based); AI is for analysis only

### Decision Recording
- Record significant architectural decisions in DECISIONS.md (library choices, API contracts, auth approach, data model changes, deploy decisions)
- Record known shortcuts and workarounds in the Known Debt section of DECISIONS.md
- Include alternatives considered — a decision without alternatives is an assertion, not a record
- To auto-load DECISIONS.md every session, add `@DECISIONS.md` to this file

---

## Context Budget

> Use `/context` for real-time context usage and optimization tips.

**Always loaded:** CLAUDE.md — keep under ~100 lines; don't add without removing something

**On-demand:** DECISIONS.md — add `@DECISIONS.md` here to auto-load; delete superseded entries
