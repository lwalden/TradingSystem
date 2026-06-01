// StrategyConstants.cs — AUTO-GENERATED. Do not edit manually.
// Source: optimind/config/strategies.yaml
// parameter_hash: f32c6bb59a1432156890dca414c7bafc5fcd637cc6d8cd04a37351c500ebccbd
//
// Regenerate with: uv run python scripts/generate_lean_config.py

namespace OptiMind.Backtests
{
    public static class StrategyConstants
    {
        public const int EntryDteMin = 30;
        public const int EntryDteTarget = 45;
        public const int EntryDteMax = 60;
        public const double EntryShortDeltaTarget = 0.16d;
        public const double EntryShortDeltaTolerance = 0.04d;
        public const int EntryWingWidthSpx = 50;
        public const int EntryWingWidthSpy = 10;
        public const int EntryWingWidthQqq = 5;
        public const int EntryWingWidthIwm = 5;
        public const double EntryMinCreditToWidthRatio = 0.2d;
        public const int EntryIvrMin = 25;
        public const int EntryIvrMax = 75;
        public const double EntryEntryMinAtmIv = 0.18d;
        public const double EntryMaxLegSpreadPct = 0.1d;
        public const double ExitProfitTargetPct = 0.7d;
        public const double ExitStopLossCreditMultiple = 2.0d;
        // ExitDteManagement: complex schedule — see strategies.yaml
        public const int ExitDteManagement_0_Dte = 21;
        public const string ExitDteManagement_0_Action = "evaluate";
        public const int ExitDteManagement_1_Dte = 14;
        public const string ExitDteManagement_1_Action = "evaluate_or_close";
        public const int ExitDteManagement_2_Dte = 7;
        public const string ExitDteManagement_2_Action = "close";
        public const int SizingDefaultContracts = 5;
        public const int SizingMaxContracts = 5;
        public const string SmartPricingEntryStart = "mid";
        public const double SmartPricingEntryStepPct = 0.1d;
        public const int SmartPricingEntryMaxSteps = 5;
        public const int SmartPricingEntryStepIntervalSeconds = 30;
        public const string SmartPricingExitStart = "mid";
        public const double SmartPricingExitStepPct = 0.1d;
        public const int SmartPricingExitMaxSteps = 5;
        public const int SmartPricingExitStepIntervalSeconds = 30;
        public const string BacktestDateRangeStart = "2019-01-01";
        public const string BacktestDateRangeEnd = "2025-12-31";
        public const string BacktestInSampleEnd = "2022-12-31";
        public const string BacktestOosStart = "2023-01-01";
        public const double BacktestSlippagePerLegUsd = 0.05d;
        public const double BacktestCommissionPerContractUsd = 0.65d;
        public const int BacktestInitialCapitalUsd = 400000;

        public const string ParameterHash = "f32c6bb59a1432156890dca414c7bafc5fcd637cc6d8cd04a37351c500ebccbd";
    }
}
