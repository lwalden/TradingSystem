namespace TradingSystem.Storage;

public class LocalStorageConfig
{
    public string DataDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "data");
}
