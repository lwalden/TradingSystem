using IBApi;
using Microsoft.Extensions.Logging.Abstractions;
using TradingSystem.Brokers.IBKR;
using Xunit;

namespace TradingSystem.Tests.IBKR;

public class IBKROptionChainCallbackTests
{
    private readonly IBKRCallbackHandler _handler;

    public IBKROptionChainCallbackTests()
    {
        _handler = new IBKRCallbackHandler(NullLogger.Instance);
    }

    // === SecDefOptParams: register + callback + end -> completes with data ===

    [Fact]
    public async Task SecDefOptParams_RegisterCallbackEnd_CompletesWithData()
    {
        var task = _handler.RegisterSecDefOptParamsRequest(100);

        var expirations = new HashSet<string> { "20260320", "20260417" };
        var strikes = new HashSet<double> { 140.0, 145.0, 150.0 };

        _handler.securityDefinitionOptionParameter(
            100, "SMART", 265598, "AAPL", "100", expirations, strikes);
        _handler.securityDefinitionOptionParameterEnd(100);

        var result = await task;
        Assert.Single(result);
        Assert.Equal("SMART", result[0].Exchange);
        Assert.Equal(265598, result[0].UnderlyingConId);
        Assert.Equal("AAPL", result[0].TradingClass);
        Assert.Equal("100", result[0].Multiplier);
        Assert.Equal(2, result[0].Expirations.Count);
        Assert.Contains("20260320", result[0].Expirations);
        Assert.Contains("20260417", result[0].Expirations);
        Assert.Equal(3, result[0].Strikes.Count);
        Assert.Contains(150.0, result[0].Strikes);
    }

    // === SecDefOptParams: multiple exchanges accumulated ===

    [Fact]
    public async Task SecDefOptParams_MultipleExchanges_AllAccumulated()
    {
        var task = _handler.RegisterSecDefOptParamsRequest(101);

        var exp1 = new HashSet<string> { "20260320" };
        var strikes1 = new HashSet<double> { 150.0 };
        _handler.securityDefinitionOptionParameter(
            101, "SMART", 265598, "AAPL", "100", exp1, strikes1);

        var exp2 = new HashSet<string> { "20260320", "20260417" };
        var strikes2 = new HashSet<double> { 145.0, 150.0, 155.0 };
        _handler.securityDefinitionOptionParameter(
            101, "CBOE", 265598, "AAPL", "100", exp2, strikes2);

        var exp3 = new HashSet<string> { "20260320" };
        var strikes3 = new HashSet<double> { 150.0 };
        _handler.securityDefinitionOptionParameter(
            101, "PSE", 265598, "AAPL", "100", exp3, strikes3);

        _handler.securityDefinitionOptionParameterEnd(101);

        var result = await task;
        Assert.Equal(3, result.Count);
        Assert.Equal("SMART", result[0].Exchange);
        Assert.Equal("CBOE", result[1].Exchange);
        Assert.Equal("PSE", result[2].Exchange);
        Assert.Equal(3, result[1].Strikes.Count);
    }

    // === SecDefOptParams: cleanup removes request ===

    [Fact]
    public void SecDefOptParams_Cleanup_RemovesRequest()
    {
        var task = _handler.RegisterSecDefOptParamsRequest(102);

        _handler.CleanupRequest(102);

        // After cleanup, callback should be silently ignored (no crash, no completion)
        var expirations = new HashSet<string> { "20260320" };
        var strikes = new HashSet<double> { 150.0 };
        _handler.securityDefinitionOptionParameter(
            102, "SMART", 265598, "AAPL", "100", expirations, strikes);
        _handler.securityDefinitionOptionParameterEnd(102);

        // The task should never complete because the TCS was removed
        Assert.False(task.IsCompleted);
    }

    // === SecDefOptParams: end without register -> no crash ===

    [Fact]
    public void SecDefOptParams_EndWithoutRegister_NoCrash()
    {
        // Calling end callback without a prior registration should not throw
        _handler.securityDefinitionOptionParameterEnd(999);
    }

    // === OptionQuote: register + tickPrice(bid/ask) + tickOptionComputation(field=13) + tickSnapshotEnd ===

    [Fact]
    public async Task OptionQuote_FullFlow_PopulatesAllData()
    {
        var task = _handler.RegisterOptionQuoteRequest(
            200, "AAPL", 150m, new DateTime(2026, 3, 20), "C");

        // Price ticks
        _handler.tickPrice(200, 1, 3.50, new TickAttrib()); // BID
        _handler.tickPrice(200, 2, 3.80, new TickAttrib()); // ASK
        _handler.tickPrice(200, 4, 3.65, new TickAttrib()); // LAST

        // Size ticks
        _handler.tickSize(200, 27, 12500m); // OPEN_INTEREST

        // Greeks via tickOptionComputation (field=13 = model)
        _handler.tickOptionComputation(200, 13, 0,
            impliedVolatility: 0.35,
            delta: 0.55,
            optPrice: 3.65,
            pvDividend: 0.02,
            gamma: 0.04,
            vega: 0.18,
            theta: -0.05,
            undPrice: 155.0);

        _handler.tickSnapshotEnd(200);

        var result = await task;
        Assert.Equal(3.50m, result.Bid);
        Assert.Equal(3.80m, result.Ask);
        Assert.Equal(3.65m, result.Last);
        Assert.Equal(12500, result.OpenInterest);
        Assert.Equal("AAPL", result.UnderlyingSymbol);
        Assert.Equal(150m, result.Strike);
        Assert.Equal(new DateTime(2026, 3, 20), result.Expiration);
        Assert.Equal("C", result.Right);
        Assert.Equal(0.35, result.ImpliedVolatility);
        Assert.Equal(0.55, result.Delta);
        Assert.Equal(0.04, result.Gamma);
        Assert.Equal(-0.05, result.Theta);
        Assert.Equal(0.18, result.Vega);
        Assert.True(result.HasGreeks);
    }

    // === OptionQuote: tickOptionComputation field != 13 is ignored ===

    [Fact]
    public async Task OptionQuote_NonModelField_GreeksNotSet()
    {
        var task = _handler.RegisterOptionQuoteRequest(
            201, "MSFT", 400m, new DateTime(2026, 4, 17), "P");

        // Field 10 = bid computation, should be ignored
        _handler.tickOptionComputation(201, 10, 0,
            impliedVolatility: 0.30,
            delta: -0.45,
            optPrice: 5.00,
            pvDividend: 0.01,
            gamma: 0.03,
            vega: 0.20,
            theta: -0.06,
            undPrice: 405.0);

        // Field 11 = ask computation, should be ignored
        _handler.tickOptionComputation(201, 11, 0,
            impliedVolatility: 0.32,
            delta: -0.47,
            optPrice: 5.20,
            pvDividend: 0.01,
            gamma: 0.03,
            vega: 0.21,
            theta: -0.07,
            undPrice: 405.0);

        // Field 12 = last computation, should be ignored
        _handler.tickOptionComputation(201, 12, 0,
            impliedVolatility: 0.31,
            delta: -0.46,
            optPrice: 5.10,
            pvDividend: 0.01,
            gamma: 0.03,
            vega: 0.20,
            theta: -0.06,
            undPrice: 405.0);

        _handler.tickSnapshotEnd(201);

        var result = await task;
        // Greeks should not be populated since field was never 13
        Assert.Null(result.ImpliedVolatility);
        Assert.Null(result.Delta);
        Assert.Null(result.Gamma);
        Assert.Null(result.Theta);
        Assert.Null(result.Vega);
        Assert.False(result.HasGreeks);
    }

    // === OptionQuote: invalid values (MaxValue) are filtered out ===

    [Fact]
    public async Task OptionQuote_InvalidMaxValues_FilteredOut()
    {
        var task = _handler.RegisterOptionQuoteRequest(
            202, "SPY", 500m, new DateTime(2026, 3, 20), "C");

        // Send field=13 with invalid values (double.MaxValue means "not available" in TWS API)
        _handler.tickOptionComputation(202, 13, 0,
            impliedVolatility: double.MaxValue,
            delta: double.MaxValue,
            optPrice: 0,
            pvDividend: 0,
            gamma: double.MaxValue,
            vega: double.MaxValue,
            theta: double.MaxValue,
            undPrice: 500.0);

        _handler.tickSnapshotEnd(202);

        var result = await task;
        // MaxValue should be filtered out by the handler's validation checks
        Assert.Null(result.ImpliedVolatility);
        Assert.Null(result.Delta);
        Assert.Null(result.Gamma);
        Assert.Null(result.Theta);
        Assert.Null(result.Vega);
        Assert.False(result.HasGreeks);
    }

    // === OptionQuote: tickSize populates OpenInterest (field 27) ===

    [Fact]
    public async Task OptionQuote_TickSize_PopulatesOpenInterestAndVolume()
    {
        var task = _handler.RegisterOptionQuoteRequest(
            203, "TSLA", 250m, new DateTime(2026, 3, 20), "P");

        _handler.tickSize(203, 8, 5000m);  // VOLUME
        _handler.tickSize(203, 27, 25000m); // OPEN_INTEREST (call field)

        _handler.tickSnapshotEnd(203);

        var result = await task;
        Assert.Equal(5000, result.OptionVolume);
        Assert.Equal(25000, result.OpenInterest);
    }

    // === FaultRequest faults option chain requests ===

    [Fact]
    public async Task FaultRequest_ViaError_FaultsSecDefOptParamsRequest()
    {
        var task = _handler.RegisterSecDefOptParamsRequest(300);

        // Simulate an IBKR error for this reqId
        _handler.error(300, 0L, 200, "No security definition has been found", "");

        var ex = await Assert.ThrowsAsync<IBKRApiException>(() => task);
        Assert.Equal(200, ex.ErrorCode);
        Assert.Contains("No security definition", ex.Message);
    }

    [Fact]
    public async Task FaultRequest_ViaError_FaultsOptionQuoteRequest()
    {
        var task = _handler.RegisterOptionQuoteRequest(
            301, "AAPL", 150m, new DateTime(2026, 3, 20), "C");

        // Simulate an IBKR error for this tickerId
        _handler.error(301, 0L, 354, "Requested market data is not subscribed", "");

        var ex = await Assert.ThrowsAsync<IBKRApiException>(() => task);
        Assert.Equal(354, ex.ErrorCode);
    }

    // === CleanupRequest cleans up option chain dictionaries ===

    [Fact]
    public void CleanupRequest_RemovesOptionChainAndQuoteDictionaries()
    {
        // Register both types of option requests
        var secDefTask = _handler.RegisterSecDefOptParamsRequest(400);
        var optQuoteTask = _handler.RegisterOptionQuoteRequest(
            401, "AAPL", 150m, new DateTime(2026, 3, 20), "C");

        // Clean up both
        _handler.CleanupRequest(400);
        _handler.CleanupRequest(401);

        // After cleanup, callbacks should not complete the tasks
        var expirations = new HashSet<string> { "20260320" };
        var strikes = new HashSet<double> { 150.0 };
        _handler.securityDefinitionOptionParameter(
            400, "SMART", 265598, "AAPL", "100", expirations, strikes);
        _handler.securityDefinitionOptionParameterEnd(400);

        _handler.tickPrice(401, 1, 3.50, new TickAttrib());
        _handler.tickOptionComputation(401, 13, 0, 0.35, 0.55, 3.65, 0.02, 0.04, 0.18, -0.05, 155.0);
        _handler.tickSnapshotEnd(401);

        // Neither task should be completed since the dictionaries were cleaned up
        Assert.False(secDefTask.IsCompleted);
        Assert.False(optQuoteTask.IsCompleted);
    }
}
