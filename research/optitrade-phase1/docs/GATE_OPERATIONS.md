# Gate Operations Runbook

Last updated: 2026-02-20
Audience: project operator
Purpose: make gate passage updates plug-and-play

## What to use

1. Gate criteria and acceptance rules:
   - `docs/VALIDATION_GATES.md`
2. Paste-ready ADR entries:
   - `docs/templates/GATE_ADR_TEMPLATES.md`
3. Paste-ready scoreboard rows:
   - `docs/templates/GATE_SCOREBOARD_TEMPLATES.md`
4. JSON report skeletons:
   - `reports/gates/templates/`

## One-time prep (already done)

- Templates for all 4 gate reports exist under `reports/gates/templates/`.
- Copy/paste templates for ADR and scoreboard updates exist under `docs/templates/`.

## Gate pass workflow (same every time)

1. Complete the gate report JSON for the current phase:
   - Phase 1: `backtests/lean/results/phase1_baseline.json`
   - Phase 2: `reports/gates/phase2_paper_ops.json`
   - Phase 3: `reports/gates/phase3_ai_ablation.json`
   - Phase 4: `reports/gates/phase4_live_readiness.json`
2. Validate values against threshold rules in `docs/VALIDATION_GATES.md`.
3. Paste matching ADR template from `docs/templates/GATE_ADR_TEMPLATES.md` into `DECISIONS.md`.
4. Replace all placeholders (`YYYY-MM-DD`, `X.X%`, `ADR-XXX`, etc.).
5. Update gate scoreboard rows in `PROGRESS.md` using the matching block from `docs/templates/GATE_SCOREBOARD_TEMPLATES.md`.
6. Add one session note in `PROGRESS.md` indicating the gate passed and ADR number.
7. Save and commit both files together.

## Quick gate map

- Phase 1 passes -> add ADR-015 block -> set Phase 2 `in_progress`.
- Phase 2 passes -> add ADR-016 block -> set Phase 3 `in_progress`.
- Phase 3 passes -> add ADR-017 block -> set Phase 4 `in_progress`.
- Phase 4 passes -> add ADR-018 block -> all gates `passed`.

## If a gate fails

1. Do not mark gate `passed`.
2. Create a waiver ADR using waiver rules in `docs/VALIDATION_GATES.md`.
3. Keep scoreboard at `in_progress` (or revert to `pending` if blocked).

