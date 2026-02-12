using System.Text.Json;
using System.Text.Json.Serialization;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using Xunit;

namespace TradingSystem.Tests.Options;

public class OptionsPositionTests
{
    [Fact]
    public void UnrealizedPnL_CreditSpread_PositiveWhenCurrentValueDecrease()
    {
        // Sold spread for $1.00 credit, now worth $0.40
        var position = CreateCreditSpread(entryCredit: 1.00m, currentValue: 0.40m);

        // Profit = (1.00 - 0.40) * 100 * 1 = $60
        Assert.Equal(60m, position.UnrealizedPnL);
    }

    [Fact]
    public void UnrealizedPnL_CreditSpread_NegativeWhenCurrentValueIncrease()
    {
        // Sold spread for $1.00 credit, now worth $2.50
        var position = CreateCreditSpread(entryCredit: 1.00m, currentValue: 2.50m);

        // Loss = (1.00 - 2.50) * 100 * 1 = -$150
        Assert.Equal(-150m, position.UnrealizedPnL);
    }

    [Fact]
    public void UnrealizedPnL_MultipleContracts()
    {
        var position = CreateCreditSpread(entryCredit: 1.00m, currentValue: 0.50m, quantity: 3);

        // Profit = (1.00 - 0.50) * 100 * 3 = $150
        Assert.Equal(150m, position.UnrealizedPnL);
    }

    [Fact]
    public void UnrealizedPnL_BreakEven_ReturnsZero()
    {
        var position = CreateCreditSpread(entryCredit: 1.00m, currentValue: 1.00m);

        Assert.Equal(0m, position.UnrealizedPnL);
    }

    [Fact]
    public void UnrealizedPnLPercent_HalfMaxProfit()
    {
        // Max profit = $2.00 per contract, currently at $1.00 profit per contract
        var position = CreateCreditSpread(entryCredit: 2.00m, currentValue: 1.00m, maxProfit: 2.00m);

        // 50% of max profit
        Assert.Equal(50m, position.UnrealizedPnLPercent);
    }

    [Fact]
    public void UnrealizedPnLPercent_FullMaxProfit()
    {
        var position = CreateCreditSpread(entryCredit: 2.00m, currentValue: 0m, maxProfit: 2.00m);

        Assert.Equal(100m, position.UnrealizedPnLPercent);
    }

    [Fact]
    public void UnrealizedPnLPercent_ZeroMaxProfit_ReturnsZero()
    {
        // Explicitly set MaxProfit to 0 (bypass helper default)
        var position = new OptionsPosition
        {
            EntryNetCredit = 1.00m,
            CurrentValue = 0.50m,
            MaxProfit = 0m,
            Quantity = 1,
            Expiration = DateTime.Today.AddDays(30)
        };

        Assert.Equal(0m, position.UnrealizedPnLPercent);
    }

    [Fact]
    public void UnrealizedPnLPercent_NegativePnL()
    {
        var position = CreateCreditSpread(entryCredit: 1.00m, currentValue: 2.00m, maxProfit: 1.00m);

        // -100% of max profit
        Assert.Equal(-100m, position.UnrealizedPnLPercent);
    }

    [Fact]
    public void DTE_FutureExpiration()
    {
        var position = new OptionsPosition
        {
            Expiration = DateTime.Today.AddDays(30)
        };

        Assert.Equal(30, position.DTE);
    }

    [Fact]
    public void DTE_TodayExpiration_ReturnsZero()
    {
        var position = new OptionsPosition
        {
            Expiration = DateTime.Today
        };

        Assert.Equal(0, position.DTE);
    }

    [Fact]
    public void DTE_PastExpiration_ReturnsZero()
    {
        var position = new OptionsPosition
        {
            Expiration = DateTime.Today.AddDays(-5)
        };

        Assert.Equal(0, position.DTE);
    }

    [Fact]
    public void PartitionKey_FormattedAsYearMonth()
    {
        var position = new OptionsPosition
        {
            OpenedAt = new DateTime(2026, 3, 15)
        };

        Assert.Equal("2026-03", position.PartitionKey);
    }

    [Fact]
    public void DefaultStatus_IsOpen()
    {
        var position = new OptionsPosition();

        Assert.Equal(OptionsPositionStatus.Open, position.Status);
    }

    [Fact]
    public void DefaultSleeve_IsTactical()
    {
        var position = new OptionsPosition();

        Assert.Equal(SleeveType.Tactical, position.Sleeve);
    }

    [Fact]
    public void DefaultQuantity_IsOne()
    {
        var position = new OptionsPosition();

        Assert.Equal(1, position.Quantity);
    }

    [Fact]
    public void SerializationRoundtrip_PreservesAllFields()
    {
        var position = new OptionsPosition
        {
            Id = "test-123",
            UnderlyingSymbol = "SPY",
            Strategy = StrategyType.BullPutSpread,
            Sleeve = SleeveType.Tactical,
            EntryNetCredit = 1.25m,
            MaxProfit = 1.25m,
            MaxLoss = 3.75m,
            Quantity = 2,
            CurrentValue = 0.50m,
            Status = OptionsPositionStatus.Open,
            Expiration = new DateTime(2026, 3, 20),
            EntryIVRank = 65.5m,
            SignalId = "sig-456",
            OrderIds = new List<string> { "ord-1", "ord-2" },
            OpenedAt = new DateTime(2026, 2, 12, 14, 30, 0),
            Legs = new List<OptionsPositionLeg>
            {
                new()
                {
                    Symbol = "SPY260320P00580000",
                    Strike = 580m,
                    Expiration = new DateTime(2026, 3, 20),
                    Right = OptionRight.Put,
                    Action = OrderAction.Sell,
                    Quantity = 1,
                    EntryPrice = 3.50m,
                    CurrentPrice = 2.00m,
                    ConId = 123456
                },
                new()
                {
                    Symbol = "SPY260320P00575000",
                    Strike = 575m,
                    Expiration = new DateTime(2026, 3, 20),
                    Right = OptionRight.Put,
                    Action = OrderAction.Buy,
                    Quantity = 1,
                    EntryPrice = 2.25m,
                    CurrentPrice = 1.50m,
                    ConId = 123457
                }
            }
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        var json = JsonSerializer.Serialize(position, options);
        var deserialized = JsonSerializer.Deserialize<OptionsPosition>(json, options);

        Assert.NotNull(deserialized);
        Assert.Equal(position.Id, deserialized!.Id);
        Assert.Equal(position.UnderlyingSymbol, deserialized.UnderlyingSymbol);
        Assert.Equal(position.Strategy, deserialized.Strategy);
        Assert.Equal(position.EntryNetCredit, deserialized.EntryNetCredit);
        Assert.Equal(position.MaxProfit, deserialized.MaxProfit);
        Assert.Equal(position.MaxLoss, deserialized.MaxLoss);
        Assert.Equal(position.Quantity, deserialized.Quantity);
        Assert.Equal(position.CurrentValue, deserialized.CurrentValue);
        Assert.Equal(position.Status, deserialized.Status);
        Assert.Equal(position.EntryIVRank, deserialized.EntryIVRank);
        Assert.Equal(position.SignalId, deserialized.SignalId);
        Assert.Equal(2, deserialized.OrderIds.Count);
        Assert.Equal(2, deserialized.Legs.Count);
        Assert.Equal(580m, deserialized.Legs[0].Strike);
        Assert.Equal(OrderAction.Sell, deserialized.Legs[0].Action);
        Assert.Equal(123456, deserialized.Legs[0].ConId);
    }

    private static OptionsPosition CreateCreditSpread(
        decimal entryCredit, decimal currentValue,
        decimal maxProfit = 0m, int quantity = 1)
    {
        return new OptionsPosition
        {
            UnderlyingSymbol = "SPY",
            Strategy = StrategyType.BullPutSpread,
            EntryNetCredit = entryCredit,
            MaxProfit = maxProfit > 0 ? maxProfit : entryCredit,
            MaxLoss = 5m - entryCredit,
            CurrentValue = currentValue,
            Quantity = quantity,
            Expiration = DateTime.Today.AddDays(30),
            Legs = new List<OptionsPositionLeg>
            {
                new() { Strike = 580m, Right = OptionRight.Put, Action = OrderAction.Sell },
                new() { Strike = 575m, Right = OptionRight.Put, Action = OrderAction.Buy }
            }
        };
    }
}
