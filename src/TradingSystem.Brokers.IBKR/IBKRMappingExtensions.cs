using System.Globalization;
using TradingSystem.Core.Models;

namespace TradingSystem.Brokers.IBKR;

/// <summary>
/// Maps IBKR internal data types to domain models.
/// </summary>
internal static class IBKRMappingExtensions
{
    public static Account ToAccount(this AccountSummaryResult summary)
    {
        return new Account
        {
            AccountId = summary.AccountId,
            NetLiquidationValue = summary.NetLiquidation,
            TotalCashValue = summary.TotalCashValue,
            BuyingPower = summary.BuyingPower,
            GrossPositionValue = summary.GrossPositionValue,
            MaintenanceMargin = summary.MaintMarginReq,
            InitialMargin = summary.InitMarginReq,
            AvailableFunds = summary.AvailableFunds,
            LastUpdated = DateTime.UtcNow
        };
    }

    public static Position ToPosition(this PositionData posData)
    {
        var position = new Position
        {
            Symbol = posData.Symbol,
            SecurityType = posData.SecType,
            Quantity = posData.Quantity,
            AverageCost = (decimal)posData.AverageCost,
            LastUpdated = DateTime.UtcNow
        };

        if (posData.SecType == "OPT")
        {
            position.UnderlyingSymbol = posData.UnderlyingSymbol ?? posData.Symbol;
            position.Strike = posData.Strike;
            position.Right = posData.Right == "C" ? OptionRight.Call : OptionRight.Put;
            if (DateTime.TryParseExact(posData.LastTradeDateOrContractMonth,
                "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exp))
            {
                position.Expiration = exp;
            }
        }

        return position;
    }

    public static Quote ToQuote(this QuoteData quoteData, string symbol)
    {
        return new Quote
        {
            Symbol = symbol,
            Bid = quoteData.Bid,
            Ask = quoteData.Ask,
            Last = quoteData.Last,
            BidSize = (int)quoteData.BidSize,
            AskSize = (int)quoteData.AskSize,
            Volume = quoteData.Volume,
            Timestamp = DateTime.UtcNow
        };
    }

    public static PriceBar ToPriceBar(this IBApi.Bar bar, string symbol, BarTimeframe timeframe)
    {
        return new PriceBar
        {
            Symbol = symbol,
            Timestamp = ParseBarDate(bar.Time),
            Open = (decimal)bar.Open,
            High = (decimal)bar.High,
            Low = (decimal)bar.Low,
            Close = (decimal)bar.Close,
            Volume = (long)bar.Volume,
            Timeframe = timeframe
        };
    }

    public static string ToIBKRBarSize(this BarTimeframe timeframe)
    {
        return timeframe switch
        {
            BarTimeframe.Minute1 => "1 min",
            BarTimeframe.Minute5 => "5 mins",
            BarTimeframe.Minute15 => "15 mins",
            BarTimeframe.Minute30 => "30 mins",
            BarTimeframe.Hour1 => "1 hour",
            BarTimeframe.Hour4 => "4 hours",
            BarTimeframe.Daily => "1 day",
            BarTimeframe.Weekly => "1 week",
            BarTimeframe.Monthly => "1 month",
            _ => throw new ArgumentOutOfRangeException(nameof(timeframe))
        };
    }

    public static string ToIBKRDuration(DateTime startDate, DateTime endDate)
    {
        var days = (endDate - startDate).Days;
        if (days <= 0) days = 1;
        if (days <= 365) return $"{days} D";
        var years = (days + 364) / 365; // round up
        return $"{years} Y";
    }

    private static DateTime ParseBarDate(string dateStr)
    {
        if (DateTime.TryParseExact(dateStr, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date))
            return date;
        if (DateTime.TryParseExact(dateStr, "yyyyMMdd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var dateTime))
            return dateTime;
        // Unix timestamp format (used for some intraday bars)
        if (long.TryParse(dateStr, out var unixTime))
            return DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime;
        return DateTime.Parse(dateStr, CultureInfo.InvariantCulture);
    }
}
