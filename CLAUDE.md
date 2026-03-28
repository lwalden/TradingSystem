# CLAUDE.md - Project Instructions

> Claude reads this file automatically at the start of every session.
> Keep it concise — every line costs context tokens.
> Use `claude --continue` to restore the previous session's full message history.

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

See `.claude/rules/git-workflow.md` — loaded natively by Claude Code each session.

### PR Format
- Clear title (under 70 chars), summary of what/why, test plan with verification steps
- Keep PRs reasonably sized -- prefer multiple smaller PRs over one massive PR

### Credentials
- Never store credentials in code files
- Use `.env` files (gitignored) for local development
- Azure Key Vault for production secrets
- When you need a credential, ask: "Please provide your [SERVICE] API key. I'll store it in .env."

### Autonomy Boundaries

**You CAN autonomously:** Create files, install packages, run builds/tests, create branches and PRs, scaffold code, install and use CLI tools, query cloud services and APIs

**Only when explicitly asked:** Merge PRs

**Ask the human first:** Create GitHub repos, sign up for services, provide API keys, approve major architectural changes

**Tool-first rule:** See `.claude/rules/tool-first.md` — never ask the user to do something you can do with a tool

### Communication
- Lead with a TL;DR, then provide detail
- Be proactive about dependencies: "Before X, we need Y"
- Flag risks early rather than discovering them mid-implementation

### Verification-First Development

- Write failing tests first, then implement
- Run the full test suite before every commit

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

**Always loaded:** CLAUDE.md — keep under ~50 lines; don't add without removing something

**On-demand:** DECISIONS.md — add `@DECISIONS.md` here to auto-load; delete superseded entries
