# Phase 3: AI Intelligence Layer

**Window:** Weeks 21-30
**Effort:** 150-220 hours at 15-20 hrs/week
**Mode:** paper only
**Last Updated:** 2026-02-20

---

## Phase Goal

Add AI-assisted regime interpretation and portfolio review while preserving deterministic quantitative fallbacks and proving measurable value over static allocation.

## Entry Criteria

- Phase 2 gate passed.

## Exit Criteria

1. Phase 3 gate in `docs/VALIDATION_GATES.md` is passed.
2. AI-vs-static ablation over >= 8 weeks is complete.
3. AI failure modes are safe and non-fatal.

---

## Sprint 3.1 (Weeks 21-22): Market Context Engine

### Deliverables

- Structured market context snapshots.
- Quantitative baseline regime classifier.
- Time-series persistence for context features.

### Acceptance

- Baseline regime can run independently of AI.

---

## Sprint 3.2 (Weeks 23-25): Claude Integration

### Deliverables

- Runtime AI client using `claude-sonnet-4-6`.
- Structured output parsing and guardrails.
- Timeout/retry/fallback ladder:
  - fresh AI result,
  - recent cached AI result,
  - quantitative regime fallback.

### Acceptance

- No AI exception can crash the trading loop.

---

## Sprint 3.3 (Weeks 26-27): MCP Portfolio Interface

### Deliverables

- MCP server exposing portfolio and risk query tools.
- Claude Desktop/Code integration for read/review workflows.

### Acceptance

- Portfolio state and risk summaries accessible via MCP tools.

---

## Sprint 3.4 (Weeks 28-30): Adaptive Weighting and Ablation

### Deliverables

- Regime-aware weighting engine.
- Static-allocation shadow portfolio tracking.
- Comparative performance report.

### Acceptance

- AI value judged by evidence, not preference.

---

## Dependencies

- Stable Phase 2 paper execution and data collection.
- Valid Anthropic API access and usage logging.

## Phase 3 Evaluation Decision: Hugging Face MCP

At Phase 3 entry, evaluate whether to add the Hugging Face MCP server as a supplemental AI tool.

**What it enables:** Direct access to HF-hosted models (sentiment, volatility regime classifiers, NLP on macro/earnings text) without a separate Python inference pipeline. Would be used by the AI layer as a complement to Claude, not a replacement.

**Decision criteria — add if ALL of the following are true:**

1. Sprint 3.1 baseline regime classifier has a measurable accuracy gap that a specialized model could close.
2. A suitable HF-hosted model exists for the target task (options-relevant sentiment or regime classification).
3. Inference latency is acceptable for the trading loop cadence established in Phase 2.
4. Incremental cost is within the Phase 3 cost model budget (`docs/COST_MODEL.md`).

**Decision criteria — skip if ANY of the following are true:**

- Claude-only regime interpretation meets or exceeds the ablation target in Sprint 3.4.
- No suitable model exists without fine-tuning (fine-tuning is out of scope for Phase 3).
- Latency or cost would breach Phase 3 constraints.

**Process:** Record the decision as an ADR entry in `DECISIONS.md` at Phase 3 entry. If added, configure via `.mcp.json` following the same pattern as GitHub and DB Hub.

## Out of Scope

- Unbounded autonomous trade execution from AI output.
- Live mode enablement.

## Phase 3 Evidence Checklist

- [ ] AI-vs-static ablation report complete.
- [ ] Fallback-path tests complete.
- [ ] Cost usage aligns with cost model assumptions.
- [ ] Gate report produced at `reports/gates/phase3_ai_ablation.json`.
- [ ] Gate scoreboard updated in `PROGRESS.md`.
