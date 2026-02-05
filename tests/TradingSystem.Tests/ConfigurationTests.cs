using TradingSystem.Core.Configuration;
using TradingSystem.Core.Models;
using Xunit;

namespace TradingSystem.Tests;

public class ConfigurationTests
{
    [Fact]
    public void DefaultConfig_HasCorrectSleeveAllocations()
    {
        var config = new TradingSystemConfig();
        
        Assert.Equal(0.70m, config.IncomeTargetPercent);
        Assert.Equal(0.30m, config.TacticalTargetPercent);
    }

    [Fact]
    public void IncomeConfig_AllocationTargetsSumToOne()
    {
        var config = new IncomeConfig();
        var sum = config.AllocationTargets.Values.Sum();
        
        Assert.Equal(1.0m, sum);
    }

    [Fact]
    public void RiskConfig_HasSaneDefaults()
    {
        var config = new RiskConfig();
        
        Assert.True(config.RiskPerTradePercent <= 0.01m); // Max 1% per trade
        Assert.True(config.DailyStopPercent <= 0.05m); // Max 5% daily
        Assert.True(config.MaxGrossLeverage <= 2.0m); // Max 2x leverage
    }
}

public class PositionSizingTests
{
    [Fact]
    public void CalculateShares_RisksCorrectAmount()
    {
        // Arrange
        decimal accountEquity = 100_000m;
        decimal riskPercent = 0.004m; // 0.4%
        decimal entryPrice = 50m;
        decimal stopPrice = 48m;
        
        // Act
        decimal riskAmount = accountEquity * riskPercent; // $400
        decimal riskPerShare = entryPrice - stopPrice; // $2
        int shares = (int)(riskAmount / riskPerShare); // 200 shares
        
        // Assert
        Assert.Equal(400m, riskAmount);
        Assert.Equal(200, shares);
        Assert.Equal(10_000m, shares * entryPrice); // Position size
    }

    [Fact]
    public void OptionsSpreadSizing_RespectsMaxRisk()
    {
        // Arrange
        decimal accountEquity = 100_000m;
        decimal riskPercent = 0.004m;
        decimal spreadWidth = 5m; // $5 wide spread
        decimal credit = 1.50m;
        
        // Act
        decimal maxLossPerSpread = (spreadWidth - credit) * 100; // $350 per contract
        decimal riskAmount = accountEquity * riskPercent; // $400
        int contracts = (int)(riskAmount / maxLossPerSpread); // 1 contract
        
        // Assert
        Assert.Equal(1, contracts);
    }
}

public class SignalTests
{
    [Fact]
    public void Signal_IsValid_WhenNotExpired()
    {
        var signal = new Signal
        {
            GeneratedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(8),
            Status = SignalStatus.Active
        };
        
        Assert.True(signal.IsValid);
    }

    [Fact]
    public void Signal_IsInvalid_WhenExpired()
    {
        var signal = new Signal
        {
            GeneratedAt = DateTime.UtcNow.AddHours(-10),
            ExpiresAt = DateTime.UtcNow.AddHours(-2),
            Status = SignalStatus.Active
        };
        
        Assert.False(signal.IsValid);
    }
}
