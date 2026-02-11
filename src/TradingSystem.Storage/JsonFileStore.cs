using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradingSystem.Storage;

/// <summary>
/// Generic JSON file store with thread-safe read/write and atomic writes.
/// Each instance manages a single JSON file containing a list of entities.
/// </summary>
public class JsonFileStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonFileStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<List<T>> ReadAllAsync<T>(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
                return new List<T>();

            var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
                return new List<T>();

            return JsonSerializer.Deserialize<List<T>>(json, SerializerOptions) ?? new List<T>();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task WriteAllAsync<T>(List<T> items, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(items, SerializerOptions);

            // Atomic write: write to temp file, then move to target
            var tempPath = _filePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json, cancellationToken);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Read a single object (not a list) from the file.
    /// Used for config storage where the file contains a single object.
    /// </summary>
    public async Task<T?> ReadObjectAsync<T>(CancellationToken cancellationToken = default) where T : class
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
                return null;

            var json = await File.ReadAllTextAsync(_filePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonSerializer.Deserialize<T>(json, SerializerOptions);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Write a single object (not a list) to the file.
    /// </summary>
    public async Task WriteObjectAsync<T>(T item, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(item, SerializerOptions);

            var tempPath = _filePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json, cancellationToken);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }
}
