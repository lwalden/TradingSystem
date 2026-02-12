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

            // Request delayed data as fallback when live subscriptions are unavailable
            // Type 3 = delayed-frozen (delayed when available, frozen last price otherwise)
            _clientSocket.reqMarketDataType(3);

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
        var contract = IBKRContractFactory.CreateEquity(symbol);
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
        var contract = IBKRContractFactory.CreateEquity(symbol);
        var task = _callbackHandler.RegisterHistoricalDataRequest(reqId);

        var endDateStr = endDate.ToString("yyyyMMdd-HH:mm:ss");
        var duration = IBKRMappingExtensions.ToIBKRDuration(startDate, endDate);
        var barSize = timeframe.ToIBKRBarSize();
        // Indices don't have TRADES data — use MIDPOINT instead
        var whatToShow = IBKRContractFactory.IsIndex(symbol) ? "MIDPOINT" : "TRADES";

        try
        {
            _clientSocket!.reqHistoricalData(
                reqId, contract, endDateStr, duration, barSize,
                whatToShow, 1, 1, false, []);

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

    // === Options ===

    public async Task<List<OptionContract>> GetOptionChainAsync(string underlying,
        DateTime? expiration = null, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        // Step 0: Resolve ConId via reqContractDetails (required for reqSecDefOptParams)
        var conId = await ResolveConIdAsync(underlying, cancellationToken);

        // Step 1: Discover available expirations and strikes
        var reqId = _requestManager.GetNextRequestId();
        var defTask = _callbackHandler.RegisterSecDefOptParamsRequest(reqId);

        try
        {
            _clientSocket!.reqSecDefOptParams(reqId, underlying, "", "STK", conId);

            var paramsList = await _requestManager.WithTimeout(
                defTask, _config.RequestTimeout, cancellationToken);

            // Pick the SMART exchange params (or first available)
            var smartParams = paramsList.FirstOrDefault(p => p.Exchange == "SMART")
                              ?? paramsList.FirstOrDefault();
            if (smartParams == null)
            {
                _logger.LogWarning("No option chain definitions found for {Symbol}", underlying);
                return new List<OptionContract>();
            }

            var chainDef = smartParams.ToOptionChainDefinition(underlying);

            // Step 2: Filter expirations to relevant DTE range
            var today = DateTime.Today;
            var relevantExpirations = chainDef.Expirations
                .Where(e =>
                {
                    var dte = (e - today).Days;
                    return dte >= _config.OptionChainMinDTE && dte <= _config.OptionChainMaxDTE;
                })
                .ToList();

            if (expiration.HasValue)
                relevantExpirations = relevantExpirations
                    .Where(e => e.Date == expiration.Value.Date).ToList();

            if (relevantExpirations.Count == 0)
            {
                _logger.LogWarning("No expirations in DTE range {Min}-{Max} for {Symbol}",
                    _config.OptionChainMinDTE, _config.OptionChainMaxDTE, underlying);
                return new List<OptionContract>();
            }

            // Step 3: Filter strikes to ATM +/- 15%
            var underlyingQuote = await GetQuoteAsync(underlying, cancellationToken);
            var underlyingPrice = underlyingQuote.Last > 0 ? underlyingQuote.Last
                : (underlyingQuote.Bid + underlyingQuote.Ask) / 2;
            var strikeRange = underlyingPrice * 0.15m;

            var relevantStrikes = chainDef.Strikes
                .Where(s => Math.Abs(s - underlyingPrice) <= strikeRange)
                .ToList();

            // Step 4: Fetch Greeks/IV for each contract with throttling
            var throttle = new SemaphoreSlim(_config.MaxConcurrentOptionRequests);
            var tasks = new List<Task<OptionContract?>>();

            foreach (var exp in relevantExpirations)
            {
                foreach (var strike in relevantStrikes)
                {
                    foreach (var right in new[] { OptionRight.Call, OptionRight.Put })
                    {
                        tasks.Add(FetchOptionQuoteAsync(
                            underlying, strike, exp, right, throttle, cancellationToken));
                    }
                }
            }

            var results = await Task.WhenAll(tasks);
            var contracts = results.Where(r => r != null).ToList();

            _logger.LogInformation(
                "Retrieved {Count} option contracts for {Symbol} ({Exps} exps, {Strikes} strikes)",
                contracts.Count, underlying, relevantExpirations.Count, relevantStrikes.Count);

            return contracts!;
        }
        finally
        {
            _callbackHandler.CleanupRequest(reqId);
        }
    }

    private async Task<OptionContract?> FetchOptionQuoteAsync(
        string underlying, decimal strike, DateTime expiration, OptionRight right,
        SemaphoreSlim throttle, CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken);
        try
        {
            await Task.Delay(_config.OptionQuoteDelayMs, cancellationToken);

            var tickerId = _requestManager.GetNextRequestId();
            var contract = IBKRContractFactory.CreateOption(underlying, strike, expiration, right);
            var rightStr = right == OptionRight.Call ? "C" : "P";
            var task = _callbackHandler.RegisterOptionQuoteRequest(
                tickerId, underlying, strike, expiration, rightStr);

            _clientSocket!.reqMktData(tickerId, contract, "", true, false, []);

            try
            {
                var data = await _requestManager.WithTimeout(
                    task, _config.RequestTimeout, cancellationToken);
                return data.ToOptionContract();
            }
            catch (Exception ex) when (ex is TimeoutException or IBKRApiException)
            {
                _logger.LogDebug("Failed to fetch option quote {Symbol} {Strike} {Exp} {Right}: {Error}",
                    underlying, strike, expiration.ToString("yyyyMMdd"), right, ex.Message);
                return null;
            }
            finally
            {
                _clientSocket!.cancelMktData(tickerId);
                _callbackHandler.CleanupRequest(tickerId);
            }
        }
        finally
        {
            throttle.Release();
        }
    }

    private async Task<int> ResolveConIdAsync(string symbol, CancellationToken cancellationToken)
    {
        var reqId = _requestManager.GetNextRequestId();
        var contract = IBKRContractFactory.CreateStock(symbol);
        var task = _callbackHandler.RegisterContractDetailsRequest(reqId);

        try
        {
            _clientSocket!.reqContractDetails(reqId, contract);

            var details = await _requestManager.WithTimeout(
                task, _config.RequestTimeout, cancellationToken);

            if (details.Count == 0)
            {
                _logger.LogWarning("No contract details found for {Symbol}, using ConId=0", symbol);
                return 0;
            }

            var conId = details[0].Contract.ConId;
            _logger.LogDebug("Resolved {Symbol} ConId={ConId}", symbol, conId);
            return conId;
        }
        finally
        {
            _callbackHandler.CleanupRequest(reqId);
        }
    }

    private async Task<int> ResolveOptionConIdAsync(
        string underlying, decimal strike, DateTime expiration, OptionRight right,
        CancellationToken cancellationToken)
    {
        var reqId = _requestManager.GetNextRequestId();
        var contract = IBKRContractFactory.CreateOption(underlying, strike, expiration, right);
        var task = _callbackHandler.RegisterContractDetailsRequest(reqId);

        try
        {
            _clientSocket!.reqContractDetails(reqId, contract);

            var details = await _requestManager.WithTimeout(
                task, _config.RequestTimeout, cancellationToken);

            if (details.Count == 0)
                throw new InvalidOperationException(
                    $"Could not resolve ConId for {underlying} {strike} {expiration:yyyyMMdd} {right}");

            var conId = details[0].Contract.ConId;
            _logger.LogDebug("Resolved option ConId: {Underlying} {Strike} {Exp} {Right} → {ConId}",
                underlying, strike, expiration.ToString("yyyyMMdd"), right, conId);
            return conId;
        }
        finally
        {
            _callbackHandler.CleanupRequest(reqId);
        }
    }

    public async Task<OptionsAnalytics> GetOptionsAnalyticsAsync(string symbol,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        // Fetch 1 year of historical IV data
        var reqId = _requestManager.GetNextRequestId();
        var contract = IBKRContractFactory.CreateEquity(symbol);
        var task = _callbackHandler.RegisterHistoricalDataRequest(reqId);

        var endDateStr = DateTime.Now.ToString("yyyyMMdd-HH:mm:ss");

        try
        {
            _clientSocket!.reqHistoricalData(
                reqId, contract, endDateStr, "1 Y", "1 day",
                "OPTION_IMPLIED_VOLATILITY", 1, 1, false, []);

            var bars = await _requestManager.WithTimeout(
                task, _config.RequestTimeout * 2, cancellationToken);

            var ivHistory = bars
                .Where(b => b.Close > 0)
                .Select(b => new IVHistoryPoint
                {
                    Date = IBKRMappingExtensions.ParseBarDate(b.Time),
                    ImpliedVolatility = (decimal)b.Close
                })
                .OrderBy(p => p.Date)
                .ToList();

            if (ivHistory.Count == 0)
            {
                _logger.LogWarning("No IV history data for {Symbol}", symbol);
                return new OptionsAnalytics { Symbol = symbol, Timestamp = DateTime.UtcNow };
            }

            var currentIV = ivHistory.Last().ImpliedVolatility;
            var high52 = ivHistory.Max(p => p.ImpliedVolatility);
            var low52 = ivHistory.Min(p => p.ImpliedVolatility);

            var ivRank = high52 != low52
                ? (currentIV - low52) / (high52 - low52) * 100
                : 50m;

            var daysBelow = ivHistory.Count(p => p.ImpliedVolatility < currentIV);
            var ivPercentile = (decimal)daysBelow / ivHistory.Count * 100;

            return new OptionsAnalytics
            {
                Symbol = symbol,
                CurrentIV = currentIV,
                IVRank = Math.Round(Math.Clamp(ivRank, 0, 100), 1),
                IVPercentile = Math.Round(ivPercentile, 1),
                HistoricalVolatility20 = ivHistory.Count >= 20
                    ? ivHistory.TakeLast(20).Average(p => p.ImpliedVolatility) : 0,
                HistoricalVolatility60 = ivHistory.Count >= 60
                    ? ivHistory.TakeLast(60).Average(p => p.ImpliedVolatility) : 0,
                Timestamp = DateTime.UtcNow
            };
        }
        finally
        {
            _callbackHandler.CleanupRequest(reqId);
        }
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

    public async Task<Order> PlaceComboOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        EnsureConnected();

        if (order.Legs == null || order.Legs.Count == 0)
            throw new ArgumentException("Combo order requires at least one leg.", nameof(order));

        // Step 1: Resolve ConId for each option leg
        var comboLegs = new List<ComboLegInfo>();
        foreach (var leg in order.Legs)
        {
            var conId = await ResolveOptionConIdAsync(
                leg.UnderlyingSymbol, leg.Strike, leg.Expiration, leg.Right, cancellationToken);
            comboLegs.Add(new ComboLegInfo
            {
                ConId = conId,
                Action = leg.Action,
                Ratio = leg.Quantity
            });
        }

        // Step 2: Create BAG contract and combo order
        var underlying = order.Legs[0].UnderlyingSymbol;
        var contract = IBKRContractFactory.CreateCombo(underlying, comboLegs);
        var ibOrder = IBKROrderFactory.CreateComboOrder(order);

        var orderId = Interlocked.Increment(ref _nextOrderId);
        order.BrokerId = orderId.ToString();
        ibOrder.OrderId = orderId;

        var task = _callbackHandler.RegisterOrderPlacementRequest(orderId);

        try
        {
            _logger.LogInformation(
                "Placing combo order {OrderId}: {Action} {Qty}x {Underlying} ({LegCount} legs) net={NetPrice}",
                orderId, order.Action, order.Quantity, underlying, order.Legs.Count, order.NetLimitPrice);

            _clientSocket!.placeOrder(orderId, contract, ibOrder);

            var result = await _requestManager.WithTimeout(
                task,
                _config.RequestTimeout,
                cancellationToken);

            order.Status = IBKRMappingExtensions.ToOrderStatus(result.Status);
            order.SubmittedAt = DateTime.UtcNow;
            order.LastUpdated = DateTime.UtcNow;

            _logger.LogInformation("Combo order {OrderId} accepted: status={Status}", orderId, result.Status);
            return order;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            order.Status = OrderStatus.Error;
            order.LastUpdated = DateTime.UtcNow;
            _logger.LogError(ex, "Failed to place combo order {OrderId}", orderId);
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
