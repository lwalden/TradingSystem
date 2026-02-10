using IBApi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingSystem.Brokers.IBKR;
using Xunit;

namespace TradingSystem.Tests.IBKR;

public class IBKRCallbackHandlerTests
{
    private readonly IBKRCallbackHandler _handler;

    public IBKRCallbackHandlerTests()
    {
        _handler = new IBKRCallbackHandler(NullLogger.Instance);
    }

    // === Connection ===

    [Fact]
    public async Task NextValidId_CompletesConnectionTask()
    {
        var task = _handler.RegisterConnectionRequest();

        _handler.nextValidId(42);

        var result = await task;
        Assert.Equal(42, result);
        Assert.Equal(42, _handler.NextValidOrderId);
    }

    // === Account Summary ===

    [Fact]
    public async Task AccountSummary_AccumulatesAndCompletes()
    {
        var task = _handler.RegisterAccountSummaryRequest(1);

        _handler.accountSummary(1, "DU12345", "NetLiquidation", "100000.50", "USD");
        _handler.accountSummary(1, "DU12345", "TotalCashValue", "50000.25", "USD");
        _handler.accountSummary(1, "DU12345", "BuyingPower", "200000.00", "USD");
        _handler.accountSummary(1, "DU12345", "GrossPositionValue", "50000.00", "USD");
        _handler.accountSummary(1, "DU12345", "MaintMarginReq", "10000.00", "USD");
        _handler.accountSummary(1, "DU12345", "InitMarginReq", "15000.00", "USD");
        _handler.accountSummary(1, "DU12345", "AvailableFunds", "85000.00", "USD");
        _handler.accountSummaryEnd(1);

        var result = await task;
        Assert.Equal("DU12345", result.AccountId);
        Assert.Equal(100000.50m, result.NetLiquidation);
        Assert.Equal(50000.25m, result.TotalCashValue);
        Assert.Equal(200000.00m, result.BuyingPower);
        Assert.Equal(50000.00m, result.GrossPositionValue);
        Assert.Equal(10000.00m, result.MaintMarginReq);
        Assert.Equal(15000.00m, result.InitMarginReq);
        Assert.Equal(85000.00m, result.AvailableFunds);
    }

    [Fact]
    public void AccountSummary_IgnoresUnknownReqId()
    {
        // Should not throw
        _handler.accountSummary(999, "DU12345", "NetLiquidation", "100000", "USD");
        _handler.accountSummaryEnd(999);
    }

    // === Positions ===

    [Fact]
    public async Task Position_AccumulatesAndCompletes()
    {
        var task = _handler.RegisterPositionRequest();

        var contract1 = new Contract { Symbol = "AAPL", SecType = "STK" };
        var contract2 = new Contract { Symbol = "MSFT", SecType = "STK" };

        _handler.position("DU12345", contract1, 100m, 150.50);
        _handler.position("DU12345", contract2, 50m, 300.00);
        _handler.positionEnd();

        var result = await task;
        Assert.Equal(2, result.Count);
        Assert.Equal("AAPL", result[0].Symbol);
        Assert.Equal(100m, result[0].Quantity);
        Assert.Equal(150.50, result[0].AverageCost);
        Assert.Equal("MSFT", result[1].Symbol);
    }

    [Fact]
    public async Task Position_EmptyList_WhenNoPositions()
    {
        var task = _handler.RegisterPositionRequest();
        _handler.positionEnd();

        var result = await task;
        Assert.Empty(result);
    }

    [Fact]
    public async Task Position_IncludesOptionFields()
    {
        var task = _handler.RegisterPositionRequest();

        var contract = new Contract
        {
            Symbol = "AAPL",
            SecType = "OPT",
            Strike = 150.0,
            LastTradeDateOrContractMonth = "20250321",
            Right = "C"
        };

        _handler.position("DU12345", contract, -5m, 3.50);
        _handler.positionEnd();

        var result = await task;
        Assert.Single(result);
        Assert.Equal("OPT", result[0].SecType);
        Assert.Equal(150m, result[0].Strike);
        Assert.Equal("20250321", result[0].LastTradeDateOrContractMonth);
        Assert.Equal("C", result[0].Right);
    }

    // === Quotes ===

    [Fact]
    public async Task TickPrice_PopulatesQuoteAndCompletes()
    {
        var task = _handler.RegisterQuoteRequest(10);

        _handler.tickPrice(10, 1, 150.00, new TickAttrib()); // BID
        _handler.tickPrice(10, 2, 150.05, new TickAttrib()); // ASK
        _handler.tickPrice(10, 4, 150.02, new TickAttrib()); // LAST
        _handler.tickPrice(10, 6, 151.00, new TickAttrib()); // HIGH
        _handler.tickPrice(10, 7, 149.50, new TickAttrib()); // LOW
        _handler.tickPrice(10, 9, 149.75, new TickAttrib()); // CLOSE
        _handler.tickSize(10, 0, 100m);  // BID_SIZE
        _handler.tickSize(10, 3, 200m);  // ASK_SIZE
        _handler.tickSize(10, 8, 5000000m); // VOLUME
        _handler.tickSnapshotEnd(10);

        var result = await task;
        Assert.Equal(150.00m, result.Bid);
        Assert.Equal(150.05m, result.Ask);
        Assert.Equal(150.02m, result.Last);
        Assert.Equal(151.00m, result.High);
        Assert.Equal(149.50m, result.Low);
        Assert.Equal(149.75m, result.Close);
        Assert.Equal(100m, result.BidSize);
        Assert.Equal(200m, result.AskSize);
        Assert.Equal(5000000L, result.Volume);
    }

    [Fact]
    public void TickPrice_IgnoresUnknownTickerId()
    {
        // Should not throw
        _handler.tickPrice(999, 1, 100.0, new TickAttrib());
        _handler.tickSize(999, 0, 100m);
        _handler.tickSnapshotEnd(999);
    }

    // === Historical Data ===

    [Fact]
    public async Task HistoricalData_AccumulatesAndCompletes()
    {
        var task = _handler.RegisterHistoricalDataRequest(20);

        var bar1 = new Bar("20250113", 150, 155, 149, 153, 1000m, 500, 152m);
        var bar2 = new Bar("20250114", 153, 158, 152, 157, 1200m, 600, 155m);
        var bar3 = new Bar("20250115", 157, 160, 156, 159, 1100m, 550, 158m);

        _handler.historicalData(20, bar1);
        _handler.historicalData(20, bar2);
        _handler.historicalData(20, bar3);
        _handler.historicalDataEnd(20, "20250113", "20250115");

        var result = await task;
        Assert.Equal(3, result.Count);
        Assert.Equal("20250113", result[0].Time);
        Assert.Equal("20250115", result[2].Time);
    }

    [Fact]
    public void HistoricalData_IgnoresUnknownReqId()
    {
        var bar = new Bar("20250113", 0, 0, 0, 0, 0m, 0, 0m);
        // Should not throw
        _handler.historicalData(999, bar);
        _handler.historicalDataEnd(999, "20250113", "20250113");
    }

    // === Error Handling ===

    [Fact]
    public async Task Error_FaultsMatchingRequest()
    {
        var task = _handler.RegisterAccountSummaryRequest(5);

        _handler.error(5, 0L, 200, "No security definition", "");

        var ex = await Assert.ThrowsAsync<IBKRApiException>(() => task);
        Assert.Equal(200, ex.ErrorCode);
        Assert.Contains("No security definition", ex.Message);
    }

    [Fact]
    public async Task Error_FaultsQuoteRequest()
    {
        var task = _handler.RegisterQuoteRequest(10);

        _handler.error(10, 0L, 354, "Requested market data is not subscribed", "");

        var ex = await Assert.ThrowsAsync<IBKRApiException>(() => task);
        Assert.Equal(354, ex.ErrorCode);
    }

    [Fact]
    public async Task Error_FaultsHistoricalDataRequest()
    {
        var task = _handler.RegisterHistoricalDataRequest(15);

        _handler.error(15, 0L, 162, "Historical data pacing violation", "");

        var ex = await Assert.ThrowsAsync<IBKRApiException>(() => task);
        Assert.Equal(162, ex.ErrorCode);
    }

    [Theory]
    [InlineData(2104)]
    [InlineData(2106)]
    [InlineData(2107)]
    [InlineData(2108)]
    [InlineData(2158)]
    public void Error_InfoCodes_DoNotFaultRequests(int infoCode)
    {
        var task = _handler.RegisterAccountSummaryRequest(5);

        // Info error codes should be silently ignored
        _handler.error(5, 0L, infoCode, "Market data farm connected", "");

        // Task should still be pending (not faulted)
        Assert.False(task.IsCompleted);
    }

    [Theory]
    [InlineData(504)]
    [InlineData(1100)]
    [InlineData(1101)]
    [InlineData(1102)]
    public async Task Error_ConnectionCodes_FaultAllPending(int errorCode)
    {
        var task1 = _handler.RegisterAccountSummaryRequest(1);
        var task2 = _handler.RegisterQuoteRequest(2);
        var task3 = _handler.RegisterHistoricalDataRequest(3);

        _handler.error(-1, 0L, errorCode, "Connection lost", "");

        await Assert.ThrowsAsync<IBKRApiException>(() => task1);
        await Assert.ThrowsAsync<IBKRApiException>(() => task2);
        await Assert.ThrowsAsync<IBKRApiException>(() => task3);
    }

    [Fact]
    public void Error_RaisesOnErrorEvent()
    {
        int? receivedReqId = null;
        int? receivedCode = null;
        string? receivedMsg = null;

        _handler.OnError += (id, code, msg) =>
        {
            receivedReqId = id;
            receivedCode = code;
            receivedMsg = msg;
        };

        _handler.error(5, 0L, 200, "test error", "");

        Assert.Equal(5, receivedReqId);
        Assert.Equal(200, receivedCode);
        Assert.Equal("test error", receivedMsg);
    }

    [Fact]
    public void ConnectionClosed_RaisesEvent()
    {
        var eventFired = false;
        _handler.OnConnectionClosed += () => eventFired = true;

        _handler.connectionClosed();

        Assert.True(eventFired);
    }

    // === Cleanup ===

    [Fact]
    public void CleanupRequest_RemovesAllTracking()
    {
        _handler.RegisterAccountSummaryRequest(1);
        _handler.RegisterQuoteRequest(2);
        _handler.RegisterHistoricalDataRequest(3);

        // Should not throw
        _handler.CleanupRequest(1);
        _handler.CleanupRequest(2);
        _handler.CleanupRequest(3);

        // Callbacks after cleanup should be silently ignored
        _handler.accountSummary(1, "DU12345", "NetLiquidation", "100000", "USD");
        _handler.tickPrice(2, 1, 150.0, new TickAttrib());
        _handler.historicalData(3, new Bar("20250113", 0, 0, 0, 0, 0m, 0, 0m));
    }
}
