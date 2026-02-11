using TradingSystem.Core.Models;
using TradingSystem.Storage.Repositories;
using Xunit;

namespace TradingSystem.Tests.Storage;

public class JsonSignalRepositoryTests : IDisposable
{
    private readonly string _testDir;
    private readonly JsonSignalRepository _repo;

    public JsonSignalRepositoryTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"ts-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _repo = new JsonSignalRepository(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public async Task Save_ThenGetById_ReturnsSignal()
    {
        var signal = CreateSignal("sig-1", "strategy-a");

        await _repo.SaveAsync(signal);
        var result = await _repo.GetByIdAsync("sig-1");

        Assert.NotNull(result);
        Assert.Equal("sig-1", result.Id);
        Assert.Equal("strategy-a", result.StrategyId);
    }

    [Fact]
    public async Task GetById_NotFound_ReturnsNull()
    {
        var result = await _repo.GetByIdAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetActiveSignals_ReturnsOnlyActive()
    {
        await _repo.SaveAsync(CreateSignal("s1", "strat", SignalStatus.Active));
        await _repo.SaveAsync(CreateSignal("s2", "strat", SignalStatus.Executed));
        await _repo.SaveAsync(CreateSignal("s3", "strat", SignalStatus.Active));
        await _repo.SaveAsync(CreateSignal("s4", "strat", SignalStatus.Expired));

        var active = await _repo.GetActiveSignalsAsync();

        Assert.Equal(2, active.Count);
        Assert.All(active, s => Assert.Equal(SignalStatus.Active, s.Status));
    }

    [Fact]
    public async Task GetByStrategy_FiltersCorrectly()
    {
        await _repo.SaveAsync(CreateSignal("s1", "alpha"));
        await _repo.SaveAsync(CreateSignal("s2", "beta"));
        await _repo.SaveAsync(CreateSignal("s3", "alpha"));

        var result = await _repo.GetByStrategyAsync("alpha");

        Assert.Equal(2, result.Count);
        Assert.All(result, s => Assert.Equal("alpha", s.StrategyId));
    }

    [Fact]
    public async Task GetByStrategy_WithSinceFilter_FiltersCorrectly()
    {
        var old = CreateSignal("s1", "alpha");
        old.GeneratedAt = new DateTime(2026, 1, 1);
        var recent = CreateSignal("s2", "alpha");
        recent.GeneratedAt = new DateTime(2026, 2, 15);

        await _repo.SaveAsync(old);
        await _repo.SaveAsync(recent);

        var result = await _repo.GetByStrategyAsync("alpha", since: new DateTime(2026, 2, 1));

        Assert.Single(result);
        Assert.Equal("s2", result[0].Id);
    }

    [Fact]
    public async Task UpdateStatus_ChangesStatusAndNotes()
    {
        await _repo.SaveAsync(CreateSignal("s1", "strat"));

        await _repo.UpdateStatusAsync("s1", SignalStatus.Executed, "Filled at $150");

        var result = await _repo.GetByIdAsync("s1");
        Assert.NotNull(result);
        Assert.Equal(SignalStatus.Executed, result.Status);
        Assert.Equal("Filled at $150", result.ExecutionNotes);
    }

    [Fact]
    public async Task UpdateStatus_NullNotes_DoesNotOverwrite()
    {
        var signal = CreateSignal("s1", "strat");
        signal.ExecutionNotes = "Original notes";
        await _repo.SaveAsync(signal);

        await _repo.UpdateStatusAsync("s1", SignalStatus.Cancelled);

        var result = await _repo.GetByIdAsync("s1");
        Assert.NotNull(result);
        Assert.Equal(SignalStatus.Cancelled, result.Status);
        Assert.Equal("Original notes", result.ExecutionNotes);
    }

    [Fact]
    public async Task UpdateStatus_NotFound_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _repo.UpdateStatusAsync("nonexistent", SignalStatus.Cancelled));
    }

    [Fact]
    public async Task Save_PreservesAllFields()
    {
        var signal = new Signal
        {
            Id = "full",
            StrategyId = "income-monthly-reinvest",
            StrategyName = "Monthly Reinvest",
            Symbol = "VIG",
            Direction = SignalDirection.Long,
            Strength = SignalStrength.Moderate,
            SuggestedEntryPrice = 180m,
            SuggestedPositionSize = 50,
            Rationale = "Drift reduction",
            GeneratedAt = new DateTime(2026, 2, 1),
            ExpiresAt = new DateTime(2026, 2, 2)
        };

        await _repo.SaveAsync(signal);
        var result = await _repo.GetByIdAsync("full");

        Assert.NotNull(result);
        Assert.Equal("VIG", result.Symbol);
        Assert.Equal(SignalDirection.Long, result.Direction);
        Assert.Equal(SignalStrength.Moderate, result.Strength);
        Assert.Equal(180m, result.SuggestedEntryPrice);
        Assert.Equal(50, result.SuggestedPositionSize);
    }

    private static Signal CreateSignal(string id, string strategyId,
        SignalStatus status = SignalStatus.Active)
    {
        return new Signal
        {
            Id = id,
            StrategyId = strategyId,
            Symbol = "AAPL",
            Direction = SignalDirection.Long,
            Strength = SignalStrength.Moderate,
            Status = status,
            GeneratedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(8)
        };
    }
}
