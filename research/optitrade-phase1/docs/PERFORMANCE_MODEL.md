# OptiMind Performance Model

Last updated: 2026-02-20
Status: Canonical return/volatility estimation method

## Purpose

Define how expected return and volatility are estimated, updated, and reported.
This prevents subjective performance claims and keeps planning tied to evidence.

## Output Schema

Every performance review must report:

1. Point estimate:
   - Expected annual return (net of commissions and modeled slippage).
   - Expected annualized volatility.
2. Interval estimate:
   - 50% interval and 90% interval for return.
   - 90% interval for max drawdown.
3. Confidence tier:
   - Low, Medium, High based on sample size and regime coverage.

## Data Inputs

1. Backtest data:
   - LEAN backtest trades and equity curve.
2. Paper/live execution data:
   - fills, commissions, slippage, realized/unrealized PnL.
3. Market context:
   - VIX regime labels, realized volatility context.
4. Benchmarks:
   - CNDR, PUT, BXM, and SPX.

## Estimation Method

## Step 1: Build net return series

- Construct daily net returns after:
  - commissions,
  - modeled or observed slippage,
  - borrow/financing impact if applicable.

## Step 2: Core statistics

- Annualized return:
  - CAGR from cumulative equity.
- Annualized volatility:
  - standard deviation of daily returns * sqrt(252).
- Drawdown:
  - max peak-to-trough drawdown on equity curve.
  - Always reported as positive magnitude in [0.0, 1.0].
    Example: 0.18 means 18% drawdown.
    See `docs/VALIDATION_GATES.md` (Data Conventions) for gate acceptance requirements.

## Step 3: Interval estimates

- Use block bootstrap on daily returns to preserve autocorrelation.
- Minimum bootstrap runs: 2,000.
- Report:
  - 5th, 25th, 50th, 75th, 95th percentiles of annual return.
  - 95th percentile worst drawdown.

## Step 4: Regime robustness

- Partition results by volatility regime:
  - LOW, NORMAL, ELEVATED, CRISIS.
- Require that no single regime explains the majority of total edge.

## Step 5: Compare to benchmarks

- Report side-by-side annualized:
  - return,
  - volatility,
  - Sharpe (rf=0 and risk-free-adjusted if available),
  - max drawdown.
- Benchmarks must use the exact same time window.

## Confidence Tiers

- Low confidence:
  - < 6 months paper/live equivalent data or poor regime coverage.
- Medium confidence:
  - 6-18 months with at least one elevated-volatility period.
- High confidence:
  - > 18 months and at least one stress regime represented.

## Reporting Cadence

- Weekly: rolling 90-day summary.
- Monthly: full model update and benchmark comparison.
- Pre-live and post-live gates: mandatory formal review packet.

## Kill and De-risk Triggers

- Expected return point estimate drops below 0 after costs: pause new development of non-essential features and review core strategy.
- 90% worst-case drawdown estimate exceeds project drawdown tolerance: reduce deployment assumptions and tighten risk.
- Persistent benchmark underperformance without volatility benefit: simplify strategy stack and reassess.

## Notes on Targets

- The 8-15% return target is a directional planning hypothesis.
- It becomes a commitment only after passing `docs/VALIDATION_GATES.md` across Phase 1-3 evidence.

