using Microsoft.Extensions.Options;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Models;

namespace TradingSystem.Strategies.Options;

/// <summary>
/// Deterministic lifecycle rules for managing open options positions.
/// </summary>
public class OptionsLifecycleRules
{
    private readonly OptionsConfig _config;

    public OptionsLifecycleRules(OptionsConfig config)
    {
        _config = config;
    }

    public OptionsLifecycleRules(IOptions<TacticalConfig> tacticalConfig)
        : this(tacticalConfig.Value.Options)
    {
    }

    public OptionsLifecycleDecision Evaluate(OptionsPosition position)
    {
        if (position.Status != OptionsPositionStatus.Open)
            return OptionsLifecycleDecision.Hold("Position is not open.");

        if (position.DTE < _config.CloseDTEThreshold)
        {
            return new OptionsLifecycleDecision(
                OptionsLifecycleAction.CloseNearExpiration,
                $"DTE {position.DTE} is below close threshold {_config.CloseDTEThreshold}.");
        }

        var stopLossDollars = Math.Abs(position.EntryNetCredit) *
                              _config.StopMultipleCredit *
                              100m *
                              position.Quantity;
        var currentLossDollars = Math.Abs(Math.Min(position.UnrealizedPnL, 0m));
        if (stopLossDollars > 0 && currentLossDollars >= stopLossDollars)
        {
            return new OptionsLifecycleDecision(
                OptionsLifecycleAction.StopOut,
                $"Loss {currentLossDollars:C} reached stop threshold {stopLossDollars:C}.");
        }

        var pnlRatio = position.UnrealizedPnLPercent / 100m;
        if (pnlRatio >= _config.ProfitTakeMax)
        {
            return new OptionsLifecycleDecision(
                OptionsLifecycleAction.MandatoryTakeProfit,
                $"Profit {position.UnrealizedPnLPercent:F1}% reached mandatory take-profit threshold {_config.ProfitTakeMax:P0}.");
        }

        if (position.DTE < _config.RollDTEThreshold && position.UnrealizedPnL > 0)
        {
            return new OptionsLifecycleDecision(
                OptionsLifecycleAction.Roll,
                $"DTE {position.DTE} is below roll threshold {_config.RollDTEThreshold} and position is profitable.");
        }

        if (pnlRatio >= _config.ProfitTakeMin)
        {
            return new OptionsLifecycleDecision(
                OptionsLifecycleAction.TakeProfit,
                $"Profit {position.UnrealizedPnLPercent:F1}% reached take-profit threshold {_config.ProfitTakeMin:P0}.");
        }

        return OptionsLifecycleDecision.Hold("No lifecycle rule triggered.");
    }
}

public enum OptionsLifecycleAction
{
    Hold,
    TakeProfit,
    MandatoryTakeProfit,
    StopOut,
    Roll,
    CloseNearExpiration
}

public sealed class OptionsLifecycleDecision
{
    public OptionsLifecycleDecision(OptionsLifecycleAction action, string reason)
    {
        Action = action;
        Reason = reason;
    }

    public OptionsLifecycleAction Action { get; }
    public string Reason { get; }
    public bool ShouldClose => Action is not OptionsLifecycleAction.Hold;
    public bool ShouldOpenReplacement => Action == OptionsLifecycleAction.Roll;

    public static OptionsLifecycleDecision Hold(string reason) =>
        new(OptionsLifecycleAction.Hold, reason);
}
