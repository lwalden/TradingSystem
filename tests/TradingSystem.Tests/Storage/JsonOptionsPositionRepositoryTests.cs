using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Storage.Repositories;
using Xunit;

namespace TradingSystem.Tests.Storage;

public class JsonOptionsPositionRepositoryTests : IDisposable
{
    private readonly string _testDir;
    private readonly JsonOptionsPositionRepository _repo;

    public JsonOptionsPositionRepositoryTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "trading-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);
        _repo = new JsonOptionsPositionRepository(_testDir);
    }

    [Fact]
    public async Task SaveAndGetById_RoundTrip()
    {
        var position = CreateTestPosition("pos-1", "SPY");

        await _repo.SaveAsync(position);
        var loaded = await _repo.GetByIdAsync("pos-1");

        Assert.NotNull(loaded);
        Assert.Equal("SPY", loaded!.UnderlyingSymbol);
        Assert.Equal(StrategyType.BullPutSpread, loaded.Strategy);
        Assert.Equal(1.25m, loaded.EntryNetCredit);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        var result = await _repo.GetByIdAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOpenPositionsAsync_ReturnsOnlyOpen()
    {
        await _repo.SaveAsync(CreateTestPosition("pos-1", "SPY", OptionsPositionStatus.Open));
        await _repo.SaveAsync(CreateTestPosition("pos-2", "AAPL", OptionsPositionStatus.Closed));
        await _repo.SaveAsync(CreateTestPosition("pos-3", "QQQ", OptionsPositionStatus.Open));

        var open = await _repo.GetOpenPositionsAsync();

        Assert.Equal(2, open.Count);
        Assert.All(open, p => Assert.Equal(OptionsPositionStatus.Open, p.Status));
    }

    [Fact]
    public async Task GetOpenPositionsAsync_EmptyStore_ReturnsEmpty()
    {
        var open = await _repo.GetOpenPositionsAsync();

        Assert.Empty(open);
    }

    [Fact]
    public async Task GetByUnderlyingAsync_FiltersBySymbol()
    {
        await _repo.SaveAsync(CreateTestPosition("pos-1", "SPY"));
        await _repo.SaveAsync(CreateTestPosition("pos-2", "AAPL"));
        await _repo.SaveAsync(CreateTestPosition("pos-3", "SPY"));

        var spyPositions = await _repo.GetByUnderlyingAsync("SPY");

        Assert.Equal(2, spyPositions.Count);
        Assert.All(spyPositions, p => Assert.Equal("SPY", p.UnderlyingSymbol));
    }

    [Fact]
    public async Task GetByUnderlyingAsync_CaseInsensitive()
    {
        await _repo.SaveAsync(CreateTestPosition("pos-1", "SPY"));

        var result = await _repo.GetByUnderlyingAsync("spy");

        Assert.Single(result);
    }

    [Fact]
    public async Task GetByDateRangeAsync_FiltersCorrectly()
    {
        var pos1 = CreateTestPosition("pos-1", "SPY");
        pos1.OpenedAt = new DateTime(2026, 1, 15);
        var pos2 = CreateTestPosition("pos-2", "AAPL");
        pos2.OpenedAt = new DateTime(2026, 2, 10);
        var pos3 = CreateTestPosition("pos-3", "QQQ");
        pos3.OpenedAt = new DateTime(2026, 3, 5);

        await _repo.SaveAsync(pos1);
        await _repo.SaveAsync(pos2);
        await _repo.SaveAsync(pos3);

        var result = await _repo.GetByDateRangeAsync(
            new DateTime(2026, 2, 1), new DateTime(2026, 2, 28));

        Assert.Single(result);
        Assert.Equal("AAPL", result[0].UnderlyingSymbol);
    }

    [Fact]
    public async Task UpdateAsync_ModifiesExistingPosition()
    {
        var position = CreateTestPosition("pos-1", "SPY");
        await _repo.SaveAsync(position);

        position.CurrentValue = 0.25m;
        position.Status = OptionsPositionStatus.ProfitTargetReached;
        await _repo.UpdateAsync(position);

        var loaded = await _repo.GetByIdAsync("pos-1");
        Assert.Equal(0.25m, loaded!.CurrentValue);
        Assert.Equal(OptionsPositionStatus.ProfitTargetReached, loaded.Status);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_Throws()
    {
        var position = CreateTestPosition("nonexistent", "SPY");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repo.UpdateAsync(position));
    }

    [Fact]
    public async Task SaveAsync_UpdatesExistingById()
    {
        var position = CreateTestPosition("pos-1", "SPY");
        await _repo.SaveAsync(position);

        position.EntryNetCredit = 2.50m;
        await _repo.SaveAsync(position);

        var all = await _repo.GetOpenPositionsAsync();
        Assert.Single(all);
        Assert.Equal(2.50m, all[0].EntryNetCredit);
    }

    [Fact]
    public async Task SaveAsync_PreservesLegs()
    {
        var position = CreateTestPosition("pos-1", "SPY");
        position.Legs = new List<OptionsPositionLeg>
        {
            new()
            {
                Symbol = "SPY260320P00580000",
                Strike = 580m,
                Expiration = new DateTime(2026, 3, 20),
                Right = OptionRight.Put,
                Action = OrderAction.Sell,
                EntryPrice = 3.50m,
                ConId = 123456
            },
            new()
            {
                Symbol = "SPY260320P00575000",
                Strike = 575m,
                Expiration = new DateTime(2026, 3, 20),
                Right = OptionRight.Put,
                Action = OrderAction.Buy,
                EntryPrice = 2.25m,
                ConId = 123457
            }
        };

        await _repo.SaveAsync(position);
        var loaded = await _repo.GetByIdAsync("pos-1");

        Assert.Equal(2, loaded!.Legs.Count);
        Assert.Equal(580m, loaded.Legs[0].Strike);
        Assert.Equal(OrderAction.Sell, loaded.Legs[0].Action);
        Assert.Equal(123456, loaded.Legs[0].ConId);
    }

    private static OptionsPosition CreateTestPosition(
        string id, string underlying,
        OptionsPositionStatus status = OptionsPositionStatus.Open)
    {
        return new OptionsPosition
        {
            Id = id,
            UnderlyingSymbol = underlying,
            Strategy = StrategyType.BullPutSpread,
            EntryNetCredit = 1.25m,
            MaxProfit = 1.25m,
            MaxLoss = 3.75m,
            CurrentValue = 0.80m,
            Status = status,
            Expiration = DateTime.Today.AddDays(30),
            OpenedAt = DateTime.UtcNow
        };
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }
        catch { /* cleanup best effort */ }
    }
}
