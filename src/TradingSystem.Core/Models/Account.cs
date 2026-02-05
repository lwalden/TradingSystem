namespace TradingSystem.Core.Models;

/// <summary>
/// Represents the trading account state
/// </summary>
public class Account
{
    public string AccountId { get; set; } = string.Empty;
    public decimal NetLiquidationValue { get; set; }
    public decimal TotalCashValue { get; set; }
    public decimal BuyingPower { get; set; }
    public decimal AvailableFunds { get; set; }
    public decimal GrossPositionValue { get; set; }
    public decimal MaintenanceMargin { get; set; }
    public decimal InitialMargin { get; set; }
    
    // Calculated sleeve values
    public decimal IncomeSleeveValue { get; set; }
    public decimal TacticalSleeveValue { get; set; }
    public decimal CashBufferValue { get; set; }
    
    // Percentages
    public decimal IncomeSleevePercent => NetLiquidationValue != 0 ? IncomeSleeveValue / NetLiquidationValue * 100 : 0;
    public decimal TacticalSleevePercent => NetLiquidationValue != 0 ? TacticalSleeveValue / NetLiquidationValue * 100 : 0;
    
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public List<Position> Positions { get; set; } = new();
}
