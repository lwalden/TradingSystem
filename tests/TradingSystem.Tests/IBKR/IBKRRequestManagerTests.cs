using TradingSystem.Brokers.IBKR;
using Xunit;

namespace TradingSystem.Tests.IBKR;

public class IBKRRequestManagerTests
{
    [Fact]
    public void GetNextRequestId_ReturnsSequentialIds()
    {
        var manager = new IBKRRequestManager(1000);

        var id1 = manager.GetNextRequestId();
        var id2 = manager.GetNextRequestId();
        var id3 = manager.GetNextRequestId();

        Assert.Equal(1001, id1);
        Assert.Equal(1002, id2);
        Assert.Equal(1003, id3);
    }

    [Fact]
    public void GetNextRequestId_IsThreadSafe()
    {
        var manager = new IBKRRequestManager(0);
        var ids = new System.Collections.Concurrent.ConcurrentBag<int>();
        const int count = 1000;

        Parallel.For(0, count, _ =>
        {
            ids.Add(manager.GetNextRequestId());
        });

        var uniqueIds = ids.Distinct().ToList();
        Assert.Equal(count, uniqueIds.Count);
    }

    [Fact]
    public async Task WithTimeout_ReturnsResult_WhenTaskCompletesInTime()
    {
        var manager = new IBKRRequestManager();
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var task = manager.WithTimeout(tcs.Task, 5000, CancellationToken.None);

        tcs.SetResult(42);

        var result = await task;
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task WithTimeout_ThrowsTimeoutException_WhenTaskDoesNotComplete()
    {
        var manager = new IBKRRequestManager();
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            manager.WithTimeout(tcs.Task, 50, CancellationToken.None));
    }

    [Fact]
    public async Task WithTimeout_InvokesOnTimeout_WhenTimedOut()
    {
        var manager = new IBKRRequestManager();
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackInvoked = false;

        await Assert.ThrowsAsync<TimeoutException>(() =>
            manager.WithTimeout(tcs.Task, 50, CancellationToken.None, () => callbackInvoked = true));

        Assert.True(callbackInvoked);
    }

    [Fact]
    public async Task WithTimeout_ThrowsOperationCancelled_WhenTokenCancelled()
    {
        var manager = new IBKRRequestManager();
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();

        var task = manager.WithTimeout(tcs.Task, 30000, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task WithTimeout_PropagatesException_FromOriginalTask()
    {
        var manager = new IBKRRequestManager();
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var task = manager.WithTimeout(tcs.Task, 5000, CancellationToken.None);
        tcs.SetException(new InvalidOperationException("test error"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal("test error", ex.Message);
    }
}
