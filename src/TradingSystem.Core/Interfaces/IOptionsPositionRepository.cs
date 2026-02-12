using TradingSystem.Core.Models;

namespace TradingSystem.Core.Interfaces;

/// <summary>
/// Repository for options position persistence (grouped multi-leg spreads).
/// </summary>
public interface IOptionsPositionRepository
{
    Task<OptionsPosition> SaveAsync(OptionsPosition position, CancellationToken ct = default);
    Task<OptionsPosition?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<OptionsPosition>> GetOpenPositionsAsync(CancellationToken ct = default);
    Task<List<OptionsPosition>> GetByUnderlyingAsync(string symbol, CancellationToken ct = default);
    Task<List<OptionsPosition>> GetByDateRangeAsync(DateTime start, DateTime end, CancellationToken ct = default);
    Task UpdateAsync(OptionsPosition position, CancellationToken ct = default);
}
