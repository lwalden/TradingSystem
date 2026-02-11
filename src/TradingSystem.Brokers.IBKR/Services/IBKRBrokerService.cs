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
    private int _nextOrderId;

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
            _nextOrderId = _callbackHandler.NextValidOrderId;
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

    public async Task<Order> PlaceOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var orderId = Interlocked.Increment(ref _nextOrderId);
        order.BrokerId = orderId.ToString();

        var contract = IBKRContractFactory.CreateStock(order.Symbol);
        var ibOrder = IBKROrderFactory.CreateOrder(order);
        ibOrder.OrderId = orderId;

        var task = _callbackHandler.RegisterOrderPlacementRequest(orderId);

        try
        {
            _logger.LogInformation("Placing order {OrderId}: {Action} {Qty} {Symbol} {Type} @ {Price}",
                orderId, order.Action, order.Quantity, order.Symbol, order.OrderType, order.LimitPrice);

            _clientSocket!.placeOrder(orderId, contract, ibOrder);

            var result = await _requestManager.WithTimeout(
                task,
                _config.RequestTimeout,
                cancellationToken);

            order.Status = IBKRMappingExtensions.ToOrderStatus(result.Status);
            order.SubmittedAt = DateTime.UtcNow;
            order.LastUpdated = DateTime.UtcNow;

            _logger.LogInformation("Order {OrderId} accepted: status={Status}", orderId, result.Status);
            return order;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            order.Status = OrderStatus.Error;
            order.LastUpdated = DateTime.UtcNow;
            _logger.LogError(ex, "Failed to place order {OrderId}", orderId);
            throw;
        }
        finally
        {
            _callbackHandler.CleanupRequest(orderId);
        }
    }

    public async Task<Order> GetOrderStatusAsync(string orderId, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        // Request all open orders and find the matching one
        var openOrders = await GetOpenOrdersAsync(cancellationToken);
        var match = openOrders.FirstOrDefault(o => o.BrokerId == orderId);
        if (match != null)
            return match;

        throw new InvalidOperationException($"Order {orderId} not found in open orders");
    }

    public async Task<List<Order>> GetOpenOrdersAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        var task = _callbackHandler.RegisterOpenOrdersRequest();

        try
        {
            _clientSocket!.reqAllOpenOrders();

            var orderDataList = await _requestManager.WithTimeout(
                task,
                _config.RequestTimeout,
                cancellationToken);

            return orderDataList.Select(od => new Order
            {
                BrokerId = od.OrderId.ToString(),
                Symbol = od.Symbol,
                SecurityType = od.SecType,
                Action = MapIBKRAction(od.Action),
                Quantity = od.TotalQuantity,
                OrderType = MapIBKROrderType(od.OrderType),
                LimitPrice = od.LmtPrice < double.MaxValue ? (decimal)od.LmtPrice : null,
                StopPrice = od.AuxPrice < double.MaxValue ? (decimal)od.AuxPrice : null,
                Status = IBKRMappingExtensions.ToOrderStatus(od.Status),
                FilledQuantity = od.Filled,
                LastUpdated = DateTime.UtcNow
            }).ToList();
        }
        finally
        {
            // Open orders request has no cleanup (global request)
        }
    }

    public async Task<bool> CancelOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        if (!int.TryParse(orderId, out var ibOrderId))
            throw new ArgumentException($"Invalid order ID: {orderId}", nameof(orderId));

        _logger.LogInformation("Cancelling order {OrderId}", ibOrderId);

        _clientSocket!.cancelOrder(ibOrderId, new OrderCancel());

        // Wait briefly for the status callback -- cancellation is confirmed via orderStatus callback
        // but we don't block on it; the caller can check status separately
        await Task.Delay(500, cancellationToken);
        return true;
    }

    public async Task<Order> ModifyOrderAsync(string orderId, decimal? newLimitPrice = null,
        decimal? newQuantity = null, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        if (!int.TryParse(orderId, out var ibOrderId))
            throw new ArgumentException($"Invalid order ID: {orderId}", nameof(orderId));

        // To modify, we need the original order details -- get from open orders
        var openOrders = await GetOpenOrdersAsync(cancellationToken);
        var existing = openOrders.FirstOrDefault(o => o.BrokerId == orderId)
            ?? throw new InvalidOperationException($"Order {orderId} not found in open orders");

        if (newLimitPrice.HasValue)
            existing.LimitPrice = newLimitPrice;
        if (newQuantity.HasValue)
            existing.Quantity = newQuantity.Value;

        var contract = IBKRContractFactory.CreateStock(existing.Symbol);
        var ibOrder = IBKROrderFactory.CreateOrder(existing);
        ibOrder.OrderId = ibOrderId;

        var task = _callbackHandler.RegisterOrderPlacementRequest(ibOrderId);

        try
        {
            _clientSocket!.placeOrder(ibOrderId, contract, ibOrder);

            var result = await _requestManager.WithTimeout(
                task,
                _config.RequestTimeout,
                cancellationToken);

            existing.Status = IBKRMappingExtensions.ToOrderStatus(result.Status);
            existing.LastUpdated = DateTime.UtcNow;
            return existing;
        }
        finally
        {
            _callbackHandler.CleanupRequest(ibOrderId);
        }
    }

    private static OrderAction MapIBKRAction(string action)
    {
        return action switch
        {
            "BUY" => OrderAction.Buy,
            "SELL" => OrderAction.Sell,
            "SSHORT" => OrderAction.SellShort,
            _ => OrderAction.Buy
        };
    }

    private static OrderType MapIBKROrderType(string orderType)
    {
        return orderType switch
        {
            "MKT" => OrderType.Market,
            "LMT" => OrderType.Limit,
            "STP" => OrderType.Stop,
            "STP LMT" => OrderType.StopLimit,
            "TRAIL" => OrderType.TrailingStop,
            _ => OrderType.Market
        };
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
