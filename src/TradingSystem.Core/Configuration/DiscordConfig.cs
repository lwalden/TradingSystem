namespace TradingSystem.Core.Configuration;

public class DiscordConfig
{
    public bool Enabled { get; set; } = true;
    public string WebhookUrl { get; set; } = string.Empty;
    public string Username { get; set; } = "TradingSystem Risk";
}
