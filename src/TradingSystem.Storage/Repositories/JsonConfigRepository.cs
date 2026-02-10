using System.Text.Json;
using Microsoft.Extensions.Options;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;

namespace TradingSystem.Storage.Repositories;

public class JsonConfigRepository : IConfigRepository
{
    private readonly JsonFileStore _configStore;
    private readonly JsonFileStore _settingsStore;

    public JsonConfigRepository(IOptions<LocalStorageConfig> config)
    {
        var dir = config.Value.DataDirectory;
        _configStore = new JsonFileStore(Path.Combine(dir, "config.json"));
        _settingsStore = new JsonFileStore(Path.Combine(dir, "settings.json"));
    }

    public JsonConfigRepository(string dataDirectory)
    {
        _configStore = new JsonFileStore(Path.Combine(dataDirectory, "config.json"));
        _settingsStore = new JsonFileStore(Path.Combine(dataDirectory, "settings.json"));
    }

    public async Task<TradingSystemConfig> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configStore.ReadObjectAsync<TradingSystemConfig>(cancellationToken);
        return config ?? new TradingSystemConfig();
    }

    public async Task SaveConfigAsync(TradingSystemConfig config, CancellationToken cancellationToken = default)
    {
        await _configStore.WriteObjectAsync(config, cancellationToken);
    }

    public async Task<T?> GetSettingAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.ReadObjectAsync<Dictionary<string, JsonElement>>(cancellationToken);
        if (settings == null || !settings.TryGetValue(key, out var element))
            return default;

        return JsonSerializer.Deserialize<T>(element.GetRawText(), JsonFileStore.SerializerOptions);
    }

    public async Task SetSettingAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.ReadObjectAsync<Dictionary<string, JsonElement>>(cancellationToken)
            ?? new Dictionary<string, JsonElement>();

        var json = JsonSerializer.Serialize(value, JsonFileStore.SerializerOptions);
        settings[key] = JsonSerializer.Deserialize<JsonElement>(json);

        await _settingsStore.WriteObjectAsync(settings, cancellationToken);
    }
}
