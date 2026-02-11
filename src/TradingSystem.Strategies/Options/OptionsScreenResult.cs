using TradingSystem.Core.Models;

namespace TradingSystem.Strategies.Options;

/// <summary>
/// Result of an options screening pass, grouped by strategy type.
/// </summary>
public class OptionsScreenResult
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public RegimeType MarketRegime { get; set; }

    // Candidates grouped by strategy
    public List<OptionCandidate> CSPCandidates { get; set; } = new();
    public List<OptionCandidate> BullPutSpreadCandidates { get; set; } = new();
    public List<OptionCandidate> BearCallSpreadCandidates { get; set; } = new();
    public List<OptionCandidate> IronCondorCandidates { get; set; } = new();
    public List<OptionCandidate> CalendarSpreadCandidates { get; set; } = new();

    // Metadata
    public int SymbolsScanned { get; set; }
    public int SymbolsFilteredByNoTrade { get; set; }
    public int SymbolsFilteredByIV { get; set; }
    public int SymbolsFilteredByLiquidity { get; set; }
    public List<string> Errors { get; set; } = new();

    public int TotalCandidates =>
        CSPCandidates.Count + BullPutSpreadCandidates.Count +
        BearCallSpreadCandidates.Count + IronCondorCandidates.Count +
        CalendarSpreadCandidates.Count;
}
