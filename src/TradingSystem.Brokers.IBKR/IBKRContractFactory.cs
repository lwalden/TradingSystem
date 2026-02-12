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
}
