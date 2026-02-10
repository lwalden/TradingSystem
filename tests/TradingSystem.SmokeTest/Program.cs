using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Brokers.IBKR;
using TradingSystem.Brokers.IBKR.Services;
using TradingSystem.Core.Models;

// Setup logging
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

var logger = loggerFactory.CreateLogger<IBKRBrokerService>();
var config = Options.Create(new IBKRConfig
{
    Host = "127.0.0.1",
    Port = 7497,
    ClientId = 99, // Use a unique clientId to avoid conflicts with other connections
    ConnectionTimeout = 10000,
    RequestTimeout = 30000
});

using var service = new IBKRBrokerService(logger, config);

Console.WriteLine("=== IBKR Smoke Test ===");
Console.WriteLine();

// 1. Connect
Console.WriteLine("[1/5] Connecting to TWS...");
try
{
    var connected = await service.ConnectAsync();
    if (!connected)
    {
        Console.WriteLine("FAIL: Could not connect. Is TWS running with API enabled on port 7497?");
        return 1;
    }
    Console.WriteLine($"  OK: Connected (IsConnected={service.IsConnected})");
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.Message}");
    return 1;
}

Console.WriteLine();

// 2. Get Account
Console.WriteLine("[2/5] Getting account summary...");
try
{
    var account = await service.GetAccountAsync();
    Console.WriteLine($"  OK: AccountId={account.AccountId}");
    Console.WriteLine($"      NetLiquidation={account.NetLiquidationValue:C}");
    Console.WriteLine($"      TotalCash={account.TotalCashValue:C}");
    Console.WriteLine($"      BuyingPower={account.BuyingPower:C}");
    Console.WriteLine($"      AvailableFunds={account.AvailableFunds:C}");
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.Message}");
}

Console.WriteLine();

// 3. Get Positions
Console.WriteLine("[3/5] Getting positions...");
try
{
    var positions = await service.GetPositionsAsync();
    Console.WriteLine($"  OK: {positions.Count} position(s)");
    foreach (var pos in positions)
    {
        Console.WriteLine($"      {pos.Symbol} ({pos.SecurityType}): {pos.Quantity} @ {pos.AverageCost:C}");
    }
    if (positions.Count == 0)
        Console.WriteLine("      (empty -- expected for new paper account)");
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.Message}");
}

Console.WriteLine();

// 4. Get Quote
Console.WriteLine("[4/5] Getting AAPL quote (snapshot)...");
try
{
    var quote = await service.GetQuoteAsync("AAPL");
    Console.WriteLine($"  OK: AAPL Bid={quote.Bid} Ask={quote.Ask} Last={quote.Last}");
    Console.WriteLine($"      BidSize={quote.BidSize} AskSize={quote.AskSize} Volume={quote.Volume}");
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.Message}");
    Console.WriteLine("      (Market data may require subscription in TWS)");
}

Console.WriteLine();

// 5. Get Historical Bars
Console.WriteLine("[5/5] Getting SPY 30-day daily bars...");
try
{
    var endDate = DateTime.UtcNow;
    var startDate = endDate.AddDays(-30);
    var bars = await service.GetHistoricalBarsAsync("SPY", BarTimeframe.Daily, startDate, endDate);
    Console.WriteLine($"  OK: {bars.Count} bar(s)");
    if (bars.Count > 0)
    {
        var first = bars[0];
        var last = bars[^1];
        Console.WriteLine($"      First: {first.Timestamp:yyyy-MM-dd} O={first.Open} H={first.High} L={first.Low} C={first.Close}");
        Console.WriteLine($"      Last:  {last.Timestamp:yyyy-MM-dd} O={last.Open} H={last.High} L={last.Low} C={last.Close}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.Message}");
}

Console.WriteLine();

// Disconnect
Console.WriteLine("Disconnecting...");
await service.DisconnectAsync();
Console.WriteLine("Done.");

return 0;
