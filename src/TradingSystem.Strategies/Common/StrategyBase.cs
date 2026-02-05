using Microsoft.Extensions.Logging;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Strategies.Common;

/// <summary>
/// Base class for all trading strategies
/// </summary>
public abstract class StrategyBase : IStrategy
{
    protected readonly ILogger _logger;

    protected StrategyBase(ILogger logger)
    {
        _logger = logger;
    }

    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract SleeveType Sleeve { get; }
    public abstract StrategyType Type { get; }
    public bool IsEnabled { get; set; } = true;
    public virtual bool RequiresAIAnalysis => false;

    public abstract Task<List<Signal>> EvaluateAsync(StrategyContext context, 
        CancellationToken cancellationToken = default);

    public virtual Task<bool> ValidateSignalAsync(Signal signal, StrategyContext context,
        CancellationToken cancellationToken = default)
    {
        // Default validation - check signal hasn't expired
        if (!signal.IsValid)
        {
            _logger.LogInformation("Signal {Id} is no longer valid", signal.Id);
            return Task.FromResult(false);
        }

        // Check no-trade window
        if (context.IsInNoTradeWindow(signal.Symbol))
        {
            _logger.LogInformation("Signal {Id} rejected - symbol in no-trade window", signal.Id);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    protected Signal CreateSignal(string symbol, SignalDirection direction, 
        SignalStrength strength, string rationale)
    {
        return new Signal
        {
            StrategyId = Id,
            StrategyName = Name,
            SetupType = Type.ToString(),
            Symbol = symbol,
            Direction = direction,
            Strength = strength,
            Rationale = rationale,
            GeneratedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(8) // Default 1 trading day
        };
    }
}
