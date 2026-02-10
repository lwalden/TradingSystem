using TradingSystem.Brokers.IBKR;
using TradingSystem.Core.Models;
using Xunit;

namespace TradingSystem.Tests.IBKR;

public class IBKROrderStatusMappingTests
{
    [Theory]
    [InlineData("PendingSubmit", OrderStatus.PendingSubmit)]
    [InlineData("PreSubmitted", OrderStatus.Submitted)]
    [InlineData("Submitted", OrderStatus.Submitted)]
    [InlineData("Filled", OrderStatus.Filled)]
    [InlineData("Cancelled", OrderStatus.Cancelled)]
    [InlineData("ApiCancelled", OrderStatus.Cancelled)]
    [InlineData("Inactive", OrderStatus.Rejected)]
    public void ToOrderStatus_MapsCorrectly(string ibkrStatus, OrderStatus expected)
    {
        var result = IBKRMappingExtensions.ToOrderStatus(ibkrStatus);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("")]
    [InlineData("SomethingElse")]
    public void ToOrderStatus_UnknownStatus_ReturnsError(string ibkrStatus)
    {
        var result = IBKRMappingExtensions.ToOrderStatus(ibkrStatus);

        Assert.Equal(OrderStatus.Error, result);
    }
}
