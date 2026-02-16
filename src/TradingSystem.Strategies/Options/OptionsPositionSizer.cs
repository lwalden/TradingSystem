using Microsoft.Extensions.Options;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Models;

namespace TradingSystem.Strategies.Options;

/// <summary>
/// Sizes options positions from per-trade risk and single-spread exposure caps.
/// </summary>
public class OptionsPositionSizer
{
    private readonly RiskConfig _riskConfig;

    public OptionsPositionSizer(RiskConfig riskConfig)
    {
        _riskConfig = riskConfig;
    }

    public OptionsPositionSizer(IOptions<TradingSystemConfig> config)
        : this(config.Value.Risk)
    {
    }

    public OptionsPositionSizeResult CalculateContracts(OptionCandidate candidate, Account account)
    {
        var buyingPower = account.AvailableFunds > 0
            ? account.AvailableFunds
            : account.BuyingPower;

        return CalculateContracts(candidate, account.NetLiquidationValue, buyingPower);
    }

    public OptionsPositionSizeResult CalculateContracts(
        OptionCandidate candidate,
        decimal accountEquity,
        decimal? availableCapital = null)
    {
        var perContractRisk = GetPerContractRisk(candidate);
        if (perContractRisk <= 0 || accountEquity <= 0)
        {
            return new OptionsPositionSizeResult
            {
                Contracts = 0,
                RiskPerContract = perContractRisk,
                TotalRisk = 0,
                RiskBudget = accountEquity * _riskConfig.RiskPerTradePercent,
                SpreadCapBudget = accountEquity * _riskConfig.MaxSingleSpreadPercent,
                LimitedBy = "invalid-risk-input"
            };
        }

        var riskBudget = accountEquity * _riskConfig.RiskPerTradePercent;
        var spreadCapBudget = accountEquity * _riskConfig.MaxSingleSpreadPercent;
        var capitalBudget = availableCapital ?? decimal.MaxValue;

        var byRisk = (int)Math.Floor(riskBudget / perContractRisk);
        var bySpreadCap = (int)Math.Floor(spreadCapBudget / perContractRisk);
        var byCapital = capitalBudget == decimal.MaxValue
            ? int.MaxValue
            : (int)Math.Floor(capitalBudget / perContractRisk);

        var contracts = Math.Max(0, Math.Min(byRisk, Math.Min(bySpreadCap, byCapital)));
        var limitedBy = DetermineLimiter(byRisk, bySpreadCap, byCapital);

        return new OptionsPositionSizeResult
        {
            Contracts = contracts,
            RiskPerContract = perContractRisk,
            TotalRisk = perContractRisk * contracts,
            RiskBudget = riskBudget,
            SpreadCapBudget = spreadCapBudget,
            LimitedBy = limitedBy
        };
    }

    internal static decimal GetPerContractRisk(OptionCandidate candidate)
    {
        if (candidate.MaxLoss != 0)
            return Math.Abs(candidate.MaxLoss);

        if (candidate.Legs.Count >= 2)
        {
            var spreadWidth = Math.Abs(candidate.Legs.Max(l => l.Strike) - candidate.Legs.Min(l => l.Strike));
            if (spreadWidth > 0 && candidate.NetCredit >= 0)
                return Math.Max((spreadWidth - candidate.NetCredit) * 100m, 0m);
            if (spreadWidth > 0 && candidate.NetCredit < 0)
                return Math.Abs(candidate.NetCredit) * 100m;
        }

        return 0m;
    }

    private static string DetermineLimiter(int byRisk, int bySpreadCap, int byCapital)
    {
        var min = Math.Min(byRisk, Math.Min(bySpreadCap, byCapital));
        if (min == byRisk) return "risk-budget";
        if (min == bySpreadCap) return "single-spread-cap";
        return "available-capital";
    }
}

public class OptionsPositionSizeResult
{
    public int Contracts { get; set; }
    public decimal RiskPerContract { get; set; }
    public decimal TotalRisk { get; set; }
    public decimal RiskBudget { get; set; }
    public decimal SpreadCapBudget { get; set; }
    public string LimitedBy { get; set; } = string.Empty;
}
