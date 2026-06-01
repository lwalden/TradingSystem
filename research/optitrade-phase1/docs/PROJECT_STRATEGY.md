# OptiMind: Project Strategy and Development Roadmap

**Project Codename:** OptiMind
**Owner:** Laurance
**Last Updated:** 2026-02-20
**Status:** Phase 1 in progress (paper mode)
**Canonical Document:** Yes

---

## Purpose

Define the canonical project scope, timeline, constraints, and success criteria.
All other planning documents must align to this file.

## Objective Framing

OptiMind is a custom options trading system with quantitative and AI-assisted decision support.

Performance objective:

- Target return: 8-15% annualized on $400,000 capital.
- Target risk profile: lower volatility and drawdown than SPX.
- This target is a hypothesis until `docs/VALIDATION_GATES.md` is passed.

## Why Build Custom

1. Portfolio-level risk controls and circuit breakers are first-class requirements.
2. Multi-strategy allocation and regime adaptation are core behavior.
3. Tight integration between strategy, execution, risk, and review workflow is required.

## Canonical Consistency Matrix

| Domain | Canonical Value | Source of Truth |
|---|---|---|
| Runtime language | Python 3.12+ | `DECISIONS.md` ADR-001 |
| Backtesting language | C# (LEAN only) | `DECISIONS.md` ADR-001 |
| Dependency manager | uv | `DECISIONS.md` ADR-007 |
| Runtime LLM model | `claude-sonnet-4-6` | `DECISIONS.md` ADR-012 |
| Paper/live control | `OPTIMIND_MODE=paper|live` | `DECISIONS.md` ADR-003 |
| Phase 1 window | Weeks 1-10 (paper only) | `docs/PHASE_1_FOUNDATION.md` |
| Phase 2 window | Weeks 11-20 | `docs/PHASE_2_STRATEGIES.md` |
| Phase 3 window | Weeks 21-30 | `docs/PHASE_3_AI_LAYER.md` |
| Phase 4 window | Weeks 31-42+ | `docs/PHASE_4_PRODUCTION.md` |
| First live trade | Phase 4 only, staged | `docs/PHASE_4_PRODUCTION.md` |
| Return target framing | hypothesis, not guarantee | `DECISIONS.md` ADR-010 |
| Phase advancement rule | gate-first | `DECISIONS.md` ADR-011, `docs/VALIDATION_GATES.md` |
| Canonical roadmap file | this file | `DECISIONS.md` ADR-013 |

## Non-Negotiable Hard Risk Limits

Hard limits are implemented in `optimind/core/constants.py`.

| Limit | Value |
|---|---|
| Max risk per trade | 2.5% of NLV |
| Max deployed capital | 40% of NLV |
| Max positions per underlying | 2 |
| Max sector positions | 3 |
| Daily loss halt | -3% of NLV |
| Daily emergency stop | -5% of NLV |
| Weekly loss limit | -5% of NLV |
| Monthly loss limit | -10% of NLV |
| Portfolio delta warn/limit | 7% / 10% of NLV |
| Portfolio vega limit | 1.5% of NLV (enforced Phase 2+) |
| Max margin utilization | 60% Reg-T / 40% PM |
| Max adjustments per position | 2 |

## Timeline

| Phase | Weeks | Goal |
|---|---|---|
| Phase 1 | 1-10 | Backtest viability + first full paper iron condor lifecycle |
| Phase 2 | 11-20 | Multi-strategy runtime and enforced portfolio risk |
| Phase 3 | 21-30 | AI layer with measurable value and safe fallback behavior |
| Phase 4 | 31-42+ | Production hardening, staged go-live, live monitoring |
| Stabilization | 43-52 | Operational maturity and post-go-live tuning |

Target windows (assuming 15-20 hrs/week):

- First live trade: approximately Month 8-10.
- Stable production track record: approximately Month 10-13.

## Gate-First Progression

No phase progression occurs on timeline alone.

Required:

1. Gate pass in `docs/VALIDATION_GATES.md`.
2. Gate status updated in `PROGRESS.md`.
3. Any waiver recorded in `DECISIONS.md`.

## Success Criteria

### Development success

- Each phase gate is passed without unresolved critical safety defects.

### Production success

- First 6 live months are assessed against:
  - net return,
  - annualized volatility,
  - max drawdown,
  - operational incident rate.
- Results are evaluated versus CNDR, PUT, BXM, and SPX over the same interval.

## Cost and Performance Governance

- Cost assumptions: `docs/COST_MODEL.md`
- Expected return/volatility methodology: `docs/PERFORMANCE_MODEL.md`
- Backtest/live parity controls: `docs/BACKTEST_LIVE_PARITY.md`

## File Index

| Document | Purpose |
|---|---|
| `docs/PROJECT_STRATEGY.md` | Canonical strategy and consistency matrix |
| `docs/PHASE_1_FOUNDATION.md` | Phase 1 plan |
| `docs/PHASE_2_STRATEGIES.md` | Phase 2 plan |
| `docs/PHASE_3_AI_LAYER.md` | Phase 3 plan |
| `docs/PHASE_4_PRODUCTION.md` | Phase 4 plan |
| `docs/VALIDATION_GATES.md` | Hard pass/fail gates |
| `docs/GATE_OPERATIONS.md` | Step-by-step operator workflow for gate updates |
| `docs/templates/GATE_ADR_TEMPLATES.md` | Paste-ready ADR templates for gate passage |
| `docs/templates/GATE_SCOREBOARD_TEMPLATES.md` | Paste-ready `PROGRESS.md` scoreboard row templates |
| `docs/PERFORMANCE_MODEL.md` | Return/volatility estimation method |
| `docs/COST_MODEL.md` | Operating cost scenarios and ROI |
| `docs/BACKTEST_LIVE_PARITY.md` | Backtest/runtime parity controls |
| `docs/RISK_FRAMEWORK.md` | Full risk policies and safety procedures |
| `docs/ARCHITECTURE.md` | Module and deployment architecture |
| `docs/strategy-roadmap.md` | Non-canonical pointer/index file |

## Context Budget

| File | Target Size | Action if Exceeded |
|---|---|---|
| `CLAUDE.md` | ~65 lines | Don't add without removing something |
| `PROGRESS.md` | ~20 lines active | Self-trimming: only 3 session notes kept |
| `DECISIONS.md` | Grows over time | Delete superseded entries (git history preserves them) |

**Reading strategy:**
- `PROGRESS.md`: every session (auto-injected by hook)
- `DECISIONS.md`: auto-injected if decisions exist; always check before architectural choices
- Phase and architecture docs: on-demand
