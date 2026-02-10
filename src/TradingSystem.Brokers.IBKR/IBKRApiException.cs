namespace TradingSystem.Brokers.IBKR;

public class IBKRApiException : Exception
{
    public int ErrorCode { get; }
    public int RequestId { get; }

    public IBKRApiException(int errorCode, string message, int requestId = -1)
        : base($"IBKR Error [{errorCode}]: {message}")
    {
        ErrorCode = errorCode;
        RequestId = requestId;
    }

    public bool IsRetryable => ErrorCode switch
    {
        162 => true,  // Historical data pacing violation
        100 => true,  // Max tickers reached
        _ => false
    };
}
