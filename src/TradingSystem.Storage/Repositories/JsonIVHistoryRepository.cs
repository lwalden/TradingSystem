using Microsoft.Extensions.Options;
using TradingSystem.Core.Models;

namespace TradingSystem.Storage.Repositories;

/// <summary>
/// Persists IV history to JSON files for caching. One file per symbol.
/// Data is considered stale after the current trading day.
/// </summary>
public class JsonIVHistoryRepository
{
    private readonly string _dataDirectory;

    public JsonIVHistoryRepository(IOptions<LocalStorageConfig> config)
    {
        _dataDirectory = Path.Combine(config.Value.DataDirectory, "iv-history");
    }

    public async Task<IVHistory?> GetAsync(string symbol, CancellationToken ct = default)
    {
        var store = GetStore(symbol);
        var history = await store.ReadObjectAsync<IVHistory>(ct);

        // SaveAsync writes UTC timestamps, so staleness should use UTC day boundaries.
        if (history != null && history.LastUpdated.Date < DateTime.UtcNow.Date)
            return null;

        return history;
    }

    public async Task SaveAsync(IVHistory history, CancellationToken ct = default)
    {
        var store = GetStore(history.Symbol);
        history.LastUpdated = DateTime.UtcNow;
        await store.WriteObjectAsync(history, ct);
    }

    private JsonFileStore GetStore(string symbol)
    {
        return new JsonFileStore(Path.Combine(_dataDirectory, $"{symbol.ToUpperInvariant()}.json"));
    }
}
