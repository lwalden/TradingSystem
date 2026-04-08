// SpxIronCondor.cs — SPX Iron Condor backtest for QuantConnect cloud
//
// Fixes over v1/v2:
//   1. Security initializer only applies to Option securities (fixes model warning)
//   2. P&L tracking uses Portfolio equity delta (not stale mid-market estimates)
//   3. EstimatePositionValue clamped: value >= 0, pnl <= credit
//   4. EXIT log shows both estimated and actual P&L for cross-validation
//   5. OnOrderEvent logs actual fill prices for diagnostics

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
    // ── SPX Iron Condor v3 — fixed P&L tracking ──
    public static class SC
    {
        public const int    EntryDteMin                = 30;
        public const int    EntryDteTarget             = 45;
        public const int    EntryDteMax                = 60;
        public const double EntryShortDeltaTarget      = 0.16d;
        public const double EntryShortDeltaTolerance   = 0.04d;
        public const int    EntryWingWidth             = 50;
        public const double EntryMinCreditToWidthRatio = 0.15d;
        public const double EntryMinAtmIv              = 0.18d;
        public const double ExitProfitTargetPct        = 0.7d;
        public const double ExitStopLossCreditMultiple = 2.0d;
        public const int    ExitDteMandatoryClose      = 7;
        public const int    SizingDefaultContracts     = 1;
        public const int    InitialCapitalUsd          = 400000;
        public const string DateRangeStart             = "2019-01-01";
        public const string DateRangeEnd               = "2025-12-31";
        public const string InSampleEnd                = "2022-12-31";
        public const string OosStart                   = "2023-01-01";
        public const string ParameterHash              = "spx-iron-condor-v3";
    }

    public class SpxIronCondorAlgorithm : QCAlgorithm
    {
        private Symbol _spxOption;
        private IronCondorPosition _openPosition = null;

        // P&L tracking using actual portfolio equity (not mid-market estimates)
        private int    _totalTrades   = 0;
        private int    _winningTrades = 0;
        private double _grossProfit   = 0;
        private double _grossLoss     = 0;
        private double _peakPortfolio = 0;
        private double _maxDrawdown   = 0;

        // Equity snapshots for actual P&L measurement
        private double _equityBeforeClose = 0;

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

            AddIndex("SPX", Resolution.Minute);

            var option = AddIndexOption("SPX", Resolution.Minute);
            option.SetFilter(u => u
                .Expiration(TimeSpan.FromDays(SC.EntryDteMin), TimeSpan.FromDays(SC.EntryDteMax))
                .Strikes(-60, 60));
            _spxOption = option.Symbol;

            // FIX: Only apply fee/slippage models to Option securities (not the index itself).
            // This resolves the "different types of models" warning.
            SetSecurityInitializer(s => {
                if (s.Type == SecurityType.IndexOption)
                {
                    s.SetFeeModel(new InteractiveBrokersFeeModel());
                    s.SetSlippageModel(new ConstantSlippageModel(0.005m));
                }
            });

            Log($"parameter_hash: {SC.ParameterHash}");
            Log($"DTE range: {SC.EntryDteMin}-{SC.EntryDteMax} (target {SC.EntryDteTarget})");
            Log($"SPX Iron Condor v3 | Wing: {SC.EntryWingWidth}pts | PT: {SC.ExitProfitTargetPct:P0} | SL: {SC.ExitStopLossCreditMultiple}x | Contracts: {SC.SizingDefaultContracts}");
        }

        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            if (orderEvent.Status == OrderStatus.Filled)
            {
                Log($"FILL: {orderEvent.Symbol.Value} qty={orderEvent.FillQuantity} price={orderEvent.FillPrice:F2} fees={orderEvent.OrderFee.Value.Amount:F2}");
            }
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

            if (Time.DayOfWeek == DayOfWeek.Saturday || Time.DayOfWeek == DayOfWeek.Sunday)
                return;
            if (Time.Date <= _lastScanDate)
                return;

            _lastScanDate = Time.Date;

            if (!data.OptionChains.ContainsKey(_spxOption))
            {
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
            if (contracts.Count < 4) { Log($"SKIP {Time.Date:yyyy-MM-dd}: only {contracts.Count} contracts"); return; }

            var calls = contracts.Where(c => c.Right == OptionRight.Call).OrderBy(c => c.Strike).ToList();
            var puts  = contracts.Where(c => c.Right == OptionRight.Put).OrderBy(c => c.Strike).ToList();

            if (calls.Count > 0 && (Time.Date - _lastDiagDate).TotalDays >= 90)
            {
                var minDelta  = calls.Min(c => Math.Abs((double)c.Greeks.Delta));
                var maxDelta  = calls.Max(c => Math.Abs((double)c.Greeks.Delta));
                var minStrike = calls.Min(c => c.Strike);
                var maxStrike = calls.Max(c => c.Strike);
                Log($"DIAG: spot={spot:F2} contracts={contracts.Count} calls={calls.Count} deltaRange=[{minDelta:F3},{maxDelta:F3}] strikeRange=[{minStrike},{maxStrike}]");
                _lastDiagDate = Time.Date;
            }

            bool deltaAvailable = calls.Any(c => Math.Abs((double)c.Greeks.Delta) > 0.001);

            OptionContract shortCall, shortPut;
            if (deltaAvailable)
            {
                shortCall = SelectByDelta(calls, SC.EntryShortDeltaTarget, SC.EntryShortDeltaTolerance);
                shortPut  = SelectByDelta(puts,  SC.EntryShortDeltaTarget, SC.EntryShortDeltaTolerance);
            }
            else
            {
                double callTarget = spot * 1.07;
                double putTarget  = spot * 0.93;
                shortCall = calls.OrderBy(c => Math.Abs((double)c.Strike - callTarget)).FirstOrDefault();
                shortPut  = puts.OrderBy(c => Math.Abs((double)c.Strike - putTarget)).FirstOrDefault();
                if (shortCall != null && shortPut != null)
                    Log($"DIAG: strike-based selection (no Greeks). call={shortCall.Strike} put={shortPut.Strike}");
            }

            if (shortCall == null) { Log($"SKIP {Time.Date:yyyy-MM-dd}: no short call near delta {SC.EntryShortDeltaTarget}"); return; }
            var longCall = calls
                .Where(c => c.Strike >= shortCall.Strike + SC.EntryWingWidth)
                .OrderBy(c => c.Strike)
                .FirstOrDefault();
            if (longCall == null) { Log($"SKIP {Time.Date:yyyy-MM-dd}: no long call >= {shortCall.Strike + SC.EntryWingWidth}"); return; }

            if (shortPut == null) { Log($"SKIP {Time.Date:yyyy-MM-dd}: no short put near delta {SC.EntryShortDeltaTarget}"); return; }
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

            // ATM IV filter
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

            // FIX: Clamp — pnl can never exceed credit for an iron condor
            pnl = Math.Min(pnl, pos.InitialCredit);

            if (pnl >= pos.InitialCredit * SC.ExitProfitTargetPct)
            {
                ClosePosition($"ProfitTarget est_pnl={pnl:F2}");
                return;
            }
            if (val >= pos.InitialCredit * SC.ExitStopLossCreditMultiple)
            {
                ClosePosition($"StopLoss est_val={val:F2}");
                return;
            }
        }

        private void ClosePosition(string reason)
        {
            var pos = _openPosition;

            // FIX: Snapshot equity BEFORE the close order to measure actual P&L
            _equityBeforeClose = (double)Portfolio.TotalPortfolioValue;

            var legs = new List<Leg>
            {
                Leg.Create(pos.ShortCall, +1),
                Leg.Create(pos.LongCall,  -1),
                Leg.Create(pos.ShortPut,  +1),
                Leg.Create(pos.LongPut,   -1),
            };

            ComboMarketOrder(legs, SC.SizingDefaultContracts, asynchronous: false);

            // FIX: Use actual portfolio equity delta for P&L (not mid-market estimate)
            double equityAfter = (double)Portfolio.TotalPortfolioValue;
            double actualPnl = equityAfter - _equityBeforeClose;

            // Also compute estimated P&L for diagnostic comparison
            double estVal = EstimatePositionValue(pos);
            double estPnl = Math.Min(pos.InitialCredit - estVal, pos.InitialCredit) * SC.SizingDefaultContracts;

            // Use actual P&L for tracking (this is what really happened)
            if (actualPnl > 0) { _grossProfit += actualPnl; _winningTrades++; }
            else                 _grossLoss   -= actualPnl;

            _totalTrades++;

            // Log both estimated and actual for cross-validation
            Log($"EXIT [{reason}] trade#{_totalTrades} exp={pos.Expiry:yyyy-MM-dd} actual_pnl=${actualPnl:F2} est_pnl=${estPnl:F2} credit={pos.InitialCredit:F2}");

            if (Math.Abs(actualPnl - estPnl) > Math.Abs(estPnl) * 0.5 && Math.Abs(actualPnl) > 50)
                Log($"WARN: Large est/actual divergence on trade#{_totalTrades}: actual=${actualPnl:F2} vs est=${estPnl:F2}");

            _openPosition = null;
        }

        public override void OnEndOfAlgorithm()
        {
            double final_  = (double)Portfolio.TotalPortfolioValue;
            double initial = SC.InitialCapitalUsd;

            double years   = (EndDate - StartDate).TotalDays / 365.25;
            double cagr    = years > 0 ? Math.Pow(final_ / initial, 1.0 / years) - 1.0 : 0;

            double isYears = (_isEnd - StartDate).TotalDays / 365.25;
            double isCagr  = isYears > 0 && _portfolioAtIsEnd > 0
                ? Math.Pow(_portfolioAtIsEnd / initial, 1.0 / isYears) - 1.0 : 0;

            double oosYrs  = (EndDate - DateTime.Parse(SC.OosStart)).TotalDays / 365.25;
            double oosCagr = oosYrs > 0 && _oosStartEquity > 0
                ? Math.Pow(final_ / _oosStartEquity, 1.0 / oosYrs) - 1.0 : 0;

            double winRate      = _totalTrades > 0 ? (double)_winningTrades / _totalTrades : 0;
            double profitFactor = _grossLoss > 0 ? _grossProfit / _grossLoss : (_grossProfit > 0 ? 99 : 0);

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
            Log($"gross_profit:       ${_grossProfit:F2}");
            Log($"gross_loss:         ${_grossLoss:F2}");
            Log($"total_trades:       {_totalTrades}");
            Log($"total_fees:         {Portfolio.TotalFees:C2}");
            Log($"final_equity:       {final_:C2}");
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
            double val = sc - lc + sp - lp;

            // FIX: Clamp — position value is always >= 0 (you always pay to close a short spread)
            // and pnl can never exceed credit (max profit = credit for an iron condor)
            return Math.Max(val, 0.0);
        }

        private double Mid(OptionContract c)
        {
            if (c.BidPrice > 0 && c.AskPrice > 0)
                return (double)(c.BidPrice + c.AskPrice) / 2.0;
            return (double)c.LastPrice;
        }

        private double MidSec(Security s)
        {
            if (s.BidPrice > 0 && s.AskPrice > 0)
                return (double)(s.BidPrice + s.AskPrice) / 2.0;
            // FIX: When bid/ask is zero, use s.Price but floor at 0
            // (stale prices for illiquid options can cause phantom values)
            return Math.Max((double)s.Price, 0.0);
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
