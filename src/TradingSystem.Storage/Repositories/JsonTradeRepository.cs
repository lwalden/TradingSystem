using Microsoft.Extensions.Options;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Storage.Repositories;

public class JsonTradeRepository : ITradeRepository
{
    private readonly JsonFileStore _store;

    public JsonTradeRepository(IOptions<LocalStorageConfig> config)
    {
        _store = new JsonFileStore(Path.Combine(config.Value.DataDirectory, "trades.json"));
    }

    public JsonTradeRepository(string dataDirectory)
    {
        _store = new JsonFileStore(Path.Combine(dataDirectory, "trades.json"));
    }

    public async Task<Trade> SaveAsync(Trade trade, CancellationToken cancellationToken = default)
    {
        var trades = await _store.ReadAllAsync<Trade>(cancellationToken);
        var existing = trades.FindIndex(t => t.Id == trade.Id);
        if (existing >= 0)
            trades[existing] = trade;
        else
            trades.Add(trade);

        await _store.WriteAllAsync(trades, cancellationToken);
        return trade;
    }

    public async Task<Trade?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var trades = await _store.ReadAllAsync<Trade>(cancellationToken);
        return trades.FirstOrDefault(t => t.Id == id);
    }

    public async Task<List<Trade>> GetByDateRangeAsync(DateTime startDate, DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        var trades = await _store.ReadAllAsync<Trade>(cancellationToken);
        return trades.Where(t => t.EntryTime >= startDate && t.EntryTime <= endDate).ToList();
    }

    public async Task<List<Trade>> GetByStrategyAsync(string strategyId, DateTime? since = null,
        CancellationToken cancellationToken = default)
    {
        var trades = await _store.ReadAllAsync<Trade>(cancellationToken);
        var query = trades.Where(t => t.StrategyId == strategyId);
        if (since.HasValue)
            query = query.Where(t => t.EntryTime >= since.Value);
        return query.ToList();
    }

    public async Task<List<Trade>> GetOpenTradesAsync(CancellationToken cancellationToken = default)
    {
        var trades = await _store.ReadAllAsync<Trade>(cancellationToken);
        return trades.Where(t => !t.ExitTime.HasValue).ToList();
    }

    public async Task<TradeStatistics> GetStatisticsAsync(DateTime? since = null, string? strategyId = null,
        CancellationToken cancellationToken = default)
    {
        var trades = await _store.ReadAllAsync<Trade>(cancellationToken);
        var query = trades.Where(t => t.ExitTime.HasValue); // Only closed trades

        if (since.HasValue)
            query = query.Where(t => t.EntryTime >= since.Value);
        if (strategyId != null)
            query = query.Where(t => t.StrategyId == strategyId);

        var closedTrades = query.ToList();
        if (closedTrades.Count == 0)
            return new TradeStatistics();

        var winners = closedTrades.Where(t => (t.RealizedPnL ?? 0) > 0).ToList();
        var losers = closedTrades.Where(t => (t.RealizedPnL ?? 0) <= 0).ToList();

        return new TradeStatistics
        {
            TotalTrades = closedTrades.Count,
            WinningTrades = winners.Count,
            LosingTrades = losers.Count,
            TotalPnL = closedTrades.Sum(t => t.RealizedPnL ?? 0),
            AveragePnL = closedTrades.Average(t => t.RealizedPnL ?? 0),
            AverageWin = winners.Count > 0 ? winners.Average(t => t.RealizedPnL ?? 0) : 0,
            AverageLoss = losers.Count > 0 ? losers.Average(t => t.RealizedPnL ?? 0) : 0,
            AverageRMultiple = closedTrades.Average(t => t.RMultiple ?? 0),
            LargestWin = winners.Count > 0 ? winners.Max(t => t.RealizedPnL ?? 0) : 0,
            LargestLoss = losers.Count > 0 ? losers.Min(t => t.RealizedPnL ?? 0) : 0,
            AverageHoldingDays = (int)closedTrades.Average(t =>
                t.ExitTime.HasValue ? (t.ExitTime.Value - t.EntryTime).TotalDays : 0),
            MaxConsecutiveWins = CalculateMaxConsecutive(closedTrades, win: true),
            MaxConsecutiveLosses = CalculateMaxConsecutive(closedTrades, win: false),
            ExpectancyPerTrade = closedTrades.Average(t => t.RealizedPnL ?? 0)
        };
    }

    private static int CalculateMaxConsecutive(List<Trade> trades, bool win)
    {
        int max = 0, current = 0;
        foreach (var trade in trades.OrderBy(t => t.EntryTime))
        {
            bool isWin = (trade.RealizedPnL ?? 0) > 0;
            if (isWin == win)
            {
                current++;
                max = Math.Max(max, current);
            }
            else
            {
                current = 0;
            }
        }
        return max;
    }
}
