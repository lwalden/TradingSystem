# Automated Trading System

A modular, AI-powered automated trading system built with .NET 8 and Azure Functions.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│              Azure Functions (Orchestration)                 │
│  • Daily pre-market scan                                     │
│  • End-of-day processing                                     │
│  • Monthly income reinvest                                   │
│  • Quarterly quality audit                                   │
└─────────────────────┬───────────────────────────────────────┘
                      │
        ┌─────────────┴─────────────┐
        ▼                           ▼
┌───────────────────┐       ┌───────────────────┐
│   Income Sleeve   │       │  Tactical Sleeve  │
│      (70%)        │       │      (30%)        │
└───────────────────┘       └───────────────────┘
```

## Projects

| Project | Description |
|---------|-------------|
| `TradingSystem.Core` | Domain models, interfaces, configuration |
| `TradingSystem.Functions` | Azure Functions orchestration |
| `TradingSystem.Brokers.IBKR` | Interactive Brokers API integration |
| `TradingSystem.Strategies` | Trading strategy implementations |
| `TradingSystem.AI` | Claude API integration for analysis |
| `TradingSystem.Tests` | Unit and integration tests |

## Sleeves

### Income Sleeve (70% allocation)
- Dividend Growth ETFs (25%): VIG, SCHD, DGRO, VYM
- Covered Call ETFs (20%): JEPI, JEPQ, XYLD, QYLD
- BDCs (20%): ARCC, MAIN, HTGC, OBDC, GBDC
- Equity REITs (10%): O, STAG, ADC, NNN, VICI
- Mortgage REITs (10%): AGNC, NLY, STWD, BXMT
- Preferreds/IG Credit (10%): PFF, PGX, PFFD, LQD, VCIT
- Cash Buffer (5%)

### Tactical Sleeve (30% allocation)
- Momentum Breakout (equities)
- Pullback to Value (equities)
- Cash-Secured Puts
- Bear Call Spreads
- Covered Calls on positions

## Getting Started

### Prerequisites
- .NET 8 SDK
- Azure Functions Core Tools
- Interactive Brokers TWS or Gateway
- Claude API key

### Setup
1. Clone the repository
2. Copy `local.settings.json.template` to `local.settings.json`
3. Configure your API keys and settings
4. Start IBKR TWS/Gateway in paper trading mode
5. Run the Azure Functions locally

```bash
cd src/TradingSystem.Functions
func start
```

### Configuration
Key settings in `local.settings.json`:
- `TradingSystem:Mode`: `Sandbox` or `Live`
- `IBKR:Port`: 7497 (TWS paper) or 7496 (TWS live)
- `Claude:ApiKey`: Your Anthropic API key

## Development Phases

- [x] Phase 1A: Project scaffold and architecture
- [ ] Phase 1B: IBKR API integration
- [ ] Phase 1C: Income sleeve monthly reinvest
- [ ] Phase 1D: Tactical equity scans
- [ ] Phase 2: Claude AI integration
- [ ] Phase 3: Backtesting engine
- [ ] Phase 4: Production deployment

## Risk Management

- Per-trade risk: 0.4% of account
- Daily stop: 2%
- Weekly stop: 4%
- Max single equity: 5%
- Max single spread: 2%
- Max gross leverage: 1.2x

## License
Private - All rights reserved
