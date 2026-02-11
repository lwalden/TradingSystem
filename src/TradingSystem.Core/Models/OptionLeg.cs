namespace TradingSystem.Core.Models;

/// <summary>
/// Represents a single leg in a multi-leg options strategy.
/// </summary>
public class OptionLeg
{
    public string Symbol { get; set; } = string.Empty;
    public string UnderlyingSymbol { get; set; } = string.Empty;
    public decimal Strike { get; set; }
    public DateTime Expiration { get; set; }
    public OptionRight Right { get; set; }
    public OrderAction Action { get; set; }
    public int Quantity { get; set; } = 1;

    // Greeks/pricing
    public decimal? Delta { get; set; }
    public decimal? Theta { get; set; }
    public decimal? ImpliedVolatility { get; set; }
    public decimal? Bid { get; set; }
    public decimal? Ask { get; set; }
    public decimal? Mid => (Bid.HasValue && Ask.HasValue) ? (Bid.Value + Ask.Value) / 2 : null;

    public int DTE => (Expiration.Date - DateTime.Today).Days;
}
