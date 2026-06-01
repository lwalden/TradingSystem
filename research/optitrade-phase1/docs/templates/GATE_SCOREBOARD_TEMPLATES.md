# Gate Scoreboard Templates

Use these rows in `PROGRESS.md` -> `## Gate Scoreboard`.

## After Phase 1 passes

```md
| Phase 1 Strategy Viability | passed | ADR-015, CAGR X.X%, DD X.X% |
| Phase 2 Execution and Risk | in_progress | started YYYY-MM-DD after ADR-015 |
| Phase 3 AI Value | pending | blocked on Phase 2 gate |
| Phase 4 Live Readiness | pending | blocked on Phase 2-3 gates |
```

## After Phase 2 passes

```md
| Phase 1 Strategy Viability | passed | ADR-015, CAGR X.X%, DD X.X% |
| Phase 2 Execution and Risk | passed | ADR-016, 0 hard violations, 0 P0 |
| Phase 3 AI Value | in_progress | started YYYY-MM-DD after ADR-016 |
| Phase 4 Live Readiness | pending | blocked on Phase 3 gate |
```

## After Phase 3 passes

```md
| Phase 1 Strategy Viability | passed | ADR-015, CAGR X.X%, DD X.X% |
| Phase 2 Execution and Risk | passed | ADR-016, 0 hard violations, 0 P0 |
| Phase 3 AI Value | passed | ADR-017, +X.Xpp return delta, CI low > 0 |
| Phase 4 Live Readiness | in_progress | started YYYY-MM-DD after ADR-017 |
```

## After Phase 4 passes

```md
| Phase 1 Strategy Viability | passed | ADR-015, CAGR X.X%, DD X.X% |
| Phase 2 Execution and Risk | passed | ADR-016, 0 hard violations, 0 P0 |
| Phase 3 AI Value | passed | ADR-017, +X.Xpp return delta, CI low > 0 |
| Phase 4 Live Readiness | passed | ADR-018, staged go-live complete |
```

