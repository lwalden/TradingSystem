using IBApi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using Order = TradingSystem.Core.Models.Order;

namespace TradingSystem.Brokers.IBKR.Services;

/// <summary>
/// Interactive Brokers TWS API implementation of IBrokerService.
/// Bridges IBKR's callback-based API to async/await via IBKRCallbackHandler.
/// </summary>
public class IBKRBrokerService : IBrokerService, IDisposable
{
    private readonly ILogger<IBKRBrokerService> _logger;
    private readonly IBKRConfig _config;
    private readonly IBKRCallbackHandler _callbackHandler;
    private readonly IBKRRequestManager _requestManager;

    private EClientSocket? _clientSocket;
    private EReader? _reader;
    private volatile bool _isConnected;
    private bool _disposed;
    private CancellationTokenSource? _messagePumpCts;

    private static readonly string AccountSummaryTags =
        "NetLiquidation,TotalCashValue,BuyingPower,GrossPositionValue,MaintMarginReq,InitMarginReq,AvailableFunds";

    public IBKRBrokerService(
        ILogger<IBKRBrokerService> logger,
        IOptions<IBKRConfig> config)
    {
        _logger = logger;
        _config = config.Value;
        _callbackHandler = new IBKRCallbackHandler(logger);
        _requestManager = new IBKRRequestManager();

        _callbackHandler.OnError += HandleError;
        _callbackHandler.OnConnectionClosed += HandleConnectionClosed;
    }

    public bool IsConnected => _isConnected;

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_isConnected)
        {
            _logger.LogWarning("Already connected to IBKR");
            return true;
        }

        _logger.LogInformation("Connecting to IBKR at {Host}:{Port} (clientId={ClientId})",
            _config.Host, _config.Port, _config.ClientId);

        var signal = new EReaderMonitorSignal();
        _clientSocket = new EClientSocket(_callbackHandler, signal);

        var connectionTask = _callbackHandler.RegisterConnectionRequest();

        _clientSocket.eConnect(_config.Host, _config.Port, _config.ClientId);

        if (!_clientSocket.IsConnected())
        {
            _logger.LogError("Failed to establish TCP connection to IBKR");
            return false;
        }

        // Start the EReader thread to receive messages from TWS
        _reader = new EReader(_clientSocket, signal);
        _reader.Start();

        // Start message processing pump on a dedicated thread
        _messagePumpCts = new CancellationTokenSource();
        var pumpToken = _messagePumpCts.Token;
        _ = Task.Factory.StartNew(() =>
        {
            while (!pumpToken.IsCancellationRequested)
            {
                signal.waitForSignal();
                if (pumpToken.IsCancellationRequested) break;
                _reader.processMsgs();
            }
        }, pumpToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        // Wait for nextValidId callback which confirms the connection is ready
        try
        {
            await _requestManager.WithTimeout(
                connectionTask,
                _config.ConnectionTimeout,
                cancellationToken,
                () => _logger.LogError("IBKR connection timed out waiting for nextValidId"));

            _isConnected = true;
            _logger.LogInformation("Connected to IBKR successfully (nextValidOrderId={OrderId})",
                _callbackHandler.NextValidOrderId);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to connect to IBKR");
            CleanupConnection();
            return false;
        }
    }

    public Task DisconnectAsync()
    {
        if (!_isConnected && _clientSocket == null)
            return Task.CompletedTask;

        _logger.LogInformation("Disconnecting from IBKR");
        CleanupConnection();
        return Task.CompletedTask;
    }

    public async Task<Account> GetAccountAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var reqId = _requestManager.GetNextRequestId();
        var task = _callbackHandler.RegisterAccountSummaryRequest(reqId);

        try
        {
            _clientSocket!.reqAccountSummary(reqId, "All", AccountSummaryTags);

            var result = await _requestManager.WithTimeout(
                task,
                _config.RequestTimeout,
                cancellationToken,
                () => _clientSocket.cancelAccountSummary(reqId));

            _clientSocket.cancelAccountSummary(reqId);
            return result.ToAccount();
        }
        finally
        {
            _callbackHandler.CleanupRequest(reqId);
        }
    }

    public async Task<List<Position>> GetPositionsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var task = _callbackHandler.RegisterPositionRequest();

        try
        {
            _clientSocket!.reqPositions();

            var positions = await _requestManager.WithTimeout(
                task,
                _config.RequestTimeout,
                cancellationToken,
                () => _clientSocket.cancelPositions());

            _clientSocket.cancelPositions();
            return positions.Select(p => p.ToPosition()).ToList();
        }
        finally
        {
            _callbackHandler.CleanupPositionRequest();
        }
    }

    public async Task<decimal> GetBuyingPowerAsync(CancellationToken cancellationToken = default)
    {
        var account = await GetAccountAsync(cancellationToken);
        return account.BuyingPower;
    }

    public async Task<Quote> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var tickerId = _requestManager.GetNextRequestId();
        var contract = IBKRContractFactory.CreateStock(symbol);
        var task = _callbackHandler.RegisterQuoteRequest(tickerId);

        try
        {
            _clientSocket!.reqMktData(tickerId, contract, "", true, false, []);

            var quoteData = await _requestManager.WithTimeout(
                task,
                _config.RequestTimeout,
                cancellationToken,
                () => _clientSocket.cancelMktData(tickerId));

            return quoteData.ToQuote(symbol);
        }
        finally
        {
            _clientSocket!.cancelMktData(tickerId);
            _callbackHandler.CleanupRequest(tickerId);
        }
    }

    public async Task<List<Quote>> GetQuotesAsync(IEnumerable<string> symbols,
        CancellationToken cancellationToken = default)
    {
        var tasks = symbols.Select(s => GetQuoteAsync(s, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    public async Task<List<PriceBar>> GetHistoricalBarsAsync(string symbol, BarTimeframe timeframe,
        DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var reqId = _requestManager.GetNextRequestId();
        var contract = IBKRContractFactory.CreateStock(symbol);
        var task = _callbackHandler.RegisterHistoricalDataRequest(reqId);

        var endDateStr = endDate.ToString("yyyyMMdd-HH:mm:ss");
        var duration = IBKRMappingExtensions.ToIBKRDuration(startDate, endDate);
        var barSize = timeframe.ToIBKRBarSize();

        try
        {
            _clientSocket!.reqHistoricalData(
                reqId, contract, endDateStr, duration, barSize,
                "TRADES", 1, 1, false, []);

            var bars = await _requestManager.WithTimeout(
                task,
                _config.RequestTimeout,
                cancellationToken);

            return bars.Select(b => b.ToPriceBar(symbol, timeframe)).ToList();
        }
        finally
        {
            _callbackHandler.CleanupRequest(reqId);
        }
    }

    // === Not yet implemented (Phase 2+) ===

    public Task<List<OptionContract>> GetOptionChainAsync(string underlying,
        DateTime? expiration = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Option chain retrieval is planned for Phase 1, Week 5-6.");
    }

    public Task<OptionsAnalytics> GetOptionsAnalyticsAsync(string symbol,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Options analytics is planned for Phase 1, Week 5-6.");
    }

    public Task<Order> PlaceOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Order placement is planned for Phase 1, Week 3-4.");
    }

    public Task<Order> GetOrderStatusAsync(string orderId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Order status tracking is planned for Phase 1, Week 3-4.");
    }

    public Task<List<Order>> GetOpenOrdersAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Open orders retrieval is planned for Phase 1, Week 3-4.");
    }

    public Task<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Order cancellation is planned for Phase 1, Week 3-4.");
    }

    public Task<Order> ModifyOrderAsync(string orderId, decimal? newLimitPrice = null,
        decimal? newQuantity = null, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Order modification is planned for Phase 1, Week 3-4.");
    }

    public Task<SecurityCalendar> GetSecurityCalendarAsync(string symbol,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Security calendar is planned for Phase 2.");
    }

    public Task<List<DividendEvent>> GetUpcomingDividendsAsync(IEnumerable<string> symbols,
        int daysAhead = 30, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Dividend calendar is planned for Phase 2.");
    }

    public Task<List<EarningsEvent>> GetUpcomingEarningsAsync(IEnumerable<string> symbols,
        int daysAhead = 14, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Earnings calendar is planned for Phase 2.");
    }

    // === Private helpers ===

    private void EnsureConnected()
    {
        if (!_isConnected || _clientSocket == null)
            throw new InvalidOperationException("Not connected to IBKR. Call ConnectAsync first.");
    }

    private void HandleError(int reqId, int errorCode, string errorMsg)
    {
        if (errorCode is 504 or 1100 or 1101 or 1102)
        {
            _isConnected = false;
        }
    }

    private void HandleConnectionClosed()
    {
        _isConnected = false;
    }

    private void CleanupConnection()
    {
        _isConnected = false;
        _messagePumpCts?.Cancel();
        _messagePumpCts?.Dispose();
        _messagePumpCts = null;

        try
        {
            _clientSocket?.eDisconnect();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during IBKR disconnect");
        }

        _clientSocket = null;
        _reader = null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _callbackHandler.OnError -= HandleError;
            _callbackHandler.OnConnectionClosed -= HandleConnectionClosed;
            CleanupConnection();
            _disposed = true;
        }
    }
}
