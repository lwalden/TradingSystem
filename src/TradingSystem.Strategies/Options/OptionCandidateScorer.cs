using TradingSystem.Core.Interfaces;

namespace TradingSystem.Strategies.Options;

/// <summary>
/// Composite scoring for option candidates.
/// Score components: return on risk, IV rank, probability of profit, DTE alignment.
/// Higher score = better candidate.
/// </summary>
public static class OptionCandidateScorer
{
    /// <summary>
    /// Score a candidate on a 0-100 scale.
    /// </summary>
    public static decimal Score(OptionCandidate candidate)
    {
        var rorScore = ScoreReturnOnRisk(candidate.ReturnOnRisk, candidate.Strategy);
        var ivScore = ScoreIVRank(candidate.IVRank);
        var popScore = ScorePOP(candidate.ProbabilityOfProfit, candidate.Strategy);
        var dteScore = ScoreDTE(candidate.DTE, candidate.Strategy);

        // Weighted composite
        var weights = GetWeights(candidate.Strategy);
        var total = rorScore * weights.RoR +
                    ivScore * weights.IV +
                    popScore * weights.POP +
                    dteScore * weights.DTE;

        candidate.Score = Math.Round(Math.Clamp(total, 0, 100), 1);
        candidate.ScoreBreakdown = $"RoR={rorScore:F0}×{weights.RoR:F2} IV={ivScore:F0}×{weights.IV:F2} POP={popScore:F0}×{weights.POP:F2} DTE={dteScore:F0}×{weights.DTE:F2}";
        return candidate.Score;
    }

    /// <summary>
    /// Return on risk score (0-100).
    /// For credit strategies: target 15-40% return on risk.
    /// </summary>
    internal static decimal ScoreReturnOnRisk(decimal returnOnRisk, StrategyType strategy)
    {
        // CSPs: RoR based on cash secured amount is typically lower
        if (strategy == StrategyType.CashSecuredPut)
        {
            if (returnOnRisk >= 3) return 100;
            if (returnOnRisk >= 2) return 80;
            if (returnOnRisk >= 1) return 60;
            if (returnOnRisk >= 0.5m) return 40;
            return 20;
        }

        // Spreads: target 20-40% return on risk
        if (returnOnRisk >= 40) return 100;
        if (returnOnRisk >= 30) return 85;
        if (returnOnRisk >= 20) return 70;
        if (returnOnRisk >= 15) return 55;
        if (returnOnRisk >= 10) return 40;
        return 20;
    }

    /// <summary>
    /// IV rank score (0-100). Higher IV rank = better for selling premium.
    /// </summary>
    internal static decimal ScoreIVRank(decimal ivRank)
    {
        if (ivRank >= 80) return 100;
        if (ivRank >= 60) return 80;
        if (ivRank >= 50) return 65;
        if (ivRank >= 40) return 50;
        if (ivRank >= 30) return 35;
        return 20;
    }

    /// <summary>
    /// Probability of profit score (0-100).
    /// For credit strategies: target 65-80% POP.
    /// </summary>
    internal static decimal ScorePOP(decimal pop, StrategyType strategy)
    {
        // Iron condors target lower POP since they collect premium on both sides
        if (strategy == StrategyType.IronCondor)
        {
            if (pop >= 70) return 100;
            if (pop >= 60) return 80;
            if (pop >= 50) return 60;
            if (pop >= 40) return 40;
            return 20;
        }

        // Standard credit strategies
        if (pop >= 80) return 100;
        if (pop >= 75) return 90;
        if (pop >= 70) return 75;
        if (pop >= 65) return 60;
        if (pop >= 60) return 45;
        return 20;
    }

    /// <summary>
    /// DTE alignment score (0-100). Target 30-45 DTE for optimal theta decay.
    /// </summary>
    internal static decimal ScoreDTE(int dte, StrategyType strategy)
    {
        // Calendar spreads prefer longer DTE
        if (strategy == StrategyType.CalendarSpread)
        {
            if (dte >= 30 && dte <= 60) return 100;
            if (dte >= 21 && dte <= 75) return 75;
            return 40;
        }

        // Standard strategies: sweet spot is 30-45 DTE
        if (dte >= 30 && dte <= 45) return 100;
        if (dte >= 25 && dte <= 50) return 80;
        if (dte >= 21 && dte <= 55) return 60;
        if (dte >= 14 && dte <= 60) return 40;
        return 20;
    }

    private static (decimal RoR, decimal IV, decimal POP, decimal DTE) GetWeights(StrategyType strategy)
    {
        return strategy switch
        {
            StrategyType.CashSecuredPut => (0.25m, 0.30m, 0.30m, 0.15m),
            StrategyType.IronCondor => (0.30m, 0.25m, 0.25m, 0.20m),
            StrategyType.CalendarSpread => (0.20m, 0.35m, 0.20m, 0.25m),
            _ => (0.30m, 0.25m, 0.30m, 0.15m) // Spreads
        };
    }
}
