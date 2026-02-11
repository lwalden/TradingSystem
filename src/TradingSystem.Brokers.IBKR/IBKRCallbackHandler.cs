using System.Collections.Concurrent;
using IBApi;
using Microsoft.Extensions.Logging;

namespace TradingSystem.Brokers.IBKR;

/// <summary>
/// Receives all callbacks from the TWS API and routes them to pending async operations.
/// Inherits from DefaultEWrapper to get empty stubs for all unused callbacks.
/// Thread-safe: callbacks arrive on the EReader message processing thread.
/// </summary>
internal class IBKRCallbackHandler : DefaultEWrapper
{
    private readonly ILogger _logger;

    // Connection
    private TaskCompletionSource<int>? _connectionTcs;
    public int NextValidOrderId { get; private set; }

    // Account summary: keyed by reqId
    private readonly ConcurrentDictionary<int, TaskCompletionSource<AccountSummaryResult>> _accountSummaryRequests = new();
    private readonly ConcurrentDictionary<int, AccountSummaryResult> _accountSummaryBuffers = new();

    // Positions: global request (no reqId)
    private TaskCompletionSource<List<PositionData>>? _positionTcs;
    private List<PositionData> _positionBuffer = new();
    private readonly object _positionLock = new();

    // Quotes: keyed by tickerId
    private readonly ConcurrentDictionary<int, TaskCompletionSource<QuoteData>> _quoteRequests = new();
    private readonly ConcurrentDictionary<int, QuoteData> _quoteBuffers = new();

    // Historical data: keyed by reqId
    private readonly ConcurrentDictionary<int, TaskCompletionSource<List<IBApi.Bar>>> _historicalDataRequests = new();
    private readonly ConcurrentDictionary<int, List<IBApi.Bar>> _historicalDataBuffers = new();

    // Orders: keyed by orderId
    private readonly ConcurrentDictionary<int, TaskCompletionSource<OrderData>> _orderPlacementRequests = new();

    // Open orders batch request (global, no reqId)
    private TaskCompletionSource<List<OrderData>>? _openOrdersTcs;
    private Dictionary<int, OrderData> _openOrdersBuffer = new();
    private readonly object _openOrdersLock = new();

    // Events
    public event Action<int, int, string>? OnError;
    public event Action? OnConnectionClosed;
    public event Action<int, OrderData>? OnOrderStatusChanged;

    // Informational error codes that are not real errors
    private static readonly HashSet<int> InfoErrorCodes = [2104, 2106, 2107, 2108, 2158];

    public IBKRCallbackHandler(ILogger logger)
    {
        _logger = logger;
    }

    // === Registration methods (called by IBKRBrokerService before sending request) ===

    public Task<int> RegisterConnectionRequest()
    {
        _connectionTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _connectionTcs.Task;
    }

    public Task<AccountSummaryResult> RegisterAccountSummaryRequest(int reqId)
    {
        var tcs = new TaskCompletionSource<AccountSummaryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _accountSummaryBuffers[reqId] = new AccountSummaryResult();
        _accountSummaryRequests[reqId] = tcs;
        return tcs.Task;
    }

    public Task<List<PositionData>> RegisterPositionRequest()
    {
        lock (_positionLock)
        {
            _positionBuffer = new List<PositionData>();
            _positionTcs = new TaskCompletionSource<List<PositionData>>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _positionTcs.Task;
        }
    }

    public Task<QuoteData> RegisterQuoteRequest(int tickerId)
    {
        var tcs = new TaskCompletionSource<QuoteData>(TaskCreationOptions.RunContinuationsAsynchronously);
        _quoteBuffers[tickerId] = new QuoteData();
        _quoteRequests[tickerId] = tcs;
        return tcs.Task;
    }

    public Task<List<IBApi.Bar>> RegisterHistoricalDataRequest(int reqId)
    {
        var tcs = new TaskCompletionSource<List<IBApi.Bar>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _historicalDataBuffers[reqId] = new List<IBApi.Bar>();
        _historicalDataRequests[reqId] = tcs;
        return tcs.Task;
    }

    public Task<OrderData> RegisterOrderPlacementRequest(int orderId)
    {
        var tcs = new TaskCompletionSource<OrderData>(TaskCreationOptions.RunContinuationsAsynchronously);
        _orderPlacementRequests[orderId] = tcs;
        return tcs.Task;
    }

    public Task<List<OrderData>> RegisterOpenOrdersRequest()
    {
        lock (_openOrdersLock)
        {
            _openOrdersBuffer = new Dictionary<int, OrderData>();
            _openOrdersTcs = new TaskCompletionSource<List<OrderData>>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _openOrdersTcs.Task;
        }
    }

    // === Cleanup ===

    public void CleanupRequest(int reqId)
    {
        _accountSummaryRequests.TryRemove(reqId, out _);
        _accountSummaryBuffers.TryRemove(reqId, out _);
        _quoteRequests.TryRemove(reqId, out _);
        _quoteBuffers.TryRemove(reqId, out _);
        _historicalDataRequests.TryRemove(reqId, out _);
        _historicalDataBuffers.TryRemove(reqId, out _);
        _orderPlacementRequests.TryRemove(reqId, out _);
    }

    public void CleanupPositionRequest()
    {
        lock (_positionLock)
        {
            _positionTcs = null;
            _positionBuffer = new List<PositionData>();
        }
    }

    // === EWrapper callback overrides ===

    public override void connectAck()
    {
        _logger.LogDebug("IBKR connectAck received");
    }

    public override void nextValidId(int orderId)
    {
        NextValidOrderId = orderId;
        _logger.LogDebug("IBKR nextValidId: {OrderId}", orderId);
        _connectionTcs?.TrySetResult(orderId);
    }

    public override void managedAccounts(string accountsList)
    {
        _logger.LogDebug("IBKR managed accounts: {Accounts}", accountsList);
    }

    public override void connectionClosed()
    {
        _logger.LogWarning("IBKR connection closed");
        OnConnectionClosed?.Invoke();
    }

    // --- Account Summary ---

    public override void accountSummary(int reqId, string account, string tag, string value, string currency)
    {
        if (!_accountSummaryBuffers.TryGetValue(reqId, out var buffer))
            return;

        buffer.AccountId = account;

        if (decimal.TryParse(value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var decVal))
        {
            switch (tag)
            {
                case "NetLiquidation": buffer.NetLiquidation = decVal; break;
                case "TotalCashValue": buffer.TotalCashValue = decVal; break;
                case "BuyingPower": buffer.BuyingPower = decVal; break;
                case "GrossPositionValue": buffer.GrossPositionValue = decVal; break;
                case "MaintMarginReq": buffer.MaintMarginReq = decVal; break;
                case "InitMarginReq": buffer.InitMarginReq = decVal; break;
                case "AvailableFunds": buffer.AvailableFunds = decVal; break;
            }
        }
    }

    public override void accountSummaryEnd(int reqId)
    {
        if (_accountSummaryRequests.TryGetValue(reqId, out var tcs) &&
            _accountSummaryBuffers.TryGetValue(reqId, out var buffer))
        {
            tcs.TrySetResult(buffer);
        }
    }

    // --- Positions ---

    public override void position(string account, Contract contract, decimal pos, double avgCost)
    {
        lock (_positionLock)
        {
            _positionBuffer.Add(new PositionData
            {
                Account = account,
                Symbol = contract.Symbol,
                SecType = contract.SecType,
                Quantity = pos,
                AverageCost = avgCost,
                Strike = contract.Strike > 0 && contract.Strike < double.MaxValue ? (decimal)contract.Strike : null,
                LastTradeDateOrContractMonth = contract.LastTradeDateOrContractMonth,
                Right = contract.Right,
                UnderlyingSymbol = contract.Symbol
            });
        }
    }

    public override void positionEnd()
    {
        lock (_positionLock)
        {
            _positionTcs?.TrySetResult(new List<PositionData>(_positionBuffer));
        }
    }

    // --- Market Data (Quotes) ---

    public override void tickPrice(int tickerId, int field, double price, TickAttrib attribs)
    {
        if (!_quoteBuffers.TryGetValue(tickerId, out var quote))
            return;

        var decPrice = (decimal)price;
        switch (field)
        {
            case 1: quote.Bid = decPrice; break;    // BID
            case 2: quote.Ask = decPrice; break;    // ASK
            case 4: quote.Last = decPrice; break;   // LAST
            case 6: quote.High = decPrice; break;   // HIGH
            case 7: quote.Low = decPrice; break;    // LOW
            case 9: quote.Close = decPrice; break;  // CLOSE
        }
    }

    public override void tickSize(int tickerId, int field, decimal size)
    {
        if (!_quoteBuffers.TryGetValue(tickerId, out var quote))
            return;

        switch (field)
        {
            case 0: quote.BidSize = size; break;    // BID_SIZE
            case 3: quote.AskSize = size; break;    // ASK_SIZE
            case 8: quote.Volume = (long)size; break; // VOLUME
        }
    }

    public override void tickSnapshotEnd(int tickerId)
    {
        if (_quoteRequests.TryGetValue(tickerId, out var tcs) &&
            _quoteBuffers.TryGetValue(tickerId, out var quote))
        {
            tcs.TrySetResult(quote);
        }
    }

    // --- Historical Data ---

    public override void historicalData(int reqId, IBApi.Bar bar)
    {
        if (_historicalDataBuffers.TryGetValue(reqId, out var buffer))
        {
            buffer.Add(bar);
        }
    }

    public override void historicalDataEnd(int reqId, string start, string end)
    {
        if (_historicalDataRequests.TryGetValue(reqId, out var tcs) &&
            _historicalDataBuffers.TryGetValue(reqId, out var buffer))
        {
            tcs.TrySetResult(buffer);
        }
    }

    // --- Orders ---

    public override void openOrder(int orderId, Contract contract, IBApi.Order order, OrderState orderState)
    {
        var orderData = new OrderData
        {
            OrderId = orderId,
            Symbol = contract.Symbol,
            SecType = contract.SecType,
            Action = order.Action,
            TotalQuantity = order.TotalQuantity,
            OrderType = order.OrderType,
            LmtPrice = order.LmtPrice,
            AuxPrice = order.AuxPrice,
            Tif = order.Tif,
            Status = orderState.Status
        };

        _logger.LogDebug("IBKR openOrder: orderId={OrderId} {Symbol} {Action} {Qty} {Type} status={Status}",
            orderId, contract.Symbol, order.Action, order.TotalQuantity, order.OrderType, orderState.Status);

        // Complete the placement TCS if waiting
        if (_orderPlacementRequests.TryGetValue(orderId, out var tcs))
        {
            tcs.TrySetResult(orderData);
        }

        // Accumulate for open orders batch request (dedup by orderId)
        lock (_openOrdersLock)
        {
            if (_openOrdersTcs != null)
            {
                _openOrdersBuffer[orderId] = orderData;
            }
        }
    }

    public override void orderStatus(int orderId, string status, decimal filled, decimal remaining,
        double avgFillPrice, long permId, int parentId, double lastFillPrice, int clientId,
        string whyHeld, double mktCapPrice)
    {
        _logger.LogDebug("IBKR orderStatus: orderId={OrderId} status={Status} filled={Filled} remaining={Remaining} avgPrice={AvgPrice}",
            orderId, status, filled, remaining, avgFillPrice);

        var update = new OrderData
        {
            OrderId = orderId,
            Status = status,
            Filled = filled,
            Remaining = remaining,
            AvgFillPrice = avgFillPrice,
            WhyHeld = whyHeld ?? string.Empty
        };

        OnOrderStatusChanged?.Invoke(orderId, update);
    }

    public override void openOrderEnd()
    {
        _logger.LogDebug("IBKR openOrderEnd");
        lock (_openOrdersLock)
        {
            _openOrdersTcs?.TrySetResult(new List<OrderData>(_openOrdersBuffer.Values));
        }
    }

    public override void execDetails(int reqId, Contract contract, Execution execution)
    {
        _logger.LogDebug("IBKR execDetails: orderId={OrderId} {Symbol} {Side} {Shares}@{Price}",
            execution.OrderId, contract.Symbol, execution.Side, execution.Shares, execution.Price);
    }

    public override void execDetailsEnd(int reqId)
    {
        _logger.LogDebug("IBKR execDetailsEnd: reqId={ReqId}", reqId);
    }

    // --- Errors ---

    public override void error(Exception e)
    {
        _logger.LogError(e, "IBKR exception");
    }

    public override void error(string str)
    {
        _logger.LogError("IBKR error: {Message}", str);
    }

    public override void error(int id, long errorTime, int errorCode, string errorMsg, string advancedOrderRejectJson)
    {
        if (InfoErrorCodes.Contains(errorCode))
        {
            _logger.LogDebug("IBKR info [{ErrorCode}]: {ErrorMsg}", errorCode, errorMsg);
            return;
        }

        _logger.LogError("IBKR error [{ErrorCode}] reqId={ReqId}: {ErrorMsg}", errorCode, id, errorMsg);
        OnError?.Invoke(id, errorCode, errorMsg);

        // Fault any pending request matching this reqId
        if (id >= 0)
        {
            var ex = new IBKRApiException(errorCode, errorMsg, id);
            FaultRequest(id, ex);
        }

        // Connection-level errors
        if (errorCode is 504 or 1100 or 1101 or 1102)
        {
            FaultAllPendingRequests(new IBKRApiException(errorCode, errorMsg));
        }
    }

    private void FaultRequest(int reqId, Exception ex)
    {
        if (_accountSummaryRequests.TryGetValue(reqId, out var acctTcs))
            acctTcs.TrySetException(ex);
        if (_quoteRequests.TryGetValue(reqId, out var quoteTcs))
            quoteTcs.TrySetException(ex);
        if (_historicalDataRequests.TryGetValue(reqId, out var histTcs))
            histTcs.TrySetException(ex);
        if (_orderPlacementRequests.TryGetValue(reqId, out var orderTcs))
            orderTcs.TrySetException(ex);
    }

    private void FaultAllPendingRequests(Exception ex)
    {
        foreach (var tcs in _accountSummaryRequests.Values) tcs.TrySetException(ex);
        foreach (var tcs in _quoteRequests.Values) tcs.TrySetException(ex);
        foreach (var tcs in _historicalDataRequests.Values) tcs.TrySetException(ex);
        foreach (var tcs in _orderPlacementRequests.Values) tcs.TrySetException(ex);
        lock (_positionLock)
        {
            _positionTcs?.TrySetException(ex);
        }
        lock (_openOrdersLock)
        {
            _openOrdersTcs?.TrySetException(ex);
        }
    }
}
