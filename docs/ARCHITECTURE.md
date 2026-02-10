# Architecture

> Living document. Update as the system evolves.
> Claude should help maintain this file when making structural changes.
> **Context budget:** Read on-demand, not every session.

## System Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                    Azure Functions (Orchestration)                │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────┐  │
│  │ DailyOrchest │  │ IncomeSleeve │  │ TacticalSleeve (TBD) │  │
│  └──────┬───────┘  └──────┬───────┘  └──────────┬───────────┘  │
└─────────┼─────────────────┼─────────────────────┼───────────────┘
          │                 │                     │
    ┌─────▼─────┐    ┌─────▼─────┐         ┌─────▼─────┐
    │  IBKR API │    │ Cosmos DB │         │ Polygon.io│
    │  (Broker) │    │  (State)  │         │ (Calendar)│
    └───────────┘    └───────────┘         └───────────┘
          │
    ┌─────▼─────┐    ┌───────────┐
    │  Risk Mgr │    │ Claude AI │  (Phase 2)
    └─────┬─────┘    └─────┬─────┘
          │                │
    ┌─────▼────────────────▼─────┐
    │     Discord (Notifications) │
    └────────────────────────────┘
```

## Key Components

| Component | Responsibility | Key Files |
|-----------|---------------|-----------|
| TradingSystem.Core | Domain models, interfaces, configuration | src/TradingSystem.Core/ |
| TradingSystem.Functions | Azure Functions orchestration | src/TradingSystem.Functions/ |
| TradingSystem.Brokers.IBKR | Interactive Brokers integration | src/TradingSystem.Brokers.IBKR/ |
| TradingSystem.Strategies | Strategy implementations (income + tactical) | src/TradingSystem.Strategies/ |
| TradingSystem.AI | Claude API integration (Phase 2) | src/TradingSystem.AI/ |

## Data Flow

1. **Daily Orchestrator** triggers pre-market and EOD functions
2. **Account sync** pulls positions, NAV, cash from IBKR
3. **Income sleeve** calculates drift, generates buy list, places limit orders
4. **Tactical sleeve** runs technical scans, applies risk gates, generates signals
5. **Risk manager** validates all orders against risk parameters before execution
6. **Discord** receives daily reports and alert notifications

## Key Decisions

See DECISIONS.md for detailed ADRs. Summary of active architectural choices:
- ADR-001: Interactive Brokers for brokerage (full options support)
- ADR-002: Azure for cloud hosting (developer expertise, .NET integration)
- ADR-003: Hybrid AI approach (Claude for analysis, rules for execution)
- ADR-009: Conservative risk parameters with slight adjustments
