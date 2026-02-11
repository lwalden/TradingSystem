using Microsoft.Extensions.Options;
using TradingSystem.Core.Interfaces;

namespace TradingSystem.Storage.Repositories;

public class JsonSnapshotRepository : ISnapshotRepository
{
    private readonly JsonFileStore _store;

    public JsonSnapshotRepository(IOptions<LocalStorageConfig> config)
    {
        _store = new JsonFileStore(Path.Combine(config.Value.DataDirectory, "snapshots.json"));
    }

    public JsonSnapshotRepository(string dataDirectory)
    {
        _store = new JsonFileStore(Path.Combine(dataDirectory, "snapshots.json"));
    }

    public async Task SaveDailySnapshotAsync(DailySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var snapshots = await _store.ReadAllAsync<DailySnapshot>(cancellationToken);
        var existing = snapshots.FindIndex(s => s.Date.Date == snapshot.Date.Date);
        if (existing >= 0)
            snapshots[existing] = snapshot;
        else
            snapshots.Add(snapshot);

        await _store.WriteAllAsync(snapshots, cancellationToken);
    }

    public async Task<DailySnapshot?> GetSnapshotAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var snapshots = await _store.ReadAllAsync<DailySnapshot>(cancellationToken);
        return snapshots.FirstOrDefault(s => s.Date.Date == date.Date);
    }

    public async Task<List<DailySnapshot>> GetSnapshotsAsync(DateTime startDate, DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        var snapshots = await _store.ReadAllAsync<DailySnapshot>(cancellationToken);
        return snapshots.Where(s => s.Date.Date >= startDate.Date && s.Date.Date <= endDate.Date)
            .OrderBy(s => s.Date)
            .ToList();
    }
}
