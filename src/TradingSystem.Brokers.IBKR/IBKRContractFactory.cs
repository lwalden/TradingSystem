using IBApi;
using TradingSystem.Core.Models;

namespace TradingSystem.Brokers.IBKR;

/// <summary>
/// Creates IBApi.Contract instances from domain parameters.
/// </summary>
internal static class IBKRContractFactory
{
    public static Contract CreateStock(string symbol)
    {
        return new Contract
        {
            Symbol = symbol,
            SecType = "STK",
            Exchange = "SMART",
            Currency = "USD"
        };
    }

    // Known indices that require SecType="IND" instead of "STK"
    private static readonly HashSet<string> IndexSymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        "VIX", "SPX", "NDX", "RUT", "DJX", "OEX"
    };

    public static bool IsIndex(string symbol) => IndexSymbols.Contains(symbol);

    public static Contract CreateIndex(string symbol)
    {
        return new Contract
        {
            Symbol = symbol,
            SecType = "IND",
            Exchange = "CBOE",
            Currency = "USD"
        };
    }

    public static Contract CreateEquity(string symbol)
    {
        return IsIndex(symbol) ? CreateIndex(symbol) : CreateStock(symbol);
    }

    public static Contract CreateOption(string symbol, decimal strike,
        DateTime expiration, OptionRight right)
    {
        return new Contract
        {
            Symbol = symbol,
            SecType = "OPT",
            Exchange = "SMART",
            Currency = "USD",
            Strike = (double)strike,
            LastTradeDateOrContractMonth = expiration.ToString("yyyyMMdd"),
            Right = right == OptionRight.Call ? "C" : "P",
            Multiplier = "100"
        };
    }

    /// <summary>
    /// Creates an IBKR BAG (combo) contract for multi-leg options orders.
    /// Each leg becomes a ComboLeg with a resolved ConId, action, ratio, and exchange.
    /// </summary>
    public static Contract CreateCombo(string underlying, List<ComboLegInfo> legs)
    {
        if (legs == null || legs.Count == 0)
            throw new ArgumentException("Combo contract requires at least one leg.", nameof(legs));

        var contract = new Contract
        {
            Symbol = underlying,
            SecType = "BAG",
            Exchange = "SMART",
            Currency = "USD"
        };

        contract.ComboLegs = legs.Select(leg => new ComboLeg
        {
            ConId = leg.ConId,
            Ratio = leg.Ratio,
            Action = leg.Action == OrderAction.Buy ? "BUY" : "SELL",
            Exchange = "SMART"
        }).ToList();

        return contract;
    }
}

/// <summary>
/// Info needed to construct a combo leg. ConId must be resolved before calling CreateCombo.
/// </summary>
public class ComboLegInfo
{
    public int ConId { get; set; }
    public OrderAction Action { get; set; }
    public int Ratio { get; set; } = 1;
}
