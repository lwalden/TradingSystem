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
