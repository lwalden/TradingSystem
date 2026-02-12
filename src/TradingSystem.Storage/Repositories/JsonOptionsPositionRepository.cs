using Microsoft.Extensions.Options;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Storage.Repositories;

public class JsonOptionsPositionRepository : IOptionsPositionRepository
{
    private readonly JsonFileStore _store;

    public JsonOptionsPositionRepository(IOptions<LocalStorageConfig> config)
    {
        _store = new JsonFileStore(Path.Combine(config.Value.DataDirectory, "options-positions.json"));
    }

    public JsonOptionsPositionRepository(string dataDirectory)
    {
        _store = new JsonFileStore(Path.Combine(dataDirectory, "options-positions.json"));
    }

    public async Task<OptionsPosition> SaveAsync(OptionsPosition position, CancellationToken ct = default)
    {
        var positions = await _store.ReadAllAsync<OptionsPosition>(ct);
        var existing = positions.FindIndex(p => p.Id == position.Id);
        if (existing >= 0)
            positions[existing] = position;
        else
            positions.Add(position);

        await _store.WriteAllAsync(positions, ct);
        return position;
    }

    public async Task<OptionsPosition?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var positions = await _store.ReadAllAsync<OptionsPosition>(ct);
        return positions.FirstOrDefault(p => p.Id == id);
    }

    public async Task<List<OptionsPosition>> GetOpenPositionsAsync(CancellationToken ct = default)
    {
        var positions = await _store.ReadAllAsync<OptionsPosition>(ct);
        return positions.Where(p => p.Status == OptionsPositionStatus.Open).ToList();
    }

    public async Task<List<OptionsPosition>> GetByUnderlyingAsync(string symbol, CancellationToken ct = default)
    {
        var positions = await _store.ReadAllAsync<OptionsPosition>(ct);
        return positions
            .Where(p => p.UnderlyingSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<List<OptionsPosition>> GetByDateRangeAsync(DateTime start, DateTime end,
        CancellationToken ct = default)
    {
        var positions = await _store.ReadAllAsync<OptionsPosition>(ct);
        return positions.Where(p => p.OpenedAt >= start && p.OpenedAt <= end).ToList();
    }

    public async Task UpdateAsync(OptionsPosition position, CancellationToken ct = default)
    {
        var positions = await _store.ReadAllAsync<OptionsPosition>(ct);
        var index = positions.FindIndex(p => p.Id == position.Id);
        if (index < 0)
            throw new InvalidOperationException($"OptionsPosition {position.Id} not found.");

        position.LastUpdated = DateTime.UtcNow;
        positions[index] = position;
        await _store.WriteAllAsync(positions, ct);
    }
}
