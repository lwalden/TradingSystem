using Microsoft.Extensions.Logging;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Core.Services;

/// <summary>
/// Simple execution service that places orders directly via the broker.
/// No order laddering or advanced execution logic -- suitable for income sleeve
/// reinvestment where orders are small limit buys.
/// </summary>
public class SimpleExecutionService : IExecutionService
{
    private readonly IBrokerService _broker;
    private readonly IOrderRepository _orderRepository;
    private readonly ISignalRepository _signalRepository;
    private readonly ILogger<SimpleExecutionService> _logger;

    public SimpleExecutionService(
        IBrokerService broker,
        IOrderRepository orderRepository,
        ISignalRepository signalRepository,
        ILogger<SimpleExecutionService> logger)
    {
        _broker = broker;
        _orderRepository = orderRepository;
        _signalRepository = signalRepository;
        _logger = logger;
    }

    public async Task<ExecutionResult> ExecuteSignalAsync(Signal signal,
        CancellationToken cancellationToken = default)
    {
        var result = new ExecutionResult { SignalId = signal.Id };

        try
        {
            var order = CreateOrderFromSignal(signal);

            _logger.LogInformation(
                "Executing signal {SignalId}: {Action} {Qty} {Symbol} @ {Price}",
                signal.Id, order.Action, order.Quantity, order.Symbol, order.LimitPrice);

            var placedOrder = await _broker.PlaceOrderAsync(order, cancellationToken);
            await _orderRepository.SaveAsync(placedOrder, cancellationToken);

            signal.WasExecuted = true;
            signal.ExecutedOrderId = placedOrder.Id;
            signal.Status = SignalStatus.Executed;
            signal.ExecutionNotes = $"Order {placedOrder.BrokerId} placed, status={placedOrder.Status}";
            await _signalRepository.UpdateStatusAsync(signal.Id, SignalStatus.Executed,
                signal.ExecutionNotes, cancellationToken);

            result.Success = true;
            result.Orders.Add(placedOrder);

            _logger.LogInformation("Signal {SignalId} executed: order {BrokerId} status={Status}",
                signal.Id, placedOrder.BrokerId, placedOrder.Status);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;

            signal.Status = SignalStatus.Rejected;
            signal.ExecutionNotes = $"Execution failed: {ex.Message}";
            await _signalRepository.UpdateStatusAsync(signal.Id, SignalStatus.Rejected,
                signal.ExecutionNotes, cancellationToken);

            _logger.LogError(ex, "Failed to execute signal {SignalId}", signal.Id);
        }

        return result;
    }

    public async Task<List<ExecutionResult>> ExecuteSignalsAsync(IEnumerable<Signal> signals,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ExecutionResult>();
        foreach (var signal in signals)
        {
            var result = await ExecuteSignalAsync(signal, cancellationToken);
            results.Add(result);
        }
        return results;
    }

    public Task<bool> UpdateStopAsync(string symbol, decimal newStopPrice,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Stop updates planned for tactical sleeve.");
    }

    public Task<ExecutionResult> ClosePositionAsync(string symbol, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Position closing planned for tactical sleeve.");
    }

    public Task<ExecutionStatus> GetExecutionStatusAsync(string signalId,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Execution status tracking planned for Phase 2.");
    }

    private static Order CreateOrderFromSignal(Signal signal)
    {
        var action = signal.Direction switch
        {
            SignalDirection.Long => OrderAction.Buy,
            SignalDirection.Short => OrderAction.SellShort,
            SignalDirection.ClosePosition => OrderAction.Sell,
            SignalDirection.ReducePosition => OrderAction.Sell,
            _ => OrderAction.Buy
        };

        var orderType = signal.SuggestedEntryPrice.HasValue
            ? OrderType.Limit
            : OrderType.Market;

        return new Order
        {
            Symbol = signal.Symbol,
            SecurityType = signal.SecurityType,
            Action = action,
            Quantity = signal.SuggestedPositionSize ?? 0,
            OrderType = orderType,
            LimitPrice = signal.SuggestedEntryPrice,
            TimeInForce = TimeInForce.Day,
            Sleeve = SleeveType.Income,
            StrategyId = signal.StrategyId,
            SignalId = signal.Id,
            Rationale = signal.Rationale
        };
    }
}
