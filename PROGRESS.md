# PROGRESS.md - Session Continuity

> Claude reads this FIRST every session. Run `/archive` when exceeding 100 lines.

**Phase:** 1 - Foundation (Week 9 in progress)
**Last Updated:** 2026-02-16

## Completed
- Week 1-2: IBKR connection layer (PR #4), smoke test (PR #5), market data subscriptions
- Week 3-4: Local JSON storage (PR #6), IBKR orders (PR #7), income sleeve (PR #8) -- all merged
- Week 5: IBKR option chains, IV rank/percentile, technical indicators, market data service (PR #9)
- Week 6: Options screening service, Polygon.io calendar integration (PR #10, #11)
- Fix: VIX index contract, option chain ConId, delayed data fallback (PR #12)
- Week 7 PR 1: Multi-leg combo orders, OptionsPosition model, JSON repository (PR #13 merged)
- Week 7 PR 2: options lifecycle rules, position grouper/reconciliation, candidate converter, position sizer
- Week 7 PR 3: `OptionsExecutionService`, `OptionsSleeveManager`, `OptionsSleeveState`, orchestration tests
- Week 8 PR 1: wired options services into Functions DI + pre-market orchestrator path (`Program.cs`, `DailyOrchestrator.cs`)
- Week 8 PR 1: added pre-market orchestration tests (`DailyOrchestratorTests`) covering missing broker, connect-fail, missing manager, empty universe, and manager-invoked path
- Week 9 PR 1: implemented concrete `RiskManager` (`IRiskManager`) with per-trade risk checks, stop-halt checks, position/cap enforcement, no-trade window validation, and metrics
- Week 9 PR 1: registered `IRiskManager` in Functions DI to fully resolve `OptionsSleeveManager` at runtime
- Week 9 PR 1: added `RiskManagerTests` (8 tests) covering rejection/acceptance paths and risk metrics behavior
- Infra fix: removed machine-specific IBKR API dependency path by vendoring `CSharpAPI` under `src/ThirdParty/CSharpAPI` and repointing solution/project references (unblocks GitHub Actions restore/build)
- Build fixes uncovered during wiring: `ClaudeService` `PostAsJsonAsync` import, Functions host bootstrap (`ConfigureFunctionsWorkerDefaults`), KeyVault config package reference
- Docs: corrected live capital envelope typo from `$10,000-$400,000` to `$100,000-$400,000` and synchronized decision/review docs
- Fix: IBKR error 10167 delayed data warning treated as informational (pushed to main)
- Fix: IV history stale-date check now uses UTC day boundary (`JsonIVHistoryRepository`)
- **413 tests passing**

## Blockers
- ~~Polygon.io~~ DONE -- Stocks Starter plan signed up
- Discord Setup: Create server/channel, create webhook URL -- needed Week 10
- Claude API Key: Sign up at console.anthropic.com -- needed Week 9

## Next Session Should
1. Expand smoke coverage for the pre-market options orchestration path (live IBKR smoke remains optional/manual)
2. Continue Week 9 risk engine integration (wire stop-trigger alerts + persistence-backed drawdown tracking)
3. Begin Claude regime service integration; prompt for Claude API key at gate before dependent automation

---
<!-- Recent sessions: keep last 3 entries. Older entries -> docs/archive/progress-archive.md -->
- 2026-02-16 CI fix: replaced external `..\..\tws api\...` project references with repo-local `src/ThirdParty/CSharpAPI` to resolve GitHub Actions restore failure.
- 2026-02-16 Completed Week 9 PR1: concrete `RiskManager` implemented + wired into Functions DI, 8 new risk-manager tests added, options manager now fully resolvable in runtime DI. Also synchronized capital-envelope typo fix across strategy/decision/review docs. 413 tests passing.
- 2026-02-16 Completed Week 8 PR1 wiring: Functions DI + DailyOrchestrator options path, added 5 orchestration tests. Found and fixed compile gaps (ClaudeService HttpClient JSON extension import, Functions worker bootstrap/config package). 405 tests passing.
