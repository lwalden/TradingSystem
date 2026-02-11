using TradingSystem.Core.Configuration;
using TradingSystem.Storage.Repositories;
using Xunit;

namespace TradingSystem.Tests.Storage;

public class JsonConfigRepositoryTests : IDisposable
{
    private readonly string _testDir;
    private readonly JsonConfigRepository _repo;

    public JsonConfigRepositoryTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"ts-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _repo = new JsonConfigRepository(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public async Task GetConfig_WhenNoFile_ReturnsDefaults()
    {
        var config = await _repo.GetConfigAsync();

        Assert.NotNull(config);
        Assert.Equal(TradingMode.Sandbox, config.Mode);
        Assert.Equal(0.70m, config.IncomeTargetPercent);
        Assert.Equal(0.30m, config.TacticalTargetPercent);
    }

    [Fact]
    public async Task SaveConfig_ThenGetConfig_RoundTrips()
    {
        var config = new TradingSystemConfig
        {
            Mode = TradingMode.Sandbox,
            IncomeTargetPercent = 0.65m,
            TacticalTargetPercent = 0.35m
        };

        await _repo.SaveConfigAsync(config);
        var result = await _repo.GetConfigAsync();

        Assert.Equal(TradingMode.Sandbox, result.Mode);
        Assert.Equal(0.65m, result.IncomeTargetPercent);
        Assert.Equal(0.35m, result.TacticalTargetPercent);
    }

    [Fact]
    public async Task SaveConfig_PreservesNestedObjects()
    {
        var config = new TradingSystemConfig();
        config.Risk.RiskPerTradePercent = 0.005m;
        config.Income.MaxIssuerPercent = 0.08m;

        await _repo.SaveConfigAsync(config);
        var result = await _repo.GetConfigAsync();

        Assert.Equal(0.005m, result.Risk.RiskPerTradePercent);
        Assert.Equal(0.08m, result.Income.MaxIssuerPercent);
    }

    [Fact]
    public async Task SetSetting_ThenGetSetting_String()
    {
        await _repo.SetSettingAsync("apiKey", "test-key-123");

        var result = await _repo.GetSettingAsync<string>("apiKey");

        Assert.Equal("test-key-123", result);
    }

    [Fact]
    public async Task SetSetting_ThenGetSetting_Int()
    {
        await _repo.SetSettingAsync("maxRetries", 5);

        var result = await _repo.GetSettingAsync<int>("maxRetries");

        Assert.Equal(5, result);
    }

    [Fact]
    public async Task SetSetting_ThenGetSetting_Bool()
    {
        await _repo.SetSettingAsync("featureEnabled", true);

        var result = await _repo.GetSettingAsync<bool>("featureEnabled");

        Assert.True(result);
    }

    [Fact]
    public async Task GetSetting_MissingKey_ReturnsDefault()
    {
        var stringResult = await _repo.GetSettingAsync<string>("missing");
        var intResult = await _repo.GetSettingAsync<int>("missing");
        var boolResult = await _repo.GetSettingAsync<bool>("missing");

        Assert.Null(stringResult);
        Assert.Equal(0, intResult);
        Assert.False(boolResult);
    }

    [Fact]
    public async Task SetSetting_OverwritesExisting()
    {
        await _repo.SetSettingAsync("key", "original");
        await _repo.SetSettingAsync("key", "updated");

        var result = await _repo.GetSettingAsync<string>("key");

        Assert.Equal("updated", result);
    }

    [Fact]
    public async Task MultipleSettings_IndependentStorage()
    {
        await _repo.SetSettingAsync("key1", "value1");
        await _repo.SetSettingAsync("key2", 42);

        var result1 = await _repo.GetSettingAsync<string>("key1");
        var result2 = await _repo.GetSettingAsync<int>("key2");

        Assert.Equal("value1", result1);
        Assert.Equal(42, result2);
    }
}
