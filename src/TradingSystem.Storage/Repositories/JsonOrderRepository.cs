using Microsoft.Extensions.Options;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Storage.Repositories;

public class JsonOrderRepository : IOrderRepository
{
    private readonly JsonFileStore _store;

    public JsonOrderRepository(IOptions<LocalStorageConfig> config)
    {
        _store = new JsonFileStore(Path.Combine(config.Value.DataDirectory, "orders.json"));
    }

    public JsonOrderRepository(string dataDirectory)
    {
        _store = new JsonFileStore(Path.Combine(dataDirectory, "orders.json"));
    }

    public async Task<Order> SaveAsync(Order order, CancellationToken cancellationToken = default)
    {
        var orders = await _store.ReadAllAsync<Order>(cancellationToken);
        var existing = orders.FindIndex(o => o.Id == order.Id);
        if (existing >= 0)
            orders[existing] = order;
        else
            orders.Add(order);

        await _store.WriteAllAsync(orders, cancellationToken);
        return order;
    }

    public async Task<Order?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var orders = await _store.ReadAllAsync<Order>(cancellationToken);
        return orders.FirstOrDefault(o => o.Id == id);
    }

    public async Task<Order?> GetByBrokerIdAsync(string brokerId, CancellationToken cancellationToken = default)
    {
        var orders = await _store.ReadAllAsync<Order>(cancellationToken);
        return orders.FirstOrDefault(o => o.BrokerId == brokerId);
    }

    public async Task<List<Order>> GetOpenOrdersAsync(CancellationToken cancellationToken = default)
    {
        var orders = await _store.ReadAllAsync<Order>(cancellationToken);
        return orders.Where(o => o.Status is OrderStatus.PendingSubmit
            or OrderStatus.Submitted or OrderStatus.Accepted
            or OrderStatus.PartiallyFilled).ToList();
    }

    public async Task<List<Order>> GetByDateRangeAsync(DateTime startDate, DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        var orders = await _store.ReadAllAsync<Order>(cancellationToken);
        return orders.Where(o => o.CreatedAt >= startDate && o.CreatedAt <= endDate).ToList();
    }

    public async Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        var orders = await _store.ReadAllAsync<Order>(cancellationToken);
        var index = orders.FindIndex(o => o.Id == order.Id);
        if (index < 0)
            throw new InvalidOperationException($"Order {order.Id} not found");

        orders[index] = order;
        await _store.WriteAllAsync(orders, cancellationToken);
    }
}
