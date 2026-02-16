using Microsoft.Extensions.Logging;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Core.Services;

/// <summary>
/// Execution service for options signals, including combo order routing and position persistence.
/// </summary>
public class OptionsExecutionService
{
    private readonly IBrokerService _broker;
    private readonly IOrderRepository _orderRepository;
    private readonly ISignalRepository _signalRepository;
    private readonly IOptionsPositionRepository _optionsPositionRepository;
    private readonly ILogger<OptionsExecutionService> _logger;

    public OptionsExecutionService(
        IBrokerService broker,
        IOrderRepository orderRepository,
        ISignalRepository signalRepository,
        IOptionsPositionRepository optionsPositionRepository,
        ILogger<OptionsExecutionService> logger)
    {
        _broker = broker;
        _orderRepository = orderRepository;
        _signalRepository = signalRepository;
        _optionsPositionRepository = optionsPositionRepository;
        _logger = logger;
    }

    public async Task<ExecutionResult> ExecuteSignalAsync(
        Signal signal,
        CancellationToken cancellationToken = default)
    {
        var result = new ExecutionResult { SignalId = signal.Id };

        await _signalRepository.SaveAsync(signal, cancellationToken);

        try
        {
            var order = CreateOrderFromSignal(signal);
            var isCombo = order.Legs is { Count: > 0 };

            _logger.LogInformation(
                "Executing options signal {SignalId}: symbol={Symbol}, combo={IsCombo}, qty={Qty}",
                signal.Id, signal.Symbol, isCombo, order.Quantity);

            var placedOrder = isCombo
                ? await _broker.PlaceComboOrderAsync(order, cancellationToken)
                : await _broker.PlaceOrderAsync(order, cancellationToken);

            await _orderRepository.SaveAsync(placedOrder, cancellationToken);

            signal.WasExecuted = true;
            signal.ExecutedOrderId = placedOrder.Id;
            signal.Status = SignalStatus.Executed;
            signal.ExecutionNotes = $"Order {placedOrder.BrokerId} placed, status={placedOrder.Status}";
            await _signalRepository.UpdateStatusAsync(
                signal.Id,
                SignalStatus.Executed,
                signal.ExecutionNotes,
                cancellationToken);

            if (IsEntrySignal(signal) && signal.SuggestedLegs is { Count: > 0 })
            {
                var optionsPosition = BuildPositionFromEntrySignal(signal, placedOrder);
                await _optionsPositionRepository.SaveAsync(optionsPosition, cancellationToken);
            }
            else if (signal.Direction == SignalDirection.ClosePosition &&
                     TryGetIndicatorString(signal, "positionId", out var positionId))
            {
                await MarkPositionClosingAsync(positionId, placedOrder.Id, signal, cancellationToken);
            }

            result.Success = true;
            result.Orders.Add(placedOrder);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            signal.Status = SignalStatus.Rejected;
            signal.ExecutionNotes = $"Execution failed: {ex.Message}";
            await _signalRepository.UpdateStatusAsync(
                signal.Id,
                SignalStatus.Rejected,
                signal.ExecutionNotes,
                cancellationToken);

            _logger.LogError(ex, "Failed to execute options signal {SignalId}", signal.Id);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    public async Task<List<ExecutionResult>> ExecuteSignalsAsync(
        IEnumerable<Signal> signals,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ExecutionResult>();
        foreach (var signal in signals)
        {
            results.Add(await ExecuteSignalAsync(signal, cancellationToken));
        }

        return results;
    }

    private async Task MarkPositionClosingAsync(
        string positionId,
        string orderId,
        Signal signal,
        CancellationToken cancellationToken)
    {
        var position = await _optionsPositionRepository.GetByIdAsync(positionId, cancellationToken);
        if (position == null)
        {
            _logger.LogWarning("Close signal referenced missing position {PositionId}", positionId);
            return;
        }

        position.Status = OptionsPositionStatus.Closing;
        position.ExitReason = TryGetIndicatorString(signal, "exitReason", out var reason)
            ? reason
            : signal.Rationale;
        position.LastUpdated = DateTime.UtcNow;
        position.OrderIds.Add(orderId);
        await _optionsPositionRepository.UpdateAsync(position, cancellationToken);
    }

    private static Order CreateOrderFromSignal(Signal signal)
    {
        var legs = signal.SuggestedLegs;
        var isCombo = legs is { Count: > 0 };
        var quantity = signal.SuggestedPositionSize ?? 0;
        if (quantity <= 0)
            throw new InvalidOperationException($"Signal {signal.Id} has invalid position size {quantity}.");

        var orderType = signal.SuggestedEntryPrice.HasValue ? OrderType.Limit : OrderType.Market;
        var action = isCombo ? OrderAction.Buy : MapAction(signal.Direction);

        return new Order
        {
            Symbol = signal.Symbol,
            SecurityType = signal.SecurityType,
            Action = action,
            Quantity = quantity,
            OrderType = orderType,
            LimitPrice = isCombo ? null : signal.SuggestedEntryPrice,
            NetLimitPrice = isCombo ? signal.SuggestedEntryPrice : null,
            Legs = isCombo ? legs!.Select(CloneLeg).ToList() : null,
            TimeInForce = TimeInForce.Day,
            Sleeve = SleeveType.Tactical,
            StrategyId = signal.StrategyId,
            SignalId = signal.Id,
            Rationale = signal.Rationale,
            ExpectedRMultiple = signal.ExpectedRMultiple
        };
    }

    private static bool IsEntrySignal(Signal signal) =>
        signal.Direction is SignalDirection.Long or SignalDirection.Short;

    private static OrderAction MapAction(SignalDirection direction)
    {
        return direction switch
        {
            SignalDirection.Long => OrderAction.Buy,
            SignalDirection.Short => OrderAction.SellShort,
            SignalDirection.ClosePosition => OrderAction.Sell,
            SignalDirection.ReducePosition => OrderAction.Sell,
            _ => OrderAction.Buy
        };
    }

    private static OptionLeg CloneLeg(OptionLeg leg)
    {
        return new OptionLeg
        {
            Symbol = leg.Symbol,
            UnderlyingSymbol = leg.UnderlyingSymbol,
            Strike = leg.Strike,
            Expiration = leg.Expiration,
            Right = leg.Right,
            Action = leg.Action,
            Quantity = leg.Quantity,
            Delta = leg.Delta,
            Theta = leg.Theta,
            ImpliedVolatility = leg.ImpliedVolatility,
            Bid = leg.Bid,
            Ask = leg.Ask
        };
    }

    private static OptionsPosition BuildPositionFromEntrySignal(Signal signal, Order placedOrder)
    {
        var legs = signal.SuggestedLegs!.Select(l => new OptionsPositionLeg
        {
            Symbol = l.Symbol,
            Strike = l.Strike,
            Expiration = l.Expiration,
            Right = l.Right,
            Action = l.Action,
            Quantity = l.Quantity,
            EntryPrice = l.Mid ?? 0m,
            CurrentPrice = l.Mid ?? 0m
        }).ToList();

        var quantity = Math.Max(1, (int)(signal.SuggestedPositionSize ?? 1));
        var netCredit = signal.SuggestedEntryPrice ??
                        (TryGetIndicatorDecimal(signal, "netCredit", out var indicatorNet)
                            ? indicatorNet
                            : 0m);

        var strategy = ParseStrategyType(signal);
        var expiration = legs.Count > 0
            ? legs.Min(l => l.Expiration)
            : (signal.SuggestedExpiration ?? DateTime.Today);
        var maxProfitPoints = signal.MaxProfit.HasValue
            ? signal.MaxProfit.Value / 100m
            : Math.Abs(netCredit);
        var maxLossPoints = signal.MaxLoss.HasValue
            ? signal.MaxLoss.Value / 100m
            : Math.Abs(netCredit);

        return new OptionsPosition
        {
            UnderlyingSymbol = signal.Symbol,
            Strategy = strategy,
            Sleeve = SleeveType.Tactical,
            Legs = legs,
            EntryNetCredit = netCredit,
            MaxProfit = maxProfitPoints,
            MaxLoss = maxLossPoints,
            Quantity = quantity,
            CurrentValue = netCredit,
            Status = OptionsPositionStatus.Open,
            Expiration = expiration,
            EntryIVRank = signal.IVRank ?? 0m,
            SignalId = signal.Id,
            OrderIds = new List<string> { placedOrder.Id },
            OpenedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };
    }

    private static StrategyType ParseStrategyType(Signal signal)
    {
        if (TryGetIndicatorString(signal, "strategyType", out var strategyText) &&
            Enum.TryParse(strategyText, true, out StrategyType parsedFromIndicator))
        {
            return parsedFromIndicator;
        }

        if (Enum.TryParse(signal.SetupType, true, out StrategyType parsedFromSetup))
            return parsedFromSetup;

        var strategyId = signal.StrategyId.ToLowerInvariant();
        if (strategyId.Contains("csp")) return StrategyType.CashSecuredPut;
        if (strategyId.Contains("bull-put")) return StrategyType.BullPutSpread;
        if (strategyId.Contains("bear-call")) return StrategyType.BearCallSpread;
        if (strategyId.Contains("iron-condor")) return StrategyType.IronCondor;
        if (strategyId.Contains("calendar")) return StrategyType.CalendarSpread;
        if (strategyId.Contains("covered-call")) return StrategyType.CoveredCall;
        return StrategyType.DiagonalSpread;
    }

    private static bool TryGetIndicatorString(Signal signal, string key, out string value)
    {
        if (signal.Indicators.TryGetValue(key, out var raw) && raw != null)
        {
            value = raw.ToString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetIndicatorDecimal(Signal signal, string key, out decimal value)
    {
        if (signal.Indicators.TryGetValue(key, out var raw) && raw != null)
        {
            if (raw is decimal d)
            {
                value = d;
                return true;
            }

            if (decimal.TryParse(raw.ToString(), out var parsed))
            {
                value = parsed;
                return true;
            }
        }

        value = 0m;
        return false;
    }
}
