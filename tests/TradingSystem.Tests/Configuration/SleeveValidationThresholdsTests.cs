using TradingSystem.Core.Configuration;
using TradingSystem.Storage.Repositories;
using Xunit;

namespace TradingSystem.Tests.Configuration;

public class SleeveValidationThresholdsTests
{
    // Builds metrics that comfortably pass every ADR-010 default threshold.
    private static SleeveMetrics PassingMetrics() => new()
    {
        HitRatePercent = 60m,
        ProfitFactor = 1.8m,
        MaxDrawdownPercent = 8m,
        WeeksObserved = 16,
        TotalReturnPercent = 5m,
        SpyReturnPercent = 3m
    };

    // --- Test 1: Defaults() maps exactly to ADR-010 ---

    [Fact]
    public void Defaults_MapToAdr010Values_ForBothSleeves()
    {
        var thresholds = SleeveValidationThresholds.Defaults();

        foreach (var sleeve in new[] { thresholds.Income, thresholds.Options })
        {
            Assert.Equal(45m, sleeve.MinHitRatePercent);
            Assert.Equal(1.3m, sleeve.MinProfitFactor);
            Assert.Equal(15m, sleeve.MaxDrawdownPercent);
            Assert.Equal(12, sleeve.MinWeeksObserved);
            Assert.True(sleeve.RequireProfitableOrBeatSpy);
        }
    }

    // --- Test 2: all metrics meet-or-exceed => PASS ---

    [Fact]
    public void Evaluate_AllMetricsMeetThresholds_Passes()
    {
        var sleeve = SleeveValidationThresholds.Defaults().Income;

        var result = sleeve.Evaluate(PassingMetrics());

        Assert.Equal(ValidationOutcome.Pass, result.Outcome);
        Assert.True(result.HitRatePass);
        Assert.True(result.ProfitFactorPass);
        Assert.True(result.DrawdownPass);
        Assert.True(result.ProfitableOrBeatSpyPass);
    }

    [Fact]
    public void Evaluate_MetricsExactlyAtThresholds_Pass()
    {
        // Meet-or-exceed semantics: hit rate and profit factor at the floor,
        // drawdown at the ceiling, weeks at the minimum all count as PASS.
        var sleeve = SleeveValidationThresholds.Defaults().Options;
        var metrics = PassingMetrics();
        metrics.HitRatePercent = 45m;
        metrics.ProfitFactor = 1.3m;
        metrics.MaxDrawdownPercent = 15m;
        metrics.WeeksObserved = 12;

        var result = sleeve.Evaluate(metrics);

        Assert.Equal(ValidationOutcome.Pass, result.Outcome);
    }

    // --- Test 3: single failing metric flagged, overall FAIL ---

    [Fact]
    public void Evaluate_HitRateBelowThreshold_FailsAndFlagsHitRate()
    {
        var sleeve = SleeveValidationThresholds.Defaults().Income;
        var metrics = PassingMetrics();
        metrics.HitRatePercent = 40m;

        var result = sleeve.Evaluate(metrics);

        Assert.Equal(ValidationOutcome.Fail, result.Outcome);
        Assert.False(result.HitRatePass);
        Assert.True(result.ProfitFactorPass);
        Assert.True(result.DrawdownPass);
        Assert.True(result.ProfitableOrBeatSpyPass);
    }

    [Fact]
    public void Evaluate_DrawdownAboveThreshold_FailsAndFlagsDrawdown()
    {
        var sleeve = SleeveValidationThresholds.Defaults().Income;
        var metrics = PassingMetrics();
        metrics.MaxDrawdownPercent = 15.1m;

        var result = sleeve.Evaluate(metrics);

        Assert.Equal(ValidationOutcome.Fail, result.Outcome);
        Assert.False(result.DrawdownPass);
    }

    // --- Test 4: RequireProfitableOrBeatSpy semantics ---

    [Fact]
    public void Evaluate_NotProfitableAndNotBeatingSpy_Fails()
    {
        var sleeve = SleeveValidationThresholds.Defaults().Income;
        var metrics = PassingMetrics();
        metrics.TotalReturnPercent = -2m;
        metrics.SpyReturnPercent = -1m; // sleeve underperforms SPY and loses money

        var result = sleeve.Evaluate(metrics);

        Assert.Equal(ValidationOutcome.Fail, result.Outcome);
        Assert.False(result.ProfitableOrBeatSpyPass);
    }

    [Fact]
    public void Evaluate_NotProfitableButBeatingSpy_Passes()
    {
        var sleeve = SleeveValidationThresholds.Defaults().Income;
        var metrics = PassingMetrics();
        metrics.TotalReturnPercent = -1m;
        metrics.SpyReturnPercent = -3m; // losing less than SPY = beating SPY (strict >)

        var result = sleeve.Evaluate(metrics);

        Assert.Equal(ValidationOutcome.Pass, result.Outcome);
        Assert.True(result.ProfitableOrBeatSpyPass);
    }

    [Fact]
    public void Evaluate_NotProfitableAndEqualToSpy_Fails_StrictGreaterThan()
    {
        // Owner-confirmed: beat-SPY is STRICT greater-than — matching SPY is not beating it.
        var sleeve = SleeveValidationThresholds.Defaults().Income;
        var metrics = PassingMetrics();
        metrics.TotalReturnPercent = -2m;
        metrics.SpyReturnPercent = -2m;

        var result = sleeve.Evaluate(metrics);

        Assert.Equal(ValidationOutcome.Fail, result.Outcome);
        Assert.False(result.ProfitableOrBeatSpyPass);
    }

    [Fact]
    public void Evaluate_RequireProfitableOrBeatSpyDisabled_IgnoresProfitability()
    {
        var sleeve = SleeveValidationThresholds.Defaults().Income;
        sleeve.RequireProfitableOrBeatSpy = false;
        var metrics = PassingMetrics();
        metrics.TotalReturnPercent = -5m;
        metrics.SpyReturnPercent = -1m; // would fail the check if it were enabled

        var result = sleeve.Evaluate(metrics);

        Assert.Equal(ValidationOutcome.Pass, result.Outcome);
        Assert.True(result.ProfitableOrBeatSpyPass);
    }

    // --- Test 5: settings-seam round trip (serialization fidelity) ---

    [Fact]
    public async Task SetSetting_ThenGetSetting_RoundTripsThresholds()
    {
        var testDir = Path.Combine(Path.GetTempPath(), $"ts-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);
        try
        {
            var repo = new JsonConfigRepository(testDir);
            var original = SleeveValidationThresholds.Defaults();
            original.Options.MinProfitFactor = 1.5m; // non-default value must survive the trip
            original.Income.RequireProfitableOrBeatSpy = false;

            await repo.SetSettingAsync(SleeveValidationThresholds.SettingsKey, original);
            var restored = await repo.GetSettingAsync<SleeveValidationThresholds>(
                SleeveValidationThresholds.SettingsKey);

            Assert.NotNull(restored);
            AssertSleeveEqual(original.Income, restored!.Income);
            AssertSleeveEqual(original.Options, restored.Options);
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    private static void AssertSleeveEqual(SleeveThresholds expected, SleeveThresholds actual)
    {
        Assert.Equal(expected.MinHitRatePercent, actual.MinHitRatePercent);
        Assert.Equal(expected.MinProfitFactor, actual.MinProfitFactor);
        Assert.Equal(expected.MaxDrawdownPercent, actual.MaxDrawdownPercent);
        Assert.Equal(expected.MinWeeksObserved, actual.MinWeeksObserved);
        Assert.Equal(expected.RequireProfitableOrBeatSpy, actual.RequireProfitableOrBeatSpy);
    }

    // --- Test 6: insufficient observation window is NOT a FAIL ---

    [Fact]
    public void Evaluate_WeeksObservedBelowMinimum_ReturnsInsufficientData()
    {
        var sleeve = SleeveValidationThresholds.Defaults().Income;
        var metrics = PassingMetrics();
        metrics.WeeksObserved = 11; // one short of the 12-week minimum

        var result = sleeve.Evaluate(metrics);

        Assert.Equal(ValidationOutcome.InsufficientData, result.Outcome);
        Assert.NotEqual(ValidationOutcome.Fail, result.Outcome);
        Assert.NotEqual(ValidationOutcome.Pass, result.Outcome);
    }

    [Fact]
    public void Evaluate_InsufficientData_EvenWhenMetricsWouldFail()
    {
        // Too-early metrics must not produce a premature FAIL verdict.
        var sleeve = SleeveValidationThresholds.Defaults().Income;
        var metrics = PassingMetrics();
        metrics.WeeksObserved = 3;
        metrics.HitRatePercent = 10m;

        var result = sleeve.Evaluate(metrics);

        Assert.Equal(ValidationOutcome.InsufficientData, result.Outcome);
    }
}
