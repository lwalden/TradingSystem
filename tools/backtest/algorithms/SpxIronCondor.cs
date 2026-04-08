// QC_CloudBacktest.cs — QuantConnect Web IDE version
//
// Paste this entire file into a new C# algorithm on quantconnect.com.
// StrategyConstants is inlined here since the web IDE is single-file.
//
// After the backtest completes:
//   1. Click "Results" → note the key metrics
//   2. Download the full log (Logs tab → copy all)
//   3. Save log as backtests/lean/results/qc_cloud_log.txt
//   4. Run: uv run python scripts/parse_lean_results.py --log backtests/lean/results/qc_cloud_log.txt
//   5. Run: uv run python scripts/evaluate_phase1_gate.py

using System;
using System.Collections.Generic;
using System.Linq;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Orders;
using QuantConnect.Orders.Fees;
using QuantConnect.Orders.Slippage;
using QuantConnect.Securities;
using QuantConnect.Securities.Option;

namespace QuantConnect.Algorithm.CSharp
{
    // ── SPX Iron Condor — commission-efficient variant ──
    // Same strategy as SPY baseline but on SPX (100x multiplier).
    // Commission of $0.65/contract is trivial vs ~$20+ credit (was ~$2 on SPY).
    // Wing width 50pt (proportional to SPY 10pt at ~10x price).
    // 1 contract per position (max loss = 50pt × $100 = $5,000).
    public static class SC
    {
        public const int    EntryDteMin                = 30;
        public const int    EntryDteTarget             = 45;
        public const int    EntryDteMax                = 60;
        public const double EntryShortDeltaTarget      = 0.16d;
        public const double EntryShortDeltaTolerance   = 0.04d;
        public const int    EntryWingWidth             = 50;     // SPX points (was 10 for SPY)
        public const double EntryMinCreditToWidthRatio = 0.15d;  // $7.50 min on 50pt wings
        public const double EntryMinAtmIv              = 0.18d;
        public const double ExitProfitTargetPct        = 0.7d;
        public const double ExitStopLossCreditMultiple = 2.0d;
        public const int    ExitDteMandatoryClose      = 7;
        public const int    SizingDefaultContracts     = 1;      // 1 SPX contract (was 5 SPY)
        public const double SlippagePerLegUsd          = 0.10d;  // SPX options have wider spreads
        public const double CommissionPerContractUsd   = 0.65d;
        public const int    InitialCapitalUsd          = 400000;
        public const string DateRangeStart             = "2019-01-01";
        public const string DateRangeEnd               = "2025-12-31";
        public const string InSampleEnd                = "2022-12-31";
        public const string OosStart                   = "2023-01-01";
        public const string ParameterHash              = "spx-iron-condor-v1";
    }

    public class SpxIronCondorAlgorithm : QCAlgorithm
    {
        private Symbol _spxOption;

        private IronCondorPosition _openPosition = null;

        private int    _totalTrades   = 0;
        private int    _winningTrades = 0;
        private double _grossProfit   = 0;
        private double _grossLoss     = 0;
        private double _totalFriction = 0;  // estimated commission + slippage per trade
        private double _peakPortfolio = 0;
        private double _maxDrawdown   = 0;

        private DateTime _isEnd;
        private double   _portfolioAtIsEnd  = 0;
        private bool     _isEndRecorded     = false;
        private double   _oosStartEquity    = 0;
        private bool     _oosStartRecorded  = false;

        private DateTime _lastScanDate   = DateTime.MinValue;
        private DateTime _lastDiagDate   = DateTime.MinValue;
        private DateTime _lastNoChainWarn = DateTime.MinValue;

        public override void Initialize()
        {
            var start = DateTime.Parse(SC.DateRangeStart);
            var end   = DateTime.Parse(SC.DateRangeEnd);
            SetStartDate(start.Year, start.Month, start.Day);
            SetEndDate(end.Year, end.Month, end.Day);
            SetCash(SC.InitialCapitalUsd);

            _isEnd = DateTime.Parse(SC.InSampleEnd);

            // SPX is an index — use AddIndex + AddIndexOption
            AddIndex("SPX", Resolution.Minute);

            var option = AddIndexOption("SPX", Resolution.Minute);
            option.SetFilter(u => u
                .Expiration(TimeSpan.FromDays(SC.EntryDteMin), TimeSpan.FromDays(SC.EntryDteMax))
                .Strikes(-60, 60));  // SPX strikes 5pt apart, -60/+60 covers ~300pt each side (need 50pt wings + margin)
            _spxOption = option.Symbol;

            // InteractiveBrokersFeeModel charges per contract ($0.65/contract for US options).
            // ConstantSlippageModel(pct) applies pct * lastPrice — use 0.5% to approximate
            // ~$0.02 absolute slippage on a ~$3 option mid-price.
            SetSecurityInitializer(s => {
                s.SetFeeModel(new InteractiveBrokersFeeModel());
                s.SetSlippageModel(new ConstantSlippageModel(0.005m));
            });

            Log($"parameter_hash: {SC.ParameterHash}");
            Log($"DTE range: {SC.EntryDteMin}-{SC.EntryDteMax} (target {SC.EntryDteTarget})");
            Log($"SPX Iron Condor | Delta target: {SC.EntryShortDeltaTarget} | Wing: {SC.EntryWingWidth}pts | Profit: {SC.ExitProfitTargetPct:P0} | Stop: {SC.ExitStopLossCreditMultiple}x | Contracts: {SC.SizingDefaultContracts}");
        }

        public override void OnData(Slice data)
        {
            TrackDrawdown();

            if (!_isEndRecorded && Time.Date >= _isEnd)
            {
                _portfolioAtIsEnd = (double)Portfolio.TotalPortfolioValue;
                _isEndRecorded    = true;
            }
            if (!_oosStartRecorded && Time.Date >= DateTime.Parse(SC.OosStart))
            {
                _oosStartEquity   = (double)Portfolio.TotalPortfolioValue;
                _oosStartRecorded = true;
            }

            if (_openPosition != null)
            {
                ManageOpenPosition();
                return;
            }

            // Scan any weekday — Wednesday preferred but fall through to Th/Fr if no chain that day.
            // Limit to one scan attempt per day.
            if (Time.DayOfWeek == DayOfWeek.Saturday || Time.DayOfWeek == DayOfWeek.Sunday)
                return;
            if (Time.Date <= _lastScanDate)
                return;

            _lastScanDate = Time.Date;  // mark scanned so we don't re-scan intra-day on minute bars

            if (!data.OptionChains.ContainsKey(_spxOption))
            {
                // Warn at most once per month to diagnose data gaps without flooding logs
                if ((Time.Date - _lastNoChainWarn).TotalDays >= 30)
                {
                    Log($"WARN: no option chain on {Time.Date:yyyy-MM-dd} ({Time.DayOfWeek})");
                    _lastNoChainWarn = Time.Date;
                }
                return;
            }
            TryEnterCondor(data.OptionChains[_spxOption]);
        }

        private void TryEnterCondor(OptionChain chain)
        {
            var underlying = chain.Underlying?.Price;
            if (underlying == null || underlying <= 0) return;
            double spot = (double)underlying;

            var expiry = SelectExpiry(chain);
            if (expiry == null) { Log($"SKIP {Time.Date:yyyy-MM-dd}: no expiry in DTE range"); return; }

            var contracts = chain.Where(c => c.Expiry.Date == expiry.Value.Date).ToList();
            if (contracts.Count < 4) { Log($"SKIP {Time.Date:yyyy-MM-dd}: only {contracts.Count} contracts for expiry {expiry.Value:yyyy-MM-dd}"); return; }

            var calls = contracts.Where(c => c.Right == OptionRight.Call).OrderBy(c => c.Strike).ToList();
            var puts  = contracts.Where(c => c.Right == OptionRight.Put).OrderBy(c => c.Strike).ToList();

            // Log diagnostics quarterly
            if (calls.Count > 0 && (Time.Date - _lastDiagDate).TotalDays >= 90)
            {
                var minDelta  = calls.Min(c => Math.Abs((double)c.Greeks.Delta));
                var maxDelta  = calls.Max(c => Math.Abs((double)c.Greeks.Delta));
                var minStrike = calls.Min(c => c.Strike);
                var maxStrike = calls.Max(c => c.Strike);
                Log($"DIAG: spot={spot:F2} contracts={contracts.Count} calls={calls.Count} deltaRange=[{minDelta:F3},{maxDelta:F3}] strikeRange=[{minStrike},{maxStrike}]");
                _lastDiagDate = Time.Date;
            }

            // Try delta-based selection; fall back to strike-based (~1 SD OTM) if Greeks are zero
            bool deltaAvailable = calls.Any(c => Math.Abs((double)c.Greeks.Delta) > 0.001);

            OptionContract shortCall, shortPut;
            if (deltaAvailable)
            {
                shortCall = SelectByDelta(calls, SC.EntryShortDeltaTarget, SC.EntryShortDeltaTolerance);
                shortPut  = SelectByDelta(puts,  SC.EntryShortDeltaTarget, SC.EntryShortDeltaTolerance);
            }
            else
            {
                // 1 SD ≈ spot * IV * sqrt(DTE/365); approximate with 7% OTM for 45 DTE
                // Select the OTM call/put strike closest to spot * 1.07 / spot * 0.93
                double callTarget = spot * 1.07;
                double putTarget  = spot * 0.93;
                shortCall = calls.OrderBy(c => Math.Abs((double)c.Strike - callTarget)).FirstOrDefault();
                shortPut  = puts.OrderBy(c => Math.Abs((double)c.Strike - putTarget)).FirstOrDefault();
                if (shortCall != null && shortPut != null)
                    Log($"DIAG: Using strike-based selection (no Greeks). callStrike={shortCall.Strike} putStrike={shortPut.Strike}");
            }

            if (shortCall == null) { Log($"SKIP {Time.Date:yyyy-MM-dd}: no short call near delta {SC.EntryShortDeltaTarget}"); return; }
            // Pick the nearest call strike at or above shortCall + wing width (exact match preferred)
            var longCall = calls
                .Where(c => c.Strike >= shortCall.Strike + SC.EntryWingWidth)
                .OrderBy(c => c.Strike)
                .FirstOrDefault();
            if (longCall == null) { Log($"SKIP {Time.Date:yyyy-MM-dd}: no long call >= {shortCall.Strike + SC.EntryWingWidth}"); return; }

            if (shortPut == null) { Log($"SKIP {Time.Date:yyyy-MM-dd}: no short put near delta {SC.EntryShortDeltaTarget}"); return; }
            // Pick the nearest put strike at or below shortPut - wing width (exact match preferred)
            var longPut = puts
                .Where(c => c.Strike <= shortPut.Strike - SC.EntryWingWidth)
                .OrderByDescending(c => c.Strike)
                .FirstOrDefault();
            if (longPut == null) { Log($"SKIP {Time.Date:yyyy-MM-dd}: no long put <= {shortPut.Strike - SC.EntryWingWidth}"); return; }

            if (shortPut.Strike >= (decimal)spot || shortCall.Strike <= (decimal)spot)
            {
                Log($"SKIP {Time.Date:yyyy-MM-dd}: strikes not OTM (put={shortPut.Strike} call={shortCall.Strike} spot={spot:F2})");
                return;
            }

            // ATM IV filter: skip entry when implied vol is below floor (thin-credit environment)
            if (SC.EntryMinAtmIv > 0)
            {
                var atmCall = calls.OrderBy(c => Math.Abs((double)c.Greeks.Delta - 0.5)).FirstOrDefault();
                if (atmCall != null && (double)atmCall.ImpliedVolatility > 0.001)
                {
                    double atmIv = (double)atmCall.ImpliedVolatility;
                    if (atmIv < SC.EntryMinAtmIv)
                    {
                        Log($"SKIP {Time.Date:yyyy-MM-dd}: ATM IV={atmIv:P1} below floor {SC.EntryMinAtmIv:P1}");
                        return;
                    }
                }
            }

            double credit = Mid(shortCall) - Mid(longCall) + Mid(shortPut) - Mid(longPut);
            if (credit < SC.EntryWingWidth * SC.EntryMinCreditToWidthRatio)
            {
                Log($"Entry rejected: credit={credit:F2} below min={SC.EntryWingWidth * SC.EntryMinCreditToWidthRatio:F2}");
                return;
            }

            var legs = new List<Leg>
            {
                Leg.Create(shortCall.Symbol, -1),
                Leg.Create(longCall.Symbol,  +1),
                Leg.Create(shortPut.Symbol,  -1),
                Leg.Create(longPut.Symbol,   +1),
            };

            ComboMarketOrder(legs, SC.SizingDefaultContracts, asynchronous: false);

            _openPosition = new IronCondorPosition
            {
                Expiry        = expiry.Value.Date,
                InitialCredit = credit,
                ShortCall     = shortCall.Symbol,
                LongCall      = longCall.Symbol,
                ShortPut      = shortPut.Symbol,
                LongPut       = longPut.Symbol,
            };

            // IBKR commission: $0.65/contract × 4 legs × N contracts per side
            // Divide by 100 (option multiplier) to match per-share P&L units
            _totalFriction += 0.65 * 4 * SC.SizingDefaultContracts / 100.0;

            int dte = (expiry.Value.Date - Time.Date).Days;
            Log($"ENTRY: {shortPut.Strike}P/{shortCall.Strike}C exp={expiry.Value:yyyy-MM-dd} credit={credit:F2} DTE={dte}");
        }

        private void ManageOpenPosition()
        {
            var pos = _openPosition;
            int dte = (pos.Expiry - Time.Date).Days;

            if (dte <= SC.ExitDteMandatoryClose)
            {
                ClosePosition("DTE<=7");
                return;
            }

            double val = EstimatePositionValue(pos);
            double pnl = pos.InitialCredit - val;

            if (pnl >= pos.InitialCredit * SC.ExitProfitTargetPct)
            {
                ClosePosition($"ProfitTarget pnl={pnl:F2}");
                return;
            }
            if (val >= pos.InitialCredit * SC.ExitStopLossCreditMultiple)
            {
                ClosePosition($"StopLoss val={val:F2}");
                return;
            }
        }

        private void ClosePosition(string reason)
        {
            var pos = _openPosition;
            var legs = new List<Leg>
            {
                Leg.Create(pos.ShortCall, +1),
                Leg.Create(pos.LongCall,  -1),
                Leg.Create(pos.ShortPut,  +1),
                Leg.Create(pos.LongPut,   -1),
            };

            ComboMarketOrder(legs, SC.SizingDefaultContracts, asynchronous: false);
            // IBKR commission: $0.65/contract × 4 legs × N contracts per side
            // Divide by 100 (option multiplier) to match per-share P&L units
            _totalFriction += 0.65 * 4 * SC.SizingDefaultContracts / 100.0;

            // Scale P&L by contract count so it's in the same units as _totalSlippage
            double pnl = (pos.InitialCredit - EstimatePositionValue(pos)) * SC.SizingDefaultContracts;
            if (pnl > 0) { _grossProfit += pnl; _winningTrades++; }
            else           _grossLoss   -= pnl;

            _totalTrades++;
            Log($"EXIT [{reason}] trade#{_totalTrades} exp={pos.Expiry:yyyy-MM-dd}");
            _openPosition = null;
        }

        public override void OnEndOfAlgorithm()
        {
            double final   = (double)Portfolio.TotalPortfolioValue;
            double initial = SC.InitialCapitalUsd;

            double years   = (EndDate - StartDate).TotalDays / 365.25;
            double cagr    = years > 0 ? Math.Pow(final / initial, 1.0 / years) - 1.0 : 0;

            double isYears = (_isEnd - StartDate).TotalDays / 365.25;
            double isCagr  = isYears > 0 && _portfolioAtIsEnd > 0
                ? Math.Pow(_portfolioAtIsEnd / initial, 1.0 / isYears) - 1.0 : 0;

            double oosYrs  = (EndDate - DateTime.Parse(SC.OosStart)).TotalDays / 365.25;
            double oosCagr = oosYrs > 0 && _oosStartEquity > 0
                ? Math.Pow(final / _oosStartEquity, 1.0 / oosYrs) - 1.0 : 0;

            double winRate      = _totalTrades > 0 ? (double)_winningTrades / _totalTrades : 0;
            double profitFactor = _grossLoss > 0 ? _grossProfit / _grossLoss : (_grossProfit > 0 ? 99 : 0);
            double slipDrag     = _grossProfit > 0 ? _totalFriction / _grossProfit : 0;

            Log("=== Phase 1 Backtest Summary ===");
            Log($"parameter_hash:     {SC.ParameterHash}");
            Log($"date_range_start:   {SC.DateRangeStart}");
            Log($"date_range_end:     {SC.DateRangeEnd}");
            Log($"cagr_net:           {cagr:P2}");
            Log($"in_sample_cagr:     {isCagr:P2}");
            Log($"oos_cagr:           {oosCagr:P2}");
            Log($"max_drawdown:       {_maxDrawdown:P2}");
            Log($"win_rate:           {winRate:P2}");
            Log($"profit_factor:      {profitFactor:F2}");
            Log($"slippage_drag_pct:  {slipDrag:P2}");
            Log($"total_trades:       {_totalTrades}");
            Log($"total_fees:         {Portfolio.TotalFees:C2}");
            Log($"final_equity:       {final:C2}");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private DateTime? SelectExpiry(OptionChain chain)
        {
            var today  = Time.Date;
            var target = today.AddDays(SC.EntryDteTarget);
            return chain
                .Select(c => c.Expiry.Date)
                .Where(e => { int d = (e - today).Days; return d >= SC.EntryDteMin && d <= SC.EntryDteMax; })
                .Distinct()
                .OrderBy(e => Math.Abs((e - target).TotalDays))
                .Cast<DateTime?>()
                .FirstOrDefault();
        }

        private OptionContract SelectByDelta(List<OptionContract> contracts, double target, double tolerance)
        {
            // Try tight tolerance first, then widen to 0.10 as fallback
            var result = contracts
                .Where(c => Math.Abs(Math.Abs((double)c.Greeks.Delta) - target) <= tolerance)
                .OrderBy(c => Math.Abs(Math.Abs((double)c.Greeks.Delta) - target))
                .FirstOrDefault();
            if (result != null) return result;
            return contracts
                .Where(c => Math.Abs(Math.Abs((double)c.Greeks.Delta) - target) <= 0.10)
                .OrderBy(c => Math.Abs(Math.Abs((double)c.Greeks.Delta) - target))
                .FirstOrDefault();
        }

        private double EstimatePositionValue(IronCondorPosition pos)
        {
            if (!Securities.ContainsKey(pos.ShortCall) || !Securities.ContainsKey(pos.LongCall) ||
                !Securities.ContainsKey(pos.ShortPut)  || !Securities.ContainsKey(pos.LongPut))
                return pos.InitialCredit;

            double sc = MidSec(Securities[pos.ShortCall]);
            double lc = MidSec(Securities[pos.LongCall]);
            double sp = MidSec(Securities[pos.ShortPut]);
            double lp = MidSec(Securities[pos.LongPut]);
            return sc - lc + sp - lp;
        }

        private double Mid(OptionContract c)
        {
            if (c.BidPrice > 0 && c.AskPrice > 0)
                return (double)(c.BidPrice + c.AskPrice) / 2.0;
            return (double)c.LastPrice;  // fallback when bid/ask not populated
        }
        private double MidSec(Security s)
        {
            if (s.BidPrice > 0 && s.AskPrice > 0)
                return (double)(s.BidPrice + s.AskPrice) / 2.0;
            return (double)s.Price;
        }

        private void TrackDrawdown()
        {
            double eq = (double)Portfolio.TotalPortfolioValue;
            if (eq > _peakPortfolio) _peakPortfolio = eq;
            if (_peakPortfolio > 0)
            {
                double dd = (_peakPortfolio - eq) / _peakPortfolio;
                if (dd > _maxDrawdown) _maxDrawdown = dd;
            }
        }
    }

    internal class IronCondorPosition
    {
        public DateTime Expiry        { get; set; }
        public double   InitialCredit { get; set; }
        public Symbol   ShortCall     { get; set; }
        public Symbol   LongCall      { get; set; }
        public Symbol   ShortPut      { get; set; }
        public Symbol   LongPut       { get; set; }
    }
}
