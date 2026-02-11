namespace TradingSystem.Brokers.IBKR;

public class IBKRConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 7497; // 7497 = TWS paper, 7496 = TWS live, 4002 = Gateway paper, 4001 = Gateway live
    public int ClientId { get; set; } = 1;
    public int ConnectionTimeout { get; set; } = 10000; // ms
    public int RequestTimeout { get; set; } = 30000; // ms
    public bool UsePaperAccount { get; set; } = true;

    // Option chain request settings
    public int OptionChainMinDTE { get; set; } = 14;
    public int OptionChainMaxDTE { get; set; } = 60;
    public int MaxConcurrentOptionRequests { get; set; } = 45;
    public int OptionQuoteDelayMs { get; set; } = 100;
}
