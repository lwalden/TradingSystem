using TradingSystem.Core.Models;

namespace TradingSystem.Strategies.Options;

/// <summary>
/// Snapshot of the options sleeve state used by the orchestrator.
/// </summary>
public class OptionsSleeveState
{
    public DateTime AsOf { get; set; } = DateTime.UtcNow;
    public List<OptionsPosition> OpenPositions { get; set; } = new();
    public Dictionary<string, int> PositionsByUnderlying { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public decimal MarginAtRisk { get; set; }
    public int MaxOpenPositions { get; set; }
    public int OpenPositionCount => OpenPositions.Count;
    public int AvailableSlots => Math.Max(0, MaxOpenPositions - OpenPositionCount);
    public bool HasCapacity => AvailableSlots > 0;

    public static OptionsSleeveState Build(
        IEnumerable<OptionsPosition> openPositions,
        int maxOpenPositions)
    {
        var open = openPositions.ToList();
        return new OptionsSleeveState
        {
            OpenPositions = open,
            MaxOpenPositions = maxOpenPositions,
            PositionsByUnderlying = open
                .GroupBy(p => p.UnderlyingSymbol, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase),
            MarginAtRisk = open.Sum(p => Math.Abs(p.MaxLoss) * 100m * p.Quantity)
        };
    }
}
