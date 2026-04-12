# Cost Model

> Operational reference for platform and brokerage costs. Extracted from `docs/strategy-roadmap.md` to keep the roadmap focused on strategy and architecture.

## Platform Costs (Target Ceiling Applies Here)

| Service | Estimated Monthly Cost | Purpose |
|---------|----------------------|---------|
| Azure Functions | ~$0-5 | Orchestration |
| Cosmos DB (Serverless) | ~$5-15 | State & config storage |
| Key Vault | ~$1 | Secret management |
| Application Insights | ~$1-5 | Monitoring |
| Storage Account | ~$1 | Function storage |
| Claude API | ~$2-10 | Regime detection + quarterly audits + recommendation/report augmentation |
| Polygon.io | $29 | Earnings calendar |
| Discord | Free | Notifications |
| **Platform Total** | **~$40-65/month** | **Target: under $100** |

## Brokerage Commissions & Fees (Tracked Separately)

Conservative planning model for options activity: assume ~$1.00 all-in per option contract-side.

| Monthly Option Contract-Sides | Estimated Commission/Fee Cost |
|-----------------------------:|------------------------------:|
| 100 | ~$100 |
| 250 | ~$250 |
| 500 | ~$500 |
| 1,000 | ~$1,000 |

## All-In Monthly Range (Platform + Brokerage)

- Light options activity (100 sides): ~$140-165
- Moderate options activity (250 sides): ~$290-315
- Active options activity (500 sides): ~$540-565
