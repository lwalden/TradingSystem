using TradingSystem.Core.Configuration;
using TradingSystem.Core.Models;

namespace TradingSystem.Strategies.Income;

/// <summary>
/// Tags raw broker positions with Sleeve and Category based on the IncomeUniverse.
/// Positions whose symbol is found in the universe are tagged as Income sleeve
/// with the appropriate IncomeCategory.
/// </summary>
public static class IncomePositionTagger
{
    /// <summary>
    /// Tags positions that belong to the income universe with their sleeve and category.
    /// Positions not in the universe are left unchanged.
    /// </summary>
    public static void TagPositions(IList<Position> positions, IncomeUniverse universe)
    {
        foreach (var position in positions)
        {
            if (universe.TryGetCategory(position.Symbol, out var category))
            {
                position.Sleeve = SleeveType.Income;
                position.Category = category.ToString();
            }
        }
    }

    /// <summary>
    /// Returns only positions that belong to the income sleeve (tagged or by existing Sleeve value).
    /// </summary>
    public static List<Position> GetIncomePositions(IEnumerable<Position> positions)
    {
        return positions.Where(p => p.Sleeve == SleeveType.Income).ToList();
    }
}
