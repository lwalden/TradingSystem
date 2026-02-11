using TradingSystem.Core.Models;
using TradingSystem.Storage.Repositories;
using Xunit;

namespace TradingSystem.Tests.Storage;

public class JsonOrderRepositoryTests : IDisposable
{
    private readonly string _testDir;
    private readonly JsonOrderRepository _repo;

    public JsonOrderRepositoryTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"ts-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _repo = new JsonOrderRepository(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public async Task Save_ThenGetById_ReturnsOrder()
    {
        var order = CreateOrder("order-1", "AAPL");

        await _repo.SaveAsync(order);
        var result = await _repo.GetByIdAsync("order-1");

        Assert.NotNull(result);
        Assert.Equal("order-1", result.Id);
        Assert.Equal("AAPL", result.Symbol);
    }

    [Fact]
    public async Task GetById_NotFound_ReturnsNull()
    {
        var result = await _repo.GetByIdAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task Save_DuplicateId_UpsertsOrder()
    {
        var order = CreateOrder("order-1", "AAPL");
        await _repo.SaveAsync(order);

        order.Symbol = "MSFT";
        await _repo.SaveAsync(order);

        var result = await _repo.GetByIdAsync("order-1");
        Assert.NotNull(result);
        Assert.Equal("MSFT", result.Symbol);

        var all = await _repo.GetOpenOrdersAsync();
        Assert.Single(all); // Not duplicated
    }

    [Fact]
    public async Task GetByBrokerId_ReturnsMatchingOrder()
    {
        var order = CreateOrder("order-1", "AAPL");
        order.BrokerId = "12345";
        await _repo.SaveAsync(order);

        var result = await _repo.GetByBrokerIdAsync("12345");

        Assert.NotNull(result);
        Assert.Equal("order-1", result.Id);
    }

    [Fact]
    public async Task GetByBrokerId_NotFound_ReturnsNull()
    {
        var result = await _repo.GetByBrokerIdAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOpenOrders_FiltersCorrectly()
    {
        await _repo.SaveAsync(CreateOrder("o1", "AAPL", OrderStatus.PendingSubmit));
        await _repo.SaveAsync(CreateOrder("o2", "MSFT", OrderStatus.Submitted));
        await _repo.SaveAsync(CreateOrder("o3", "GOOG", OrderStatus.Accepted));
        await _repo.SaveAsync(CreateOrder("o4", "AMZN", OrderStatus.PartiallyFilled));
        await _repo.SaveAsync(CreateOrder("o5", "META", OrderStatus.Filled));
        await _repo.SaveAsync(CreateOrder("o6", "TSLA", OrderStatus.Cancelled));
        await _repo.SaveAsync(CreateOrder("o7", "NFLX", OrderStatus.Rejected));

        var open = await _repo.GetOpenOrdersAsync();

        Assert.Equal(4, open.Count);
        Assert.All(open, o => Assert.Contains(o.Status, new[]
        {
            OrderStatus.PendingSubmit, OrderStatus.Submitted,
            OrderStatus.Accepted, OrderStatus.PartiallyFilled
        }));
    }

    [Fact]
    public async Task GetByDateRange_FiltersCorrectly()
    {
        var jan = CreateOrder("o1", "AAPL");
        jan.CreatedAt = new DateTime(2026, 1, 15);
        var feb = CreateOrder("o2", "MSFT");
        feb.CreatedAt = new DateTime(2026, 2, 15);
        var mar = CreateOrder("o3", "GOOG");
        mar.CreatedAt = new DateTime(2026, 3, 15);

        await _repo.SaveAsync(jan);
        await _repo.SaveAsync(feb);
        await _repo.SaveAsync(mar);

        var result = await _repo.GetByDateRangeAsync(
            new DateTime(2026, 2, 1), new DateTime(2026, 2, 28));

        Assert.Single(result);
        Assert.Equal("o2", result[0].Id);
    }

    [Fact]
    public async Task Update_ModifiesExistingOrder()
    {
        var order = CreateOrder("order-1", "AAPL");
        await _repo.SaveAsync(order);

        order.Status = OrderStatus.Filled;
        order.FilledQuantity = 100;
        order.AverageFillPrice = 150.50m;
        await _repo.UpdateAsync(order);

        var result = await _repo.GetByIdAsync("order-1");
        Assert.NotNull(result);
        Assert.Equal(OrderStatus.Filled, result.Status);
        Assert.Equal(100, result.FilledQuantity);
        Assert.Equal(150.50m, result.AverageFillPrice);
    }

    [Fact]
    public async Task Update_OrderNotFound_Throws()
    {
        var order = CreateOrder("nonexistent", "AAPL");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repo.UpdateAsync(order));
    }

    [Fact]
    public async Task Save_PreservesAllOrderFields()
    {
        var order = new Order
        {
            Id = "full-order",
            BrokerId = "99",
            Symbol = "AAPL",
            SecurityType = "STK",
            Action = OrderAction.Buy,
            Quantity = 100,
            OrderType = OrderType.Limit,
            LimitPrice = 150m,
            TimeInForce = TimeInForce.GTC,
            Status = OrderStatus.Submitted,
            Sleeve = SleeveType.Income,
            StrategyId = "income-monthly-reinvest",
            Rationale = "Reduce drift"
        };

        await _repo.SaveAsync(order);
        var result = await _repo.GetByIdAsync("full-order");

        Assert.NotNull(result);
        Assert.Equal("99", result.BrokerId);
        Assert.Equal(OrderAction.Buy, result.Action);
        Assert.Equal(OrderType.Limit, result.OrderType);
        Assert.Equal(150m, result.LimitPrice);
        Assert.Equal(TimeInForce.GTC, result.TimeInForce);
        Assert.Equal(SleeveType.Income, result.Sleeve);
        Assert.Equal("income-monthly-reinvest", result.StrategyId);
    }

    private static Order CreateOrder(string id, string symbol, OrderStatus status = OrderStatus.Submitted)
    {
        return new Order
        {
            Id = id,
            Symbol = symbol,
            Action = OrderAction.Buy,
            Quantity = 100,
            OrderType = OrderType.Limit,
            LimitPrice = 100m,
            Status = status
        };
    }
}
