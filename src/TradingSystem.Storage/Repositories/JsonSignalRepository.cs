using Microsoft.Extensions.Options;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Storage.Repositories;

public class JsonSignalRepository : ISignalRepository
{
    private readonly JsonFileStore _store;

    public JsonSignalRepository(IOptions<LocalStorageConfig> config)
    {
        _store = new JsonFileStore(Path.Combine(config.Value.DataDirectory, "signals.json"));
    }

    public JsonSignalRepository(string dataDirectory)
    {
        _store = new JsonFileStore(Path.Combine(dataDirectory, "signals.json"));
    }

    public async Task<Signal> SaveAsync(Signal signal, CancellationToken cancellationToken = default)
    {
        var signals = await _store.ReadAllAsync<Signal>(cancellationToken);
        var existing = signals.FindIndex(s => s.Id == signal.Id);
        if (existing >= 0)
            signals[existing] = signal;
        else
            signals.Add(signal);

        await _store.WriteAllAsync(signals, cancellationToken);
        return signal;
    }

    public async Task<Signal?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var signals = await _store.ReadAllAsync<Signal>(cancellationToken);
        return signals.FirstOrDefault(s => s.Id == id);
    }

    public async Task<List<Signal>> GetActiveSignalsAsync(CancellationToken cancellationToken = default)
    {
        var signals = await _store.ReadAllAsync<Signal>(cancellationToken);
        return signals.Where(s => s.Status == SignalStatus.Active).ToList();
    }

    public async Task<List<Signal>> GetByStrategyAsync(string strategyId, DateTime? since = null,
        CancellationToken cancellationToken = default)
    {
        var signals = await _store.ReadAllAsync<Signal>(cancellationToken);
        var query = signals.Where(s => s.StrategyId == strategyId);
        if (since.HasValue)
            query = query.Where(s => s.GeneratedAt >= since.Value);
        return query.ToList();
    }

    public async Task UpdateStatusAsync(string id, SignalStatus status, string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var signals = await _store.ReadAllAsync<Signal>(cancellationToken);
        var signal = signals.FirstOrDefault(s => s.Id == id);
        if (signal == null)
            throw new InvalidOperationException($"Signal {id} not found");

        signal.Status = status;
        if (notes != null)
            signal.ExecutionNotes = notes;

        await _store.WriteAllAsync(signals, cancellationToken);
    }
}
