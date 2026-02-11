using Microsoft.Extensions.Options;
using TradingSystem.Core.Models;
using TradingSystem.Storage;
using TradingSystem.Storage.Repositories;
using Xunit;

namespace TradingSystem.Tests.Storage;

public class JsonIVHistoryRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly JsonIVHistoryRepository _repo;

    public JsonIVHistoryRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"trading-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var config = Microsoft.Extensions.Options.Options.Create(new LocalStorageConfig { DataDirectory = _tempDir });
        _repo = new JsonIVHistoryRepository(config);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task GetAsync_NoFile_ReturnsNull()
    {
        var result = await _repo.GetAsync("AAPL");
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAndGet_RoundTrips()
    {
        var history = new IVHistory
        {
            Symbol = "AAPL",
            DataPoints = new List<IVHistoryPoint>
            {
                new() { Date = DateTime.Today.AddDays(-2), ImpliedVolatility = 0.25m },
                new() { Date = DateTime.Today.AddDays(-1), ImpliedVolatility = 0.27m },
                new() { Date = DateTime.Today, ImpliedVolatility = 0.30m }
            },
            LastUpdated = DateTime.UtcNow
        };

        await _repo.SaveAsync(history);
        var loaded = await _repo.GetAsync("AAPL");

        Assert.NotNull(loaded);
        Assert.Equal("AAPL", loaded!.Symbol);
        Assert.Equal(3, loaded.DataPoints.Count);
        Assert.Equal(0.30m, loaded.DataPoints[2].ImpliedVolatility);
    }

    [Fact]
    public async Task GetAsync_StaleData_ReturnsNull()
    {
        // Save data with LastUpdated set to yesterday
        var history = new IVHistory
        {
            Symbol = "MSFT",
            DataPoints = new List<IVHistoryPoint>
            {
                new() { Date = DateTime.Today.AddDays(-1), ImpliedVolatility = 0.20m }
            },
            LastUpdated = DateTime.UtcNow.AddDays(-1) // Yesterday
        };

        await _repo.SaveAsync(history);

        // Manually set LastUpdated to yesterday by re-writing the file
        // (SaveAsync sets LastUpdated = UtcNow, so we need to manipulate the file)
        var filePath = Path.Combine(_tempDir, "iv-history", "MSFT.json");
        var json = await File.ReadAllTextAsync(filePath);
        // The repo checks history.LastUpdated.Date < DateTime.Today
        // Since SaveAsync just set it to UtcNow, it won't be stale yet.
        // We need to create a repo that reads the file after we've set the date to yesterday.
        var staleHistory = new IVHistory
        {
            Symbol = "MSFT",
            DataPoints = new List<IVHistoryPoint>
            {
                new() { Date = DateTime.Today.AddDays(-1), ImpliedVolatility = 0.20m }
            },
            LastUpdated = DateTime.UtcNow.AddDays(-1)
        };

        // Write directly to file to bypass SaveAsync's UtcNow override
        var store = new JsonFileStore(filePath);
        await store.WriteObjectAsync(staleHistory);

        var result = await _repo.GetAsync("MSFT");
        Assert.Null(result); // Should be null because LastUpdated is yesterday
    }

    [Fact]
    public async Task SaveAsync_SetsLastUpdated()
    {
        var before = DateTime.UtcNow;
        var history = new IVHistory
        {
            Symbol = "GOOG",
            DataPoints = new List<IVHistoryPoint>
            {
                new() { Date = DateTime.Today, ImpliedVolatility = 0.22m }
            },
            LastUpdated = DateTime.MinValue // Will be overwritten by SaveAsync
        };

        await _repo.SaveAsync(history);
        var after = DateTime.UtcNow;

        // SaveAsync should have set LastUpdated to UtcNow
        Assert.True(history.LastUpdated >= before);
        Assert.True(history.LastUpdated <= after);
    }

    [Fact]
    public async Task GetAsync_DifferentSymbols_IndependentFiles()
    {
        var aaplHistory = new IVHistory
        {
            Symbol = "AAPL",
            DataPoints = new List<IVHistoryPoint>
            {
                new() { Date = DateTime.Today, ImpliedVolatility = 0.30m }
            }
        };

        var msftHistory = new IVHistory
        {
            Symbol = "MSFT",
            DataPoints = new List<IVHistoryPoint>
            {
                new() { Date = DateTime.Today, ImpliedVolatility = 0.25m }
            }
        };

        await _repo.SaveAsync(aaplHistory);
        await _repo.SaveAsync(msftHistory);

        var loadedAapl = await _repo.GetAsync("AAPL");
        var loadedMsft = await _repo.GetAsync("MSFT");

        Assert.NotNull(loadedAapl);
        Assert.NotNull(loadedMsft);
        Assert.Equal(0.30m, loadedAapl!.DataPoints[0].ImpliedVolatility);
        Assert.Equal(0.25m, loadedMsft!.DataPoints[0].ImpliedVolatility);
    }
}
