# OptiMind Cost Model

Last updated: 2026-02-20
Status: Canonical operating cost assumptions

## Purpose

Provide a conservative, transparent operating cost model for ROI evaluation.
All ROI discussions reference this file.

## Capital Assumption

- Live capital base for planning: $400,000.

## Cost Scenarios

## Low Scenario

- Azure VM (D2s v5): $70/mo
- Azure PostgreSQL + monitor/backup: $42/mo
- IBKR market data: $8/mo
- IBKR commissions: $32/mo
- Claude API: $20/mo
- ORATS: $99/mo
- Total: $271/mo ($3,252/yr)
- Break-even annual return on $400,000: 0.81%

## Base Scenario

- Azure VM (D2s v5): $80/mo
- Azure PostgreSQL + monitor/backup: $52/mo
- IBKR market data: $10/mo
- IBKR commissions: $65/mo
- Claude API: $40/mo
- ORATS: $199/mo
- Total: $446/mo ($5,352/yr)
- Break-even annual return on $400,000: 1.34%

## High Scenario

- Azure VM (D2s v5): $95/mo
- Azure PostgreSQL + monitor/backup: $70/mo
- IBKR market data: $20/mo
- IBKR commissions: $120/mo
- Claude API: $100/mo
- ORATS: $399/mo
- Total: $804/mo ($9,648/yr)
- Break-even annual return on $400,000: 2.41%

## ROI Snapshot by Gross Return Target

At 8% gross return ($32,000/yr):
- Low: $28,748 net
- Base: $26,648 net
- High: $22,352 net

At 10% gross return ($40,000/yr):
- Low: $36,748 net
- Base: $34,648 net
- High: $30,352 net

At 15% gross return ($60,000/yr):
- Low: $56,748 net
- Base: $54,648 net
- High: $50,352 net

## Rules

1. Cost review cadence: monthly.
2. Any vendor price change > 20% requires this file update and a note in `DECISIONS.md`.
3. No ROI claims should use a single-point cost assumption; always show low/base/high.

## Scope Clarifications

- This model excludes tax liability and one-time setup labor.
- This model is for system operating economics only.

