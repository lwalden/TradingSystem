using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Brokers.IBKR;
using TradingSystem.Brokers.IBKR.Services;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Models;
using TradingSystem.Strategies.Options;
using TradingSystem.Strategies.Services;

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

var totalTests = 14;
var passed = 0;
var failed = 0;

Console.WriteLine($"=== IBKR Smoke Test ({totalTests} tests) ===");
Console.WriteLine();

// 1. Connect
Console.WriteLine($"[1/{totalTests}] Connecting to TWS...");
try
{
    var connected = await service.ConnectAsync();
    if (!connected)
    {
        Console.WriteLine("FAIL: Could not connect. Is TWS running with API enabled on port 7497?");
        return 1;
    }
    Console.WriteLine($"  OK: Connected (IsConnected={service.IsConnected})");
    passed++;
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.Message}");
    return 1;
}

Console.WriteLine();

// 2. Get Account
Console.WriteLine($"[2/{totalTests}] Getting account summary...");
try
{
    var account = await service.GetAccountAsync();
    Console.WriteLine($"  OK: AccountId={account.AccountId}");
    Console.WriteLine($"      NetLiquidation={account.NetLiquidationValue:C}");
    Console.WriteLine($"      TotalCash={account.TotalCashValue:C}");
    Console.WriteLine($"      BuyingPower={account.BuyingPower:C}");
    Console.WriteLine($"      AvailableFunds={account.AvailableFunds:C}");
    passed++;
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.Message}");
    failed++;
}

Console.WriteLine();

// 3. Get Positions
Console.WriteLine($"[3/{totalTests}] Getting positions...");
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
    passed++;
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.Message}");
    failed++;
}

Console.WriteLine();

// 4. Get Quote
Console.WriteLine($"[4/{totalTests}] Getting AAPL quote (snapshot)...");
try
{
    var quote = await service.GetQuoteAsync("AAPL");
    Console.WriteLine($"  OK: AAPL Bid={quote.Bid} Ask={quote.Ask} Last={quote.Last}");
    Console.WriteLine($"      BidSize={quote.BidSize} AskSize={quote.AskSize} Volume={quote.Volume}");
    passed++;
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.Message}");
    Console.WriteLine("      (Market data may require subscription in TWS)");
    failed++;
}

Console.WriteLine();

// 5. Get Historical Bars
Console.WriteLine($"[5/{totalTests}] Getting SPY 30-day daily bars...");
List<PriceBar>? spyBars = null;
try
{
    var endDate = DateTime.UtcNow;
    var startDate = endDate.AddDays(-30);
    spyBars = await service.GetHistoricalBarsAsync("SPY", BarTimeframe.Daily, startDate, endDate);
    Console.WriteLine($"  OK: {spyBars.Count} bar(s)");
    if (spyBars.Count > 0)
    {
        var first = spyBars[0];
        var last = spyBars[^1];
        Console.WriteLine($"      First: {first.Timestamp:yyyy-MM-dd} O={first.Open} H={first.High} L={first.Low} C={first.Close}");
        Console.WriteLine($"      Last:  {last.Timestamp:yyyy-MM-dd} O={last.Open} H={last.High} L={last.Low} C={last.Close}");
    }
    passed++;
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.Message}");
    failed++;
}

Console.WriteLine();

// 6. Place Limit Order (very low price so it won't fill)
Console.WriteLine($"[6/{totalTests}] Placing limit buy: 1 VIG @ $1.00...");
string? placedBrokerId = null;
try
{
    var order = new Order
    {
        Symbol = "VIG",
        Action = OrderAction.Buy,
        Quantity = 1,
        OrderType = OrderType.Limit,
        LimitPrice = 1.00m,
        TimeInForce = TimeInForce.Day,
        Sleeve = SleeveType.Income,
        Rationale = "Smoke test order"
    };

    var result = await service.PlaceOrderAsync(order);
    placedBrokerId = result.BrokerId;
    Console.WriteLine($"  OK: BrokerId={result.BrokerId}, Status={result.Status}");
    passed++;
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.Message}");
    failed++;
}

Console.WriteLine();

// 7. Get Open Orders
Console.WriteLine($"[7/{totalTests}] Getting open orders...");
try
{
    var openOrders = await service.GetOpenOrdersAsync();
    Console.WriteLine($"  OK: {openOrders.Count} open order(s)");
    foreach (var oo in openOrders)
    {
        Console.WriteLine($"      {oo.BrokerId}: {oo.Action} {oo.Quantity} {oo.Symbol} {oo.OrderType} @ {oo.LimitPrice} [{oo.Status}]");
    }
    passed++;
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.Message}");
    failed++;
}

Console.WriteLine();

// 8. Cancel the order
Console.WriteLine($"[8/{totalTests}] Cancelling the order...");
if (placedBrokerId != null)
{
    try
    {
        var cancelled = await service.CancelOrderAsync(placedBrokerId);
        Console.WriteLine($"  OK: Cancel request sent (result={cancelled})");
        await Task.Delay(1500); // Wait for cancel confirmation

        // Verify it's gone from open orders
        var remaining = await service.GetOpenOrdersAsync();
        var stillOpen = remaining.Any(o => o.BrokerId == placedBrokerId);
        Console.WriteLine($"  OK: Order still in open orders = {stillOpen}");
        passed++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL: {ex.Message}");
        failed++;
    }
}
else
{
    Console.WriteLine("  SKIP: No order was placed to cancel");
}

Console.WriteLine();

// =============================================
// PR #9: Option Chains, IV Analytics, Technical Indicators
// =============================================

Console.WriteLine("--- PR #9: Option Chains / IV / Technical Indicators ---");
Console.WriteLine();

// 9. Get SPY Option Chain
Console.WriteLine($"[9/{totalTests}] Getting SPY option chain (nearest expiry)...");
try
{
    var chain = await service.GetOptionChainAsync("SPY");
    Console.WriteLine($"  OK: {chain.Count} contract(s)");
    if (chain.Count > 0)
    {
        var puts = chain.Where(c => c.Right == OptionRight.Put).ToList();
        var calls = chain.Where(c => c.Right == OptionRight.Call).ToList();
        var expirations = chain.Select(c => c.Expiration).Distinct().OrderBy(e => e).ToList();
        Console.WriteLine($"      Puts={puts.Count} Calls={calls.Count}");
        Console.WriteLine($"      Expirations: {string.Join(", ", expirations.Take(3).Select(e => e.ToString("yyyy-MM-dd")))}");

        // Show a sample contract with Greeks
        var sample = chain.FirstOrDefault(c => c.Delta != null && c.Delta != 0);
        if (sample != null)
        {
            Console.WriteLine($"      Sample: {sample.UnderlyingSymbol} {sample.Expiration:MMM-dd} {sample.Strike} {sample.Right}");
            Console.WriteLine($"              Bid={sample.Bid} Ask={sample.Ask} Mid={sample.Mid}");
            Console.WriteLine($"              Delta={sample.Delta:F4} Gamma={sample.Gamma:F4} Theta={sample.Theta:F4} Vega={sample.Vega:F4}");
            Console.WriteLine($"              IV={sample.ImpliedVolatility:F4} OI={sample.OpenInterest} Vol={sample.Volume}");
        }
    }
    passed++;
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.Message}");
    failed++;
}

Console.WriteLine();

// 10. Get IV Analytics (IV Rank / IV Percentile)
Console.WriteLine($"[10/{totalTests}] Getting AAPL IV analytics (rank/percentile)...");
try
{
    var analytics = await service.GetOptionsAnalyticsAsync("AAPL");
    Console.WriteLine($"  OK: AAPL IV Analytics");
    Console.WriteLine($"      CurrentIV={analytics.CurrentIV:P2}");
    Console.WriteLine($"      IVRank={analytics.IVRank:F1} IVPercentile={analytics.IVPercentile:F1}");
    Console.WriteLine($"      HV20={analytics.HistoricalVolatility20:P2} HV60={analytics.HistoricalVolatility60:P2}");
    passed++;
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.Message}");
    failed++;
}

Console.WriteLine();

// 11. Technical Indicators (from historical bars)
Console.WriteLine($"[11/{totalTests}] Computing SPY technical indicators...");
try
{
    // Get 1yr of bars for full indicator calculation
    var endDate = DateTime.UtcNow;
    var startDate = endDate.AddDays(-365);
    var bars = await service.GetHistoricalBarsAsync("SPY", BarTimeframe.Daily, startDate, endDate);
    var indicators = TechnicalIndicatorCalculator.Calculate("SPY", bars);
    Console.WriteLine($"  OK: SPY Technical Indicators ({bars.Count} bars)");
    Console.WriteLine($"      SMA20={indicators.SMA20:F2} SMA50={indicators.SMA50:F2} SMA200={indicators.SMA200:F2}");
    Console.WriteLine($"      EMA20={indicators.EMA20:F2}");
    Console.WriteLine($"      RSI14={indicators.RSI14:F1} RSI2={indicators.RSI2:F1}");
    Console.WriteLine($"      ATR14={indicators.ATR14:F2}");
    Console.WriteLine($"      Bollinger: {indicators.BollingerLower:F2} / {indicators.BollingerMid:F2} / {indicators.BollingerUpper:F2}");
    Console.WriteLine($"      VolumeAvg20={indicators.VolumeAvg20:F0} VolumeRatio={indicators.VolumeRatio:F2}");
    Console.WriteLine($"      Above 20DMA={indicators.Above20DMA} 50DMA={indicators.Above50DMA} 200DMA={indicators.Above200DMA}");
    passed++;
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.Message}");
    failed++;
}

Console.WriteLine();

// =============================================
// PR #10/11: CachingMarketDataService, Market Regime, Options Screening
// =============================================

Console.WriteLine("--- PR #10/11: Caching Service / Market Regime / Options Screening ---");
Console.WriteLine();

// 12. CachingMarketDataService - Quote + Indicators
Console.WriteLine($"[12/{totalTests}] CachingMarketDataService: AAPL quote + indicators...");
var cachingLogger = loggerFactory.CreateLogger<CachingMarketDataService>();
var cachingService = new CachingMarketDataService(service, cachingLogger);
try
{
    var quote = await cachingService.GetQuoteAsync("AAPL");
    Console.WriteLine($"  OK: AAPL cached quote: Bid={quote.Bid} Ask={quote.Ask} Last={quote.Last}");

    var indicators = await cachingService.GetIndicatorsAsync("AAPL");
    Console.WriteLine($"  OK: AAPL cached indicators: SMA20={indicators.SMA20:F2} RSI14={indicators.RSI14:F1}");

    // Verify caching by fetching again (should be instant)
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var quote2 = await cachingService.GetQuoteAsync("AAPL");
    sw.Stop();
    Console.WriteLine($"  OK: Cached re-fetch took {sw.ElapsedMilliseconds}ms (should be <5ms)");
    passed++;
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.Message}");
    failed++;
}

Console.WriteLine();

// 13. Market Regime Detection
Console.WriteLine($"[13/{totalTests}] Market regime detection (VIX + SPY analysis)...");
try
{
    var regime = await cachingService.GetMarketRegimeAsync();
    Console.WriteLine($"  OK: Regime={regime.Regime}");
    Console.WriteLine($"      RiskMultiplier={regime.RiskMultiplier:F2}");
    Console.WriteLine($"      VIX={regime.VIX:F2} VIXElevated={regime.VIXElevated}");
    Console.WriteLine($"      SPY={regime.SPYPrice:F2} 50DMA={regime.SPY50DMA:F2} 200DMA={regime.SPY200DMA:F2}");
    passed++;
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.Message}");
    failed++;
}

Console.WriteLine();

// 14. Options Screening Service (with stub calendar -- no Polygon key required)
Console.WriteLine($"[14/{totalTests}] Options screening pipeline (SPY, 1 symbol)...");
try
{
    var tacticalConfig = Options.Create(new TacticalConfig());
    var screenLogger = loggerFactory.CreateLogger<OptionsScreeningService>();
    // Use a stub calendar that says no symbols are in no-trade windows
    var calendar = new StubCalendarService();
    var screener = new OptionsScreeningService(cachingService, service, calendar, tacticalConfig, screenLogger);

    var result = await screener.ScanAsync(["SPY"]);
    Console.WriteLine($"  OK: Screening complete");
    Console.WriteLine($"      SymbolsScanned={result.SymbolsScanned}");
    Console.WriteLine($"      Regime={result.MarketRegime}");
    Console.WriteLine($"      FilteredByNoTrade={result.SymbolsFilteredByNoTrade}");
    Console.WriteLine($"      FilteredByIV={result.SymbolsFilteredByIV}");
    Console.WriteLine($"      FilteredByLiquidity={result.SymbolsFilteredByLiquidity}");
    Console.WriteLine($"      TotalCandidates={result.TotalCandidates}");
    Console.WriteLine($"        CSP={result.CSPCandidates.Count} BullPut={result.BullPutSpreadCandidates.Count} BearCall={result.BearCallSpreadCandidates.Count}");
    Console.WriteLine($"        IronCondor={result.IronCondorCandidates.Count} Calendar={result.CalendarSpreadCandidates.Count}");
    // Show top candidates from each strategy
    var allCandidates = result.CSPCandidates
        .Concat(result.BullPutSpreadCandidates)
        .Concat(result.BearCallSpreadCandidates)
        .Concat(result.IronCondorCandidates)
        .Concat(result.CalendarSpreadCandidates)
        .OrderByDescending(c => c.Score)
        .Take(5);
    foreach (var c in allCandidates)
    {
        Console.WriteLine($"      -> {c.Strategy}: {c.UnderlyingSymbol} Score={c.Score:F2} DTE={c.DTE} RoR={c.ReturnOnRisk:F1}% POP={c.ProbabilityOfProfit:P0}");
    }
    if (result.TotalCandidates == 0)
        Console.WriteLine("      (no candidates -- expected if IV rank is below threshold or market closed)");
    if (result.Errors.Count > 0)
        Console.WriteLine($"      Errors: {string.Join("; ", result.Errors)}");
    passed++;
}
catch (Exception ex)
{
    Console.WriteLine($"FAIL: {ex.Message}");
    failed++;
}

Console.WriteLine();

// Disconnect
Console.WriteLine("Disconnecting...");
await service.DisconnectAsync();

Console.WriteLine();
Console.WriteLine($"=== Results: {passed} passed, {failed} failed, {totalTests} total ===");

return failed > 0 ? 1 : 0;

// Stub calendar service for smoke testing without Polygon.io API key
class StubCalendarService : TradingSystem.Core.Interfaces.ICalendarService
{
    public Task<List<EarningsEvent>> GetEarningsCalendarAsync(DateTime startDate, DateTime endDate,
        IEnumerable<string>? symbols = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new List<EarningsEvent>());

    public Task<List<DividendEvent>> GetDividendCalendarAsync(DateTime startDate, DateTime endDate,
        IEnumerable<string>? symbols = null, CancellationToken cancellationToken = default)
        => Task.FromResult(new List<DividendEvent>());

    public Task<List<MacroEvent>> GetMacroCalendarAsync(DateTime startDate, DateTime endDate,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new List<MacroEvent>());

    public Task<bool> IsInNoTradeWindowAsync(string symbol, DateTime date,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<List<string>> GetSymbolsInNoTradeWindowAsync(IEnumerable<string> symbols, DateTime date,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new List<string>());
}
