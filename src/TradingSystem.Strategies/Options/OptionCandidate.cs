using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Strategies.Options;

/// <summary>
/// A scored options trade candidate produced by the screening service.
/// Contains the full leg definition, risk/reward metrics, and a composite score.
/// </summary>
public class OptionCandidate
{
    public string UnderlyingSymbol { get; set; } = string.Empty;
    public StrategyType Strategy { get; set; }
    public List<OptionLeg> Legs { get; set; } = new();

    // Risk/reward
    public decimal MaxProfit { get; set; }
    public decimal MaxLoss { get; set; }
    public decimal ReturnOnRisk => MaxLoss != 0 ? Math.Round(MaxProfit / Math.Abs(MaxLoss) * 100, 2) : 0;
    public decimal NetCredit { get; set; }

    // Probability
    public decimal ProbabilityOfProfit { get; set; }

    // IV context
    public decimal IVRank { get; set; }
    public decimal IVPercentile { get; set; }

    // DTE
    public int DTE => Legs.Count > 0 ? Legs.Min(l => l.DTE) : 0;

    // Composite score (higher = better)
    public decimal Score { get; set; }
    public string ScoreBreakdown { get; set; } = string.Empty;

    // Underlying price context
    public decimal UnderlyingPrice { get; set; }
}
