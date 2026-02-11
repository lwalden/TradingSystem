using TradingSystem.Storage;
using Xunit;

namespace TradingSystem.Tests.Storage;

public class JsonFileStoreTests : IDisposable
{
    private readonly string _testDir;

    public JsonFileStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"ts-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public async Task ReadAll_NonExistentFile_ReturnsEmptyList()
    {
        var store = new JsonFileStore(Path.Combine(_testDir, "missing.json"));

        var result = await store.ReadAllAsync<TestEntity>();

        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadAll_EmptyFile_ReturnsEmptyList()
    {
        var filePath = Path.Combine(_testDir, "empty.json");
        await File.WriteAllTextAsync(filePath, "");
        var store = new JsonFileStore(filePath);

        var result = await store.ReadAllAsync<TestEntity>();

        Assert.Empty(result);
    }

    [Fact]
    public async Task WriteAll_ThenReadAll_RoundTrips()
    {
        var store = new JsonFileStore(Path.Combine(_testDir, "roundtrip.json"));
        var items = new List<TestEntity>
        {
            new() { Id = "1", Name = "Alpha", Value = 42.5m },
            new() { Id = "2", Name = "Beta", Value = 99.9m }
        };

        await store.WriteAllAsync(items);
        var result = await store.ReadAllAsync<TestEntity>();

        Assert.Equal(2, result.Count);
        Assert.Equal("1", result[0].Id);
        Assert.Equal("Alpha", result[0].Name);
        Assert.Equal(42.5m, result[0].Value);
        Assert.Equal("2", result[1].Id);
        Assert.Equal("Beta", result[1].Name);
    }

    [Fact]
    public async Task WriteAll_CreatesDirectoryIfMissing()
    {
        var nestedDir = Path.Combine(_testDir, "sub", "deep");
        var store = new JsonFileStore(Path.Combine(nestedDir, "data.json"));

        await store.WriteAllAsync(new List<TestEntity> { new() { Id = "1", Name = "Test" } });

        Assert.True(Directory.Exists(nestedDir));
        var result = await store.ReadAllAsync<TestEntity>();
        Assert.Single(result);
    }

    [Fact]
    public async Task WriteAll_OverwritesExistingData()
    {
        var store = new JsonFileStore(Path.Combine(_testDir, "overwrite.json"));

        await store.WriteAllAsync(new List<TestEntity> { new() { Id = "1", Name = "Original" } });
        await store.WriteAllAsync(new List<TestEntity> { new() { Id = "2", Name = "Replaced" } });

        var result = await store.ReadAllAsync<TestEntity>();
        Assert.Single(result);
        Assert.Equal("2", result[0].Id);
        Assert.Equal("Replaced", result[0].Name);
    }

    [Fact]
    public async Task WriteAll_ProducesHumanReadableJson()
    {
        var filePath = Path.Combine(_testDir, "readable.json");
        var store = new JsonFileStore(filePath);

        await store.WriteAllAsync(new List<TestEntity> { new() { Id = "1", Name = "Test", Value = 10m } });

        var json = await File.ReadAllTextAsync(filePath);
        Assert.Contains("\n", json); // Indented
        Assert.Contains("\"id\"", json); // camelCase
        Assert.Contains("\"name\"", json);
    }

    [Fact]
    public async Task WriteAll_SerializesEnumsAsStrings()
    {
        var filePath = Path.Combine(_testDir, "enums.json");
        var store = new JsonFileStore(filePath);

        await store.WriteAllAsync(new List<TestEntityWithEnum>
        {
            new() { Id = "1", Status = TestStatus.Active }
        });

        var json = await File.ReadAllTextAsync(filePath);
        Assert.Contains("\"Active\"", json);
        Assert.DoesNotContain("\"0\"", json); // Not numeric
    }

    [Fact]
    public async Task ConcurrentWrites_DoNotCorrupt()
    {
        var store = new JsonFileStore(Path.Combine(_testDir, "concurrent.json"));

        // Run 20 concurrent writes
        var tasks = Enumerable.Range(0, 20).Select(async i =>
        {
            var items = new List<TestEntity> { new() { Id = i.ToString(), Name = $"Item{i}" } };
            await store.WriteAllAsync(items);
        });

        await Task.WhenAll(tasks);

        // File should be valid JSON with exactly 1 item (last writer wins)
        var result = await store.ReadAllAsync<TestEntity>();
        Assert.Single(result);
    }

    [Fact]
    public async Task WriteAll_NoTempFileLeftBehind()
    {
        var filePath = Path.Combine(_testDir, "clean.json");
        var store = new JsonFileStore(filePath);

        await store.WriteAllAsync(new List<TestEntity> { new() { Id = "1" } });

        Assert.False(File.Exists(filePath + ".tmp"));
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task ReadObject_NonExistentFile_ReturnsNull()
    {
        var store = new JsonFileStore(Path.Combine(_testDir, "missing-obj.json"));

        var result = await store.ReadObjectAsync<TestEntity>();

        Assert.Null(result);
    }

    [Fact]
    public async Task WriteObject_ThenReadObject_RoundTrips()
    {
        var store = new JsonFileStore(Path.Combine(_testDir, "obj.json"));
        var entity = new TestEntity { Id = "1", Name = "Config", Value = 100m };

        await store.WriteObjectAsync(entity);
        var result = await store.ReadObjectAsync<TestEntity>();

        Assert.NotNull(result);
        Assert.Equal("1", result.Id);
        Assert.Equal("Config", result.Name);
        Assert.Equal(100m, result.Value);
    }

    // Test models
    public class TestEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Value { get; set; }
    }

    public class TestEntityWithEnum
    {
        public string Id { get; set; } = string.Empty;
        public TestStatus Status { get; set; }
    }

    public enum TestStatus { Active, Inactive }
}
