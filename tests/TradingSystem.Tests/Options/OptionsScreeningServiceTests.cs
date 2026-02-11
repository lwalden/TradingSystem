using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Strategies.Options;
using Xunit;

namespace TradingSystem.Tests.Options;

public class OptionsScreeningServiceTests
{
    private readonly Mock<IMarketDataService> _marketDataMock;
    private readonly Mock<IBrokerService> _brokerMock;
    private readonly Mock<ICalendarService> _calendarMock;
    private readonly TacticalConfig _config;
    private readonly OptionsScreeningService _service;

    public OptionsScreeningServiceTests()
    {
        _marketDataMock = new Mock<IMarketDataService>();
        _brokerMock = new Mock<IBrokerService>();
        _calendarMock = new Mock<ICalendarService>();
        _config = new TacticalConfig();

        _service = new OptionsScreeningService(
            _marketDataMock.Object,
            _brokerMock.Object,
            _calendarMock.Object,
            Microsoft.Extensions.Options.Options.Create(_config),
            NullLogger<OptionsScreeningService>.Instance);
    }

    #region Helper Methods

    private MarketRegime CreateMarketRegime(RegimeType regime)
    {
        return new MarketRegime
        {
            Regime = regime,
            VIX = 20m,
            SPYPrice = 450m,
            SPY50DMA = 445m,
            SPY200DMA = 440m,
            RiskMultiplier = 1.0m,
            Timestamp = DateTime.UtcNow
        };
    }

    private OptionsAnalytics CreateOptionsAnalytics(string symbol, decimal ivRank = 50m, decimal ivPercentile = 60m)
    {
        return new OptionsAnalytics
        {
            Symbol = symbol,
            IVRank = ivRank,
            IVPercentile = ivPercentile,
            CurrentIV = 0.30m,
            HistoricalVolatility20 = 0.25m,
            Timestamp = DateTime.UtcNow
        };
    }

    private Quote CreateQuote(string symbol, decimal price)
    {
        return new Quote
        {
            Symbol = symbol,
            Bid = price - 0.05m,
            Ask = price + 0.05m,
            Last = price,
            Timestamp = DateTime.UtcNow
        };
    }

    private OptionContract CreateOptionContract(
        string symbol,
        string underlying,
        decimal strike,
        DateTime expiration,
        OptionRight right,
        decimal delta,
        decimal mid,
        int openInterest = 500,
        decimal? impliedVolatility = null,
        decimal spreadPercent = 0.01m) // Default 1% spread, well below 2.5% threshold
    {
        var spread = mid * spreadPercent;
        var bid = mid - spread / 2;
        var ask = mid + spread / 2;

        return new OptionContract
        {
            Symbol = symbol,
            UnderlyingSymbol = underlying,
            Strike = strike,
            Expiration = expiration,
            Right = right,
            Delta = delta,
            Bid = bid,
            Ask = ask,
            Last = mid,
            OpenInterest = openInterest,
            Volume = 100,
            ImpliedVolatility = impliedVolatility ?? 0.30m,
            Theta = -0.05m,
            Gamma = 0.02m,
            Vega = 0.10m,
            Timestamp = DateTime.UtcNow
        };
    }

    private List<OptionContract> CreateCSPChain(string symbol, decimal underlyingPrice)
    {
        var expiration = DateTime.Today.AddDays(30);
        var strike = underlyingPrice * 0.95m; // 5% OTM

        // For 1% credit requirement: creditPer30Days / strike >= 0.01
        // creditPer30Days = credit / DTE * 30
        // So: (credit / 30 * 30) / strike >= 0.01
        // credit / strike >= 0.01
        // credit >= strike * 0.01
        var minCredit = strike * 0.01m; // Minimum credit for 1% threshold
        var goodCredit = minCredit * 1.5m; // 50% above minimum

        return new List<OptionContract>
        {
            // Put at 0.20 delta (target for CSP) with sufficient credit
            CreateOptionContract(
                $"{symbol}_PUT_{strike:F2}",
                symbol,
                strike,
                expiration,
                OptionRight.Put,
                -0.20m,
                mid: goodCredit,
                openInterest: 500),

            // Put at 0.15 delta (too low delta, outside range)
            CreateOptionContract(
                $"{symbol}_PUT_{underlyingPrice * 0.90m:F2}",
                symbol,
                underlyingPrice * 0.90m,
                expiration,
                OptionRight.Put,
                -0.08m, // Below 0.10 threshold (0.20 - 0.10)
                mid: 0.40m,
                openInterest: 500)
        };
    }

    private List<OptionContract> CreateBullPutSpreadChain(string symbol, decimal underlyingPrice)
    {
        var expiration = DateTime.Today.AddDays(30);
        // Create a vertical put spread: sell higher strike (short put), buy lower strike (long put)
        // Short put needs abs(delta) between 0.15 and 0.25

        return new List<OptionContract>
        {
            // Long put - lower strike, smaller delta, cheaper
            CreateOptionContract(
                $"{symbol}_PUT_100",
                symbol,
                100m,
                expiration,
                OptionRight.Put,
                -0.10m, // 10 delta
                mid: 1.00m,
                openInterest: 500),

            // Short put - higher strike, larger delta (0.20), more expensive
            CreateOptionContract(
                $"{symbol}_PUT_105",
                symbol,
                105m,
                expiration,
                OptionRight.Put,
                -0.20m, // 20 delta - in range [0.15, 0.25]
                mid: 2.00m, // Credit = 2.00 - 1.00 = 1.00
                openInterest: 500),

            // Another short put option
            CreateOptionContract(
                $"{symbol}_PUT_110",
                symbol,
                110m,
                expiration,
                OptionRight.Put,
                -0.25m, // 25 delta - in range [0.15, 0.25]
                mid: 3.00m,
                openInterest: 500)
        };
    }

    private List<OptionContract> CreateBearCallSpreadChain(string symbol, decimal underlyingPrice)
    {
        var expiration = DateTime.Today.AddDays(30);
        // Create a vertical call spread: sell lower strike (short call), buy higher strike (long call)
        // Short call needs abs(delta) between 0.15 and 0.25 (ShortCallDeltaMin to ShortCallDeltaMax)

        return new List<OptionContract>
        {
            // Short call - lower strike, larger delta (0.20), more expensive
            CreateOptionContract(
                $"{symbol}_CALL_105",
                symbol,
                105m,
                expiration,
                OptionRight.Call,
                0.20m, // 20 delta - in range [0.15, 0.25]
                mid: 2.00m,
                openInterest: 500),

            // Long call - higher strike, smaller delta, cheaper
            CreateOptionContract(
                $"{symbol}_CALL_110",
                symbol,
                110m,
                expiration,
                OptionRight.Call,
                0.10m, // 10 delta
                mid: 1.00m, // Credit = 2.00 - 1.00 = 1.00
                openInterest: 500),

            // Another short call option
            CreateOptionContract(
                $"{symbol}_CALL_100",
                symbol,
                100m,
                expiration,
                OptionRight.Call,
                0.25m, // 25 delta - in range [0.15, 0.25]
                mid: 3.00m,
                openInterest: 500)
        };
    }

    private List<OptionContract> CreateCalendarSpreadChain(string symbol, decimal underlyingPrice)
    {
        var frontExpiration = DateTime.Today.AddDays(21);
        var backExpiration = DateTime.Today.AddDays(60);
        var strike = underlyingPrice;

        return new List<OptionContract>
        {
            // Front month put (0.40 delta)
            CreateOptionContract(
                $"{symbol}_PUT_{strike}_FRONT",
                symbol,
                strike,
                frontExpiration,
                OptionRight.Put,
                -0.40m,
                mid: 2.05m,
                openInterest: 500,
                impliedVolatility: 0.30m),

            // Back month put (0.40 delta, higher IV)
            CreateOptionContract(
                $"{symbol}_PUT_{strike}_BACK",
                symbol,
                strike,
                backExpiration,
                OptionRight.Put,
                -0.40m,
                mid: 3.55m,
                openInterest: 500,
                impliedVolatility: 0.35m)
        };
    }

    private List<OptionContract> CreateLowLiquidityChain(string symbol, decimal underlyingPrice)
    {
        var expiration = DateTime.Today.AddDays(30);
        return new List<OptionContract>
        {
            // Low open interest
            CreateOptionContract(
                $"{symbol}_PUT_{underlyingPrice * 0.95m:F2}",
                symbol,
                underlyingPrice * 0.95m,
                expiration,
                OptionRight.Put,
                -0.20m,
                mid: 0.52m,
                openInterest: 100), // Below 250 threshold

            // Wide spread (bid=0.50, ask=0.80, spread=$0.30, above $0.10 threshold)
            CreateOptionContract(
                $"{symbol}_PUT_{underlyingPrice * 0.90m:F2}",
                symbol,
                underlyingPrice * 0.90m,
                expiration,
                OptionRight.Put,
                -0.20m,
                mid: 0.65m,
                openInterest: 500,
                spreadPercent: 0.30m / 0.65m) // Will create $0.30 spread
        };
    }

    #endregion

    #region Market Regime Tests

    [Fact]
    public async Task ScanAsync_RiskOffRegime_ReturnsEmptyResult()
    {
        // Arrange
        var symbols = new[] { "AAPL" };
        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.RiskOff));

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.Equal(RegimeType.RiskOff, result.MarketRegime);
        Assert.Equal(1, result.SymbolsScanned);
        Assert.Empty(result.CSPCandidates);
        Assert.Empty(result.BullPutSpreadCandidates);
        Assert.Empty(result.BearCallSpreadCandidates);
        Assert.Empty(result.IronCondorCandidates);
        Assert.Empty(result.CalendarSpreadCandidates);
        Assert.Equal(0, result.TotalCandidates);
    }

    #endregion

    #region No-Trade Window Tests

    [Fact]
    public async Task ScanAsync_NoTradeWindow_FiltersSymbols()
    {
        // Arrange
        var symbols = new[] { "AAPL", "MSFT", "GOOGL" };
        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.RiskOn));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "AAPL" });

        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("MSFT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOptionsAnalytics("MSFT", ivRank: 20m, ivPercentile: 40m));
        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("GOOGL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOptionsAnalytics("GOOGL", ivRank: 20m, ivPercentile: 40m));

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.Equal(3, result.SymbolsScanned);
        Assert.Equal(1, result.SymbolsFilteredByNoTrade);
        Assert.Equal(2, result.SymbolsFilteredByIV); // MSFT and GOOGL filtered by IV
        _marketDataMock.Verify(m => m.GetOptionsAnalyticsAsync("AAPL", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ScanAsync_AllInNoTradeWindow_ReturnsEmpty()
    {
        // Arrange
        var symbols = new[] { "AAPL", "MSFT" };
        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.RiskOn));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "AAPL", "MSFT" });

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.Equal(2, result.SymbolsScanned);
        Assert.Equal(2, result.SymbolsFilteredByNoTrade);
        Assert.Equal(0, result.TotalCandidates);
    }

    #endregion

    #region IV Filter Tests

    [Fact]
    public async Task ScanAsync_IVBelowThreshold_FiltersOut()
    {
        // Arrange
        var symbols = new[] { "AAPL" };
        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.RiskOn));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOptionsAnalytics("AAPL", ivRank: 25m, ivPercentile: 45m)); // Below thresholds

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.Equal(1, result.SymbolsScanned);
        Assert.Equal(1, result.SymbolsFilteredByIV);
        Assert.Equal(0, result.TotalCandidates);
    }

    #endregion

    #region Liquidity Filter Tests

    [Fact]
    public async Task ScanAsync_NoLiquidContracts_FiltersOut()
    {
        // Arrange
        var symbols = new[] { "AAPL" };
        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.RiskOn));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOptionsAnalytics("AAPL"));
        _brokerMock.Setup(b => b.GetOptionChainAsync("AAPL", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateLowLiquidityChain("AAPL", 150m));

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.Equal(1, result.SymbolsFilteredByLiquidity);
        Assert.Equal(0, result.TotalCandidates);
    }

    [Fact]
    public async Task PassesLiquidityFilter_SufficientOI_ReturnsTrue()
    {
        // Arrange
        var contract = CreateOptionContract("AAPL_PUT_150", "AAPL", 150m, DateTime.Today.AddDays(30),
            OptionRight.Put, -0.20m, mid: 5.00m, openInterest: 500);

        // Act
        var passes = _service.PassesLiquidityFilter(contract);

        // Assert
        Assert.True(passes);
    }

    [Fact]
    public async Task PassesLiquidityFilter_LowOI_ReturnsFalse()
    {
        // Arrange
        var contract = CreateOptionContract("AAPL_PUT_150", "AAPL", 150m, DateTime.Today.AddDays(30),
            OptionRight.Put, -0.20m, mid: 5.00m, openInterest: 100);

        // Act
        var passes = _service.PassesLiquidityFilter(contract);

        // Assert
        Assert.False(passes);
    }

    [Fact]
    public async Task PassesLiquidityFilter_WideSpread_ReturnsFalse()
    {
        // Arrange
        // Mid = 0.65, spread needs to be > $0.10, so spreadPercent = 0.20 / 0.65 = ~30.8%
        var contract = CreateOptionContract("AAPL_PUT_150", "AAPL", 150m, DateTime.Today.AddDays(30),
            OptionRight.Put, -0.20m, mid: 0.65m, openInterest: 500, spreadPercent: 0.20m / 0.65m);

        // Act
        var passes = _service.PassesLiquidityFilter(contract);

        // Assert
        Assert.False(passes);
    }

    [Fact]
    public async Task PassesLiquidityFilter_HighSpreadPercent_ReturnsFalse()
    {
        // Arrange
        // Mid = 5.00, spread % = 5% (above 2.5% threshold)
        var contract = CreateOptionContract("AAPL_PUT_150", "AAPL", 150m, DateTime.Today.AddDays(30),
            OptionRight.Put, -0.20m, mid: 5.00m, openInterest: 500, spreadPercent: 0.05m);

        // Act
        var passes = _service.PassesLiquidityFilter(contract);

        // Assert
        Assert.False(passes);
    }

    #endregion

    #region CSP Tests

    [Fact]
    public async Task ScanAsync_CSP_RiskOnRegime_FindsCandidates()
    {
        // Arrange
        var symbols = new[] { "AAPL" };
        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.RiskOn));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOptionsAnalytics("AAPL"));
        _brokerMock.Setup(b => b.GetOptionChainAsync("AAPL", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCSPChain("AAPL", 150m));
        _marketDataMock.Setup(m => m.GetQuoteAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateQuote("AAPL", 150m));

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.Equal(RegimeType.RiskOn, result.MarketRegime);
        Assert.NotEmpty(result.CSPCandidates);
        var candidate = result.CSPCandidates[0];
        Assert.Equal("AAPL", candidate.UnderlyingSymbol);
        Assert.Equal(StrategyType.CashSecuredPut, candidate.Strategy);
        Assert.Single(candidate.Legs);
        Assert.Equal(OptionRight.Put, candidate.Legs[0].Right);
        Assert.Equal(OrderAction.Sell, candidate.Legs[0].Action);
        Assert.True(candidate.Score > 0);
    }

    [Fact]
    public async Task ScanAsync_CSP_CautiousRegime_NoCandidates()
    {
        // Arrange
        var symbols = new[] { "AAPL" };
        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.Cautious));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOptionsAnalytics("AAPL"));
        _brokerMock.Setup(b => b.GetOptionChainAsync("AAPL", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCSPChain("AAPL", 150m));
        _marketDataMock.Setup(m => m.GetQuoteAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateQuote("AAPL", 150m));

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.Equal(RegimeType.Cautious, result.MarketRegime);
        Assert.Empty(result.CSPCandidates);
    }

    [Fact]
    public async Task ScanAsync_CSP_RecoveryRegime_FindsCandidates()
    {
        // Arrange
        var symbols = new[] { "AAPL" };
        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.Recovery));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOptionsAnalytics("AAPL"));
        _brokerMock.Setup(b => b.GetOptionChainAsync("AAPL", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCSPChain("AAPL", 150m));
        _marketDataMock.Setup(m => m.GetQuoteAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateQuote("AAPL", 150m));

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.Equal(RegimeType.Recovery, result.MarketRegime);
        Assert.NotEmpty(result.CSPCandidates);
    }

    [Fact]
    public async Task ScanAsync_CSP_InsufficientCredit_FilteredOut()
    {
        // Arrange
        var symbols = new[] { "AAPL" };
        var expiration = DateTime.Today.AddDays(30);
        var lowCreditChain = new List<OptionContract>
        {
            // Credit too low: 0.10 credit / 30 DTE * 30 / 142.50 strike = 0.7% (below 1% threshold)
            CreateOptionContract(
                "AAPL_PUT_142.50",
                "AAPL",
                142.50m,
                expiration,
                OptionRight.Put,
                -0.20m,
                mid: 0.10m, // Very low credit
                openInterest: 500)
        };

        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.RiskOn));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOptionsAnalytics("AAPL"));
        _brokerMock.Setup(b => b.GetOptionChainAsync("AAPL", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lowCreditChain);
        _marketDataMock.Setup(m => m.GetQuoteAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateQuote("AAPL", 150m));

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.Empty(result.CSPCandidates);
    }

    #endregion

    #region Bull Put Spread Tests

    [Fact]
    public async Task ScanAsync_BullPutSpread_FindsCandidates()
    {
        // Arrange
        var symbols = new[] { "SPY" };
        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.RiskOn));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOptionsAnalytics("SPY"));
        _brokerMock.Setup(b => b.GetOptionChainAsync("SPY", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateBullPutSpreadChain("SPY", 110m));
        _marketDataMock.Setup(m => m.GetQuoteAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateQuote("SPY", 110m));

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.NotEmpty(result.BullPutSpreadCandidates);
        var candidate = result.BullPutSpreadCandidates[0];
        Assert.Equal("SPY", candidate.UnderlyingSymbol);
        Assert.Equal(StrategyType.BullPutSpread, candidate.Strategy);
        Assert.Equal(2, candidate.Legs.Count);
        Assert.All(candidate.Legs, leg => Assert.Equal(OptionRight.Put, leg.Right));
        Assert.Contains(candidate.Legs, leg => leg.Action == OrderAction.Sell);
        Assert.Contains(candidate.Legs, leg => leg.Action == OrderAction.Buy);
        Assert.True(candidate.Legs[0].Strike > candidate.Legs[1].Strike); // Short put at higher strike
    }

    [Fact]
    public async Task ScanAsync_BullPutSpread_CautiousRegime_NoCandidates()
    {
        // Arrange
        var symbols = new[] { "SPY" };
        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.Cautious));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOptionsAnalytics("SPY"));
        _brokerMock.Setup(b => b.GetOptionChainAsync("SPY", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateBullPutSpreadChain("SPY", 110m));
        _marketDataMock.Setup(m => m.GetQuoteAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateQuote("SPY", 110m));

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.Empty(result.BullPutSpreadCandidates);
    }

    #endregion

    #region Bear Call Spread Tests

    [Fact]
    public async Task ScanAsync_BearCallSpread_FindsCandidates()
    {
        // Arrange
        var symbols = new[] { "SPY" };
        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.RiskOn));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOptionsAnalytics("SPY"));
        _brokerMock.Setup(b => b.GetOptionChainAsync("SPY", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateBearCallSpreadChain("SPY", 110m));
        _marketDataMock.Setup(m => m.GetQuoteAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateQuote("SPY", 110m));

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.NotEmpty(result.BearCallSpreadCandidates);
        var candidate = result.BearCallSpreadCandidates[0];
        Assert.Equal("SPY", candidate.UnderlyingSymbol);
        Assert.Equal(StrategyType.BearCallSpread, candidate.Strategy);
        Assert.Equal(2, candidate.Legs.Count);
        Assert.All(candidate.Legs, leg => Assert.Equal(OptionRight.Call, leg.Right));
        Assert.Contains(candidate.Legs, leg => leg.Action == OrderAction.Sell);
        Assert.Contains(candidate.Legs, leg => leg.Action == OrderAction.Buy);
        Assert.True(candidate.Legs[0].Strike < candidate.Legs[1].Strike); // Short call at lower strike
    }

    [Fact]
    public async Task ScanAsync_BearCallSpread_CautiousRegime_Allowed()
    {
        // Arrange
        var symbols = new[] { "SPY" };
        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.Cautious));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOptionsAnalytics("SPY"));
        _brokerMock.Setup(b => b.GetOptionChainAsync("SPY", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateBearCallSpreadChain("SPY", 110m));
        _marketDataMock.Setup(m => m.GetQuoteAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateQuote("SPY", 110m));

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.NotEmpty(result.BearCallSpreadCandidates);
    }

    [Fact]
    public async Task ScanAsync_BearCallSpread_RecoveryRegime_NotAllowed()
    {
        // Arrange
        var symbols = new[] { "SPY" };
        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.Recovery));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOptionsAnalytics("SPY"));
        _brokerMock.Setup(b => b.GetOptionChainAsync("SPY", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateBearCallSpreadChain("SPY", 110m));
        _marketDataMock.Setup(m => m.GetQuoteAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateQuote("SPY", 110m));

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.Empty(result.BearCallSpreadCandidates);
    }

    #endregion

    #region Iron Condor Tests

    [Fact]
    public async Task ScanAsync_IronCondor_RiskOnOnly()
    {
        // Arrange
        var symbols = new[] { "SPY" };
        var chain = CreateBullPutSpreadChain("SPY", 110m)
            .Concat(CreateBearCallSpreadChain("SPY", 110m))
            .ToList();

        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.RiskOn));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOptionsAnalytics("SPY"));
        _brokerMock.Setup(b => b.GetOptionChainAsync("SPY", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chain);
        _marketDataMock.Setup(m => m.GetQuoteAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateQuote("SPY", 110m));

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.NotEmpty(result.IronCondorCandidates);

        // Test with Cautious regime
        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.Cautious));
        var cautiousResult = await _service.ScanAsync(symbols);
        Assert.Empty(cautiousResult.IronCondorCandidates);
    }

    [Fact]
    public async Task ScanAsync_IronCondor_CombinesSides()
    {
        // Arrange
        var symbols = new[] { "SPY" };
        var chain = CreateBullPutSpreadChain("SPY", 110m)
            .Concat(CreateBearCallSpreadChain("SPY", 110m))
            .ToList();

        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.RiskOn));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOptionsAnalytics("SPY"));
        _brokerMock.Setup(b => b.GetOptionChainAsync("SPY", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chain);
        _marketDataMock.Setup(m => m.GetQuoteAsync("SPY", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateQuote("SPY", 110m));

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.NotEmpty(result.IronCondorCandidates);
        var candidate = result.IronCondorCandidates[0];
        Assert.Equal(StrategyType.IronCondor, candidate.Strategy);
        Assert.Equal(4, candidate.Legs.Count);
        Assert.Equal(2, candidate.Legs.Count(leg => leg.Right == OptionRight.Put));
        Assert.Equal(2, candidate.Legs.Count(leg => leg.Right == OptionRight.Call));
    }

    #endregion

    #region Calendar Spread Tests

    [Fact]
    public async Task ScanAsync_CalendarSpread_FindsCandidates()
    {
        // Arrange
        var symbols = new[] { "AAPL" };
        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.RiskOn));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOptionsAnalytics("AAPL"));
        _brokerMock.Setup(b => b.GetOptionChainAsync("AAPL", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCalendarSpreadChain("AAPL", 150m));
        _marketDataMock.Setup(m => m.GetQuoteAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateQuote("AAPL", 150m));

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.NotEmpty(result.CalendarSpreadCandidates);
        var candidate = result.CalendarSpreadCandidates[0];
        Assert.Equal("AAPL", candidate.UnderlyingSymbol);
        Assert.Equal(StrategyType.CalendarSpread, candidate.Strategy);
        Assert.Equal(2, candidate.Legs.Count);
        Assert.Equal(candidate.Legs[0].Strike, candidate.Legs[1].Strike); // Same strike
        Assert.True(candidate.Legs[1].Expiration > candidate.Legs[0].Expiration); // Back month later
        Assert.Equal(OrderAction.Sell, candidate.Legs[0].Action); // Sell front
        Assert.Equal(OrderAction.Buy, candidate.Legs[1].Action); // Buy back
    }

    [Fact]
    public async Task ScanAsync_CalendarSpread_UnfavorableTermStructure_Skipped()
    {
        // Arrange
        var symbols = new[] { "AAPL" };
        var frontExpiration = DateTime.Today.AddDays(21);
        var backExpiration = DateTime.Today.AddDays(60);
        var strike = 150m;

        // Unfavorable term structure: front month IV > back month IV
        var chain = new List<OptionContract>
        {
            CreateOptionContract(
                $"AAPL_PUT_{strike}_FRONT",
                "AAPL",
                strike,
                frontExpiration,
                OptionRight.Put,
                -0.40m,
                mid: 2.05m,
                openInterest: 500,
                impliedVolatility: 0.35m), // Higher IV in front

            CreateOptionContract(
                $"AAPL_PUT_{strike}_BACK",
                "AAPL",
                strike,
                backExpiration,
                OptionRight.Put,
                -0.40m,
                mid: 3.55m,
                openInterest: 500,
                impliedVolatility: 0.30m) // Lower IV in back (unfavorable)
        };

        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.RiskOn));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOptionsAnalytics("AAPL"));
        _brokerMock.Setup(b => b.GetOptionChainAsync("AAPL", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chain);
        _marketDataMock.Setup(m => m.GetQuoteAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateQuote("AAPL", 150m));

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.Empty(result.CalendarSpreadCandidates);
    }

    #endregion

    #region Sorting and Error Handling Tests

    [Fact]
    public async Task ScanAsync_CandidatesSortedByScoreDescending()
    {
        // Arrange
        var symbols = new[] { "AAPL" };
        var expiration = DateTime.Today.AddDays(30);

        // Create multiple CSPs with different characteristics that will result in different scores
        // All with delta in range 0.10-0.25 and sufficient credit (strike * 0.01)
        var chain = new List<OptionContract>
        {
            CreateOptionContract("AAPL_PUT_145", "AAPL", 145m, expiration, OptionRight.Put, -0.20m, mid: 1.50m, openInterest: 500),
            CreateOptionContract("AAPL_PUT_142", "AAPL", 142m, expiration, OptionRight.Put, -0.18m, mid: 1.45m, openInterest: 500),
            CreateOptionContract("AAPL_PUT_140", "AAPL", 140m, expiration, OptionRight.Put, -0.22m, mid: 1.42m, openInterest: 500),
        };

        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.RiskOn));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOptionsAnalytics("AAPL"));
        _brokerMock.Setup(b => b.GetOptionChainAsync("AAPL", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(chain);
        _marketDataMock.Setup(m => m.GetQuoteAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateQuote("AAPL", 150m));

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.True(result.CSPCandidates.Count >= 2);
        for (int i = 0; i < result.CSPCandidates.Count - 1; i++)
        {
            Assert.True(result.CSPCandidates[i].Score >= result.CSPCandidates[i + 1].Score,
                $"Candidates not sorted: Score[{i}]={result.CSPCandidates[i].Score}, Score[{i+1}]={result.CSPCandidates[i + 1].Score}");
        }
    }

    [Fact]
    public async Task ScanAsync_AnalyticsFetchFails_RecordsError()
    {
        // Arrange
        var symbols = new[] { "AAPL" };
        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.RiskOn));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("AAPL", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Analytics service unavailable"));

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("AAPL") && e.Contains("IV analytics failed"));
    }

    [Fact]
    public async Task ScanAsync_ChainFetchFails_RecordsError()
    {
        // Arrange
        var symbols = new[] { "AAPL" };
        _marketDataMock.Setup(m => m.GetMarketRegimeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMarketRegime(RegimeType.RiskOn));
        _calendarMock.Setup(c => c.GetSymbolsInNoTradeWindowAsync(
            It.IsAny<List<string>>(),
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _marketDataMock.Setup(m => m.GetOptionsAnalyticsAsync("AAPL", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateOptionsAnalytics("AAPL"));
        _brokerMock.Setup(b => b.GetOptionChainAsync("AAPL", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Chain service unavailable"));

        // Act
        var result = await _service.ScanAsync(symbols);

        // Assert
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("AAPL") && e.Contains("screening failed"));
    }

    #endregion
}
