namespace TradingSystem.Brokers.IBKR;

/// <summary>
/// Manages request ID generation and timeout enforcement for IBKR API calls.
/// </summary>
internal class IBKRRequestManager
{
    private int _nextRequestId;

    public IBKRRequestManager(int startingId = 1000)
    {
        _nextRequestId = startingId;
    }

    /// <summary>
    /// Thread-safe atomic increment for request IDs.
    /// Starts high to avoid collision with TWS-assigned order IDs.
    /// </summary>
    public int GetNextRequestId() => Interlocked.Increment(ref _nextRequestId);

    /// <summary>
    /// Wraps a Task with timeout and cancellation support.
    /// </summary>
    public async Task<T> WithTimeout<T>(
        Task<T> task,
        int timeoutMs,
        CancellationToken cancellationToken,
        Action? onTimeout = null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var completedTask = await Task.WhenAny(task, Task.Delay(timeoutMs, cts.Token));

        if (completedTask == task)
        {
            await cts.CancelAsync();
            return await task; // propagate exceptions
        }

        cancellationToken.ThrowIfCancellationRequested();
        onTimeout?.Invoke();
        throw new TimeoutException($"IBKR request timed out after {timeoutMs}ms");
    }
}
