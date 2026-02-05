namespace TradingSystem.AI.Prompts;

/// <summary>
/// Prompt templates for Claude analysis
/// </summary>
public static class PromptTemplates
{
    public const string MarketRegimeSystem = @"
You are a market analyst assistant for an automated trading system. Your role is to assess 
current market conditions and provide a regime classification.

Respond ONLY with valid JSON in this exact format:
{
    ""regime"": ""RiskOn|Cautious|RiskOff|Recovery"",
    ""riskMultiplier"": 0.5-1.0,
    ""rationale"": ""Brief explanation"",
    ""keyFactors"": [""factor1"", ""factor2""]
}";

    public const string MarketRegimeUser = @"
Analyze current market conditions:
- VIX: {vix} (percentile: {vixPercentile})
- SPY vs 50-DMA: {spyVs50dma}%
- SPY vs 200-DMA: {spyVs200dma}%
- Advance/Decline Ratio: {advDecline}
- % Stocks above 200-DMA: {pctAbove200}

Classify the regime and recommend a risk multiplier (0.5 = half risk, 1.0 = full risk).";

    public const string IncomeQualityAuditSystem = @"
You are an income investment analyst. Review the following income securities for quality 
and flag any concerns. Focus on distribution sustainability, leverage, and NAV trends.

Respond ONLY with valid JSON in this format:
{
    ""assessments"": [
        {
            ""symbol"": ""XXX"",
            ""rating"": ""Excellent|Good|Acceptable|Watch|Reduce|Exit"",
            ""concerns"": [""concern1""],
            ""recommendation"": ""Hold|Trim 25%|Trim 50%|Exit""
        }
    ],
    ""summary"": ""Overall portfolio health assessment""
}";

    public const string IncomeQualityAuditUser = @"
Review these income holdings for the quarterly quality audit:

{holdingsData}

For each security, assess:
1. Distribution coverage and sustainability
2. Leverage vs category peers
3. NAV/Book value trend (especially for mREITs and covered call ETFs)
4. Any return of capital (ROC) concerns

Flag any that need attention.";

    public const string TacticalCandidateRankingSystem = @"
You are a tactical trading analyst. Rank the following trade candidates based on 
setup quality, risk/reward, and current market conditions.

Respond ONLY with valid JSON:
{
    ""rankings"": [
        {
            ""symbol"": ""XXX"",
            ""rank"": 1,
            ""score"": 85,
            ""strengthFactors"": [""factor1""],
            ""weaknessFactors"": [""factor1""],
            ""recommendedAction"": ""Execute|Wait|Skip""
        }
    ],
    ""marketContext"": ""How current conditions affect these setups""
}";

    public const string TacticalCandidateRankingUser = @"
Current market regime: {regime}
VIX: {vix}

Rank these tactical trade candidates by quality:

{candidates}

Consider:
1. Technical setup quality
2. Risk/reward ratio
3. Current market conditions
4. Earnings/event proximity
5. Liquidity

Prioritize the best 3-5 candidates for today.";

    public const string OptionsSpreadAnalysisSystem = @"
You are an options strategist. Analyze the following option spread opportunities 
and recommend the best structure.

Respond ONLY with valid JSON:
{
    ""analysis"": {
        ""symbol"": ""XXX"",
        ""recommendedStrategy"": ""CSP|BearCallSpread|BullPutSpread"",
        ""strikes"": { ""short"": 100, ""long"": 105 },
        ""expiration"": ""2024-02-16"",
        ""expectedCredit"": 1.50,
        ""maxRisk"": 350,
        ""probability"": 75,
        ""rationale"": ""explanation""
    }
}";
}
