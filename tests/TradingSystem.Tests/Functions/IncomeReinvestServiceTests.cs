using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Functions;
using TradingSystem.Strategies.Income;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace TradingSystem.Tests.Functions;

/// <summary>
/// S6-001 Rigorous-tier tests for the monthly reinvest pipeline. THE primary sprint
/// invariant lives here (locked decision 1): with IncomeSleeve:OrderPlacementEnabled at its
/// default (false), the run computes a ReinvestmentPlan and sends a report but NEVER touches
/// IExecutionService — zero orders, ever. Per the spec these tests use the REAL
/// IncomeSleeveManager (caps enforced at plan time — 10% issuer / 40% category, post-buy
/// semantics) with mocked IBrokerService / IExecutionService and seeded positions, so the
/// cap and no-order invariants are proven end-to-end through the actual plan generator.
/// </summary>
public class IncomeReinvestServiceTests
{
    // 2026-07-01 is a Wednesday — the first trading weekday of July 2026 (the real first
    // firing this item must protect).
    private static readonly DateTime FirstWeekdayJul2026 = new(2026, 7, 1, 13, 30, 0, DateTimeKind.Utc);

    // ---------- harness ----------

    private sealed class Fixture
    {
        public Mock<IBrokerService> Broker { get; } = new();
        public Mock<IExecutionService> Execution { get; } = new();
        public Mock<IIncomeReportService> Report { get; } = new();
        public Mock<IOperationalAlertService> Alerts { get; } = new();
        public Mock<ILogger<IncomeReinvestService>> Logger { get; } = new();
        public ReinvestmentPlan? CapturedPlan { get; set; }
        public IncomeSleeveState? CapturedState { get; set; }
        public int? CapturedOrdersPlaced { get; set; }
        public IncomeReinvestService Service { get; set; } = null!;
    }

    private static Fixture Build(
        bool orderPlacementEnabled = false,
        List<Position>? positions = null,
        decimal totalCashValue = 100_000m,
        bool connectSucceeds = true,
        IncomeConfig? incomeConfig = null,
        bool includeReportService = true,
        bool includeAlertService = true,
        Exception? reportThrows = null,
        Exception? accountThrows = null)
    {
        var fixture = new Fixture();

        fixture.Broker.Setup(b => b.ConnectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(connectSucceeds);
        fixture.Broker.Setup(b => b.DisconnectAsync()).Returns(Task.CompletedTask);
        fixture.Broker.Setup(b => b.GetPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions ?? new List<Position>());

        var accountSetup = fixture.Broker.Setup(b => b.GetAccountAsync(It.IsAny<CancellationToken>()));
        if (accountThrows != null)
        {
            accountSetup.ThrowsAsync(accountThrows);
        }
        else
        {
            accountSetup.ReturnsAsync(new Account
            {
                TotalCashValue = totalCashValue,
                CashBufferValue = 0m
            });
        }

        fixture.Broker.Setup(b => b.GetQuotesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> symbols, CancellationToken _) =>
                symbols.Select(s => new Quote { Symbol = s, Bid = 49.90m, Ask = 50m, Last = 50m }).ToList());

        fixture.Execution.Setup(e => e.ExecuteSignalAsync(It.IsAny<Signal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Signal signal, CancellationToken _) => new ExecutionResult
            {
                Success = true,
                SignalId = signal.Id,
                Orders = new List<Order> { new() { Symbol = signal.Symbol } }
            });

        var reportSetup = fixture.Report.Setup(r => r.SendReinvestmentPlanReportAsync(
            It.IsAny<ReinvestmentPlan>(), It.IsAny<IncomeSleeveState>(), It.IsAny<int>(), It.IsAny<CancellationToken>()));
        if (reportThrows != null)
        {
            reportSetup.ThrowsAsync(reportThrows);
        }
        else
        {
            reportSetup
                .Callback<ReinvestmentPlan, IncomeSleeveState, int, CancellationToken>((plan, state, ordersPlaced, _) =>
                {
                    fixture.CapturedPlan = plan;
                    fixture.CapturedState = state;
                    fixture.CapturedOrdersPlaced = ordersPlaced;
                })
                .Returns(Task.CompletedTask);
        }

        // REAL plan generator (spec: reuse, never duplicate, the 10%/40% cap logic).
        var manager = new IncomeSleeveManager(
            fixture.Broker.Object,
            fixture.Execution.Object,
            new IncomeUniverse(),
            incomeConfig ?? new IncomeConfig(),
            new ExecutionConfig(),
            new Mock<ILogger<IncomeSleeveManager>>().Object);

        fixture.Service = new IncomeReinvestService(
            fixture.Broker.Object,
            manager,
            MsOptions.Create(new TradingSystemConfig()),
            MsOptions.Create(new IncomeSleeveConfig { OrderPlacementEnabled = orderPlacementEnabled }),
            fixture.Logger.Object,
            includeReportService ? fixture.Report.Object : null,
            includeAlertService ? fixture.Alerts.Object : null);

        return fixture;
    }

    /// <summary>
    /// Underweight seed: a single 50k DividendGrowthETF position makes every other category
    /// underweight. With default caps/targets the drift math yields: CoveredCallETF and BDC
    /// (drift -20% → $10k buys) are SKIPPED by the post-buy issuer cap
    /// (10k / 60k = 16.7% &gt; 10%), while EquityREIT / MortgageREIT / Preferreds
    /// (drift -10% → $5k buys, 5k / 55k = 9.09% ≤ 10%) survive → 3 proposed buys.
    /// </summary>
    private static List<Position> UnderweightSleeve() => new()
    {
        new Position { Symbol = "VIG", Quantity = 1000m, MarketPrice = 50m, AverageCost = 45m }
    };

    private static List<(LogLevel Level, string Message)> CapturedLogs(Mock<ILogger<IncomeReinvestService>> logger)
    {
        var entries = new List<(LogLevel, string)>();
        foreach (var inv in logger.Invocations)
        {
            if (inv.Method.Name != nameof(ILogger.Log))
                continue;
            entries.Add(((LogLevel)inv.Arguments[0], inv.Arguments[2]?.ToString() ?? string.Empty));
        }
        return entries;
    }

    // ---------- 1. THE invariant: no orders by default (locked decision 1) ----------

    [Fact]
    public async Task FlagOff_Default_PlanProposed_ButExecutionServiceNeverCalled()
    {
        var fixture = Build(orderPlacementEnabled: false, positions: UnderweightSleeve());

        var result = await fixture.Service.RunAsync("run00001", FirstWeekdayJul2026, CancellationToken.None);

        Assert.False(result.Skipped);
        Assert.True(result.BrokerConnected);
        Assert.True(result.PlanGenerated);
        Assert.True(result.ProposedBuyCount >= 1);

        // THE sprint invariant: flag off ⇒ IExecutionService is NEVER called.
        fixture.Execution.Verify(
            e => e.ExecuteSignalAsync(It.IsAny<Signal>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Equal(0, result.OrdersPlaced);

        // Posture is logged explicitly so the run record states why no orders exist.
        Assert.Contains(CapturedLogs(fixture.Logger),
            e => e.Level == LogLevel.Information &&
                 e.Message.Contains("recommendation-only") &&
                 e.Message.Contains("IncomeSleeve:OrderPlacementEnabled=false"));
    }

    // ---------- 2. Flag on: existing execution path engages ----------

    [Fact]
    public async Task FlagOn_ExecutesOncePerProposedBuy_AndOrdersPlacedMatches()
    {
        var fixture = Build(orderPlacementEnabled: true, positions: UnderweightSleeve());

        var result = await fixture.Service.RunAsync("run00002", FirstWeekdayJul2026, CancellationToken.None);

        // Default seed yields 3 surviving buys (see UnderweightSleeve doc comment).
        Assert.Equal(3, result.ProposedBuyCount);
        fixture.Execution.Verify(
            e => e.ExecuteSignalAsync(It.IsAny<Signal>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        Assert.Equal(3, result.OrdersPlaced);

        // The report reflects the orders-placed posture.
        Assert.Equal(3, fixture.CapturedOrdersPlaced);
    }

    // ---------- 3. Flag default regression tripwire (locked decision 1) ----------

    [Fact]
    public void IncomeSleeveConfig_CodeDefault_IsOrderPlacementDisabled()
    {
        Assert.False(new IncomeSleeveConfig().OrderPlacementEnabled);
    }

    [Fact]
    public void IncomeSleeveConfig_UnboundEmptySection_BindsToOrderPlacementDisabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var bound = new IncomeSleeveConfig();
        configuration.GetSection("IncomeSleeve").Bind(bound);

        Assert.False(bound.OrderPlacementEnabled);
    }

    // ---------- 4. First-trading-weekday gate (Default D3) ----------

    [Fact]
    public async Task Gate_FirstWeekdayOfMonth_Runs()
    {
        var fixture = Build(positions: UnderweightSleeve());

        var result = await fixture.Service.RunAsync("run00003", FirstWeekdayJul2026, CancellationToken.None);

        Assert.False(result.Skipped);
        fixture.Broker.Verify(b => b.ConnectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Gate_SecondWeekdayOfMonth_SkipsWithZeroBrokerCalls()
    {
        var fixture = Build(positions: UnderweightSleeve());

        // 2026-07-02 (Thursday) — still inside the cron's day 1-7 window, but not the gate day.
        var result = await fixture.Service.RunAsync(
            "run00004", new DateTime(2026, 7, 2, 13, 30, 0, DateTimeKind.Utc), CancellationToken.None);

        Assert.True(result.Skipped);
        Assert.False(string.IsNullOrWhiteSpace(result.SkipReason));
        fixture.Broker.Verify(b => b.ConnectAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.Report.Verify(r => r.SendReinvestmentPlanReportAsync(
                It.IsAny<ReinvestmentPlan>(), It.IsAny<IncomeSleeveState>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // The skip is an Information log (proof the timer ran), not silence.
        Assert.Contains(CapturedLogs(fixture.Logger), e => e.Level == LogLevel.Information);
    }

    [Fact]
    public async Task Gate_MonthStartingOnSaturday_FirstMondayIsTheGateDay()
    {
        // August 2026 starts on a Saturday → the first trading weekday is Monday 2026-08-03.
        var fixture = Build(positions: UnderweightSleeve());

        var monday = await fixture.Service.RunAsync(
            "run00005", new DateTime(2026, 8, 3, 13, 30, 0, DateTimeKind.Utc), CancellationToken.None);
        Assert.False(monday.Skipped);

        var tuesday = await fixture.Service.RunAsync(
            "run00006", new DateTime(2026, 8, 4, 13, 30, 0, DateTimeKind.Utc), CancellationToken.None);
        Assert.True(tuesday.Skipped);

        fixture.Broker.Verify(b => b.ConnectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------- 5. Broker connect failure (Default D7 — S5-003 reuse) ----------

    [Fact]
    public async Task ConnectFailure_SendsExactlyOneOperationalAlert_NoPlanNoReportNoThrow()
    {
        var fixture = Build(positions: UnderweightSleeve(), connectSucceeds: false);

        var result = await fixture.Service.RunAsync("run00007", FirstWeekdayJul2026, CancellationToken.None);

        Assert.False(result.BrokerConnected);
        Assert.False(result.PlanGenerated);
        Assert.False(result.ReportSent);

        fixture.Alerts.Verify(a => a.SendOperationalAlertAsync(
                "Broker Connect Failure — Monthly Reinvest",
                It.Is<string>(d => d.Contains("run00007")),
                It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Report.Verify(r => r.SendReinvestmentPlanReportAsync(
                It.IsAny<ReinvestmentPlan>(), It.IsAny<IncomeSleeveState>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // No successful connect → no disconnect.
        fixture.Broker.Verify(b => b.DisconnectAsync(), Times.Never);
    }

    [Fact]
    public async Task ConnectFailure_WithoutAlertService_StillDegradesQuietly()
    {
        var fixture = Build(positions: UnderweightSleeve(), connectSucceeds: false, includeAlertService: false);

        var result = await fixture.Service.RunAsync("run00008", FirstWeekdayJul2026, CancellationToken.None);

        Assert.False(result.BrokerConnected);
    }

    [Fact]
    public async Task ConnectFailure_AlertServiceThrows_NeverFailsTheRun()
    {
        var fixture = Build(positions: UnderweightSleeve(), connectSucceeds: false);
        fixture.Alerts.Setup(a => a.SendOperationalAlertAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("alert transport down"));

        var result = await fixture.Service.RunAsync("run00009", FirstWeekdayJul2026, CancellationToken.None);

        Assert.False(result.BrokerConnected);
    }

    // ---------- 6. Caps respected end-to-end through the REAL manager ----------

    [Fact]
    public async Task IssuerCap_PostBuySemantics_SkipsSymbolsThatWouldExceedTenPercent()
    {
        var fixture = Build(positions: UnderweightSleeve());

        await fixture.Service.RunAsync("run00010", FirstWeekdayJul2026, CancellationToken.None);

        var plan = fixture.CapturedPlan;
        Assert.NotNull(plan);

        // CoveredCallETF and BDC ($10k buys → 10k/60k = 16.7% post-buy) violate the 10%
        // issuer cap and must be absent; the $5k buys (9.09% post-buy) survive.
        Assert.DoesNotContain(plan!.ProposedBuys, b => b.Category == IncomeCategory.CoveredCallETF);
        Assert.DoesNotContain(plan.ProposedBuys, b => b.Category == IncomeCategory.BDC);
        Assert.Equal(3, plan.ProposedBuys.Count);
        Assert.All(plan.ProposedBuys, b => Assert.Equal(5_000m, b.Amount));
    }

    [Fact]
    public async Task CategoryCap_PostBuySemantics_SkipsCategoryThatWouldExceedFortyPercent()
    {
        // Test fixture isolates the CATEGORY cap: issuer cap is opened to 100% so the
        // category check is the deciding one. (Fixture config only — production caps are
        // untouched by this item.)
        var incomeConfig = new IncomeConfig
        {
            MaxIssuerPercent = 1.0m,
            AllocationTargets = new Dictionary<string, decimal>
            {
                // Drift -75% → $37.5k buy → 37.5k/87.5k = 42.9% post-buy > 40% → skipped.
                { "CoveredCallETF", 0.75m },
                // Drift -10% → $5k buy → 5k/55k = 9.09% post-buy ≤ 40% → survives.
                { "EquityREIT", 0.10m }
            }
        };
        var fixture = Build(positions: UnderweightSleeve(), incomeConfig: incomeConfig);

        await fixture.Service.RunAsync("run00011", FirstWeekdayJul2026, CancellationToken.None);

        var plan = fixture.CapturedPlan;
        Assert.NotNull(plan);
        Assert.DoesNotContain(plan!.ProposedBuys, b => b.Category == IncomeCategory.CoveredCallETF);
        var only = Assert.Single(plan.ProposedBuys);
        Assert.Equal(IncomeCategory.EquityREIT, only.Category);
    }

    // ---------- 7. Empty sleeve honesty (Default D8) ----------

    [Fact]
    public async Task EmptySleeve_YieldsEmptyPlan_AndReportIsStillSent()
    {
        var fixture = Build(positions: new List<Position>());

        var result = await fixture.Service.RunAsync("run00012", FirstWeekdayJul2026, CancellationToken.None);

        Assert.True(result.PlanGenerated);
        Assert.Equal(0, result.ProposedBuyCount);
        Assert.Equal(0m, result.TotalProposedAmount);
        Assert.Equal(0, result.OrdersPlaced);

        // Proof the timer ran: the report still goes out saying no buys are proposed.
        Assert.True(result.ReportSent);
        Assert.NotNull(fixture.CapturedPlan);
        Assert.Empty(fixture.CapturedPlan!.ProposedBuys);
    }

    // ---------- 8. Report failure can never fail the run ----------

    [Fact]
    public async Task ReportFailure_RunCompletes_WithWarning_AndOrdersOutcomeUnaffected()
    {
        var fixture = Build(
            positions: UnderweightSleeve(),
            reportThrows: new HttpRequestException("simulated webhook outage"));

        var result = await fixture.Service.RunAsync("run00013", FirstWeekdayJul2026, CancellationToken.None);

        Assert.True(result.PlanGenerated);
        Assert.False(result.ReportSent);
        Assert.NotEmpty(result.Warnings);
        Assert.Equal(0, result.OrdersPlaced);

        // Warning content carries the exception TYPE NAME only — never the message.
        Assert.Contains(result.Warnings, w => w.Contains(nameof(HttpRequestException)));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("simulated webhook outage"));
    }

    [Fact]
    public async Task NoReportServiceRegistered_RunCompletes_ReportSentFalse()
    {
        var fixture = Build(positions: UnderweightSleeve(), includeReportService: false);

        var result = await fixture.Service.RunAsync("run00014", FirstWeekdayJul2026, CancellationToken.None);

        Assert.True(result.PlanGenerated);
        Assert.False(result.ReportSent);
    }

    // ---------- 9. D4: recommendation-cash input ----------

    [Fact]
    public async Task AvailableCash_IsTotalCashValueTimesIncomeTargetPercent_AndDividendFieldsStayZero()
    {
        var fixture = Build(positions: UnderweightSleeve(), totalCashValue: 100_000m);

        await fixture.Service.RunAsync("run00015", FirstWeekdayJul2026, CancellationToken.None);

        Assert.NotNull(fixture.CapturedPlan);
        // 100,000 × 0.70 (default IncomeTargetPercent) = 70,000 — recorded in the plan so the
        // owner sees the input (Default D4).
        Assert.Equal(70_000m, fixture.CapturedPlan!.AvailableCash);
        // No dividend-activity data source yet — fields stay default-valued, never guessed.
        Assert.Equal(0m, fixture.CapturedPlan.DividendsReceived);
        Assert.Equal(0m, fixture.CapturedPlan.InterestReceived);
    }

    // ---------- 10. Disconnect finally-semantics ----------

    [Fact]
    public async Task Disconnect_CalledExactlyOnce_AfterSuccessfulRun()
    {
        var fixture = Build(positions: UnderweightSleeve());

        await fixture.Service.RunAsync("run00016", FirstWeekdayJul2026, CancellationToken.None);

        fixture.Broker.Verify(b => b.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task Disconnect_CalledExactlyOnce_EvenWhenPlanGenerationThrows()
    {
        var fixture = Build(
            positions: UnderweightSleeve(),
            accountThrows: new InvalidOperationException("account fetch failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RunAsync("run00017", FirstWeekdayJul2026, CancellationToken.None));

        fixture.Broker.Verify(b => b.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task Disconnect_CalledExactlyOnce_WhenReportThrows()
    {
        var fixture = Build(
            positions: UnderweightSleeve(),
            reportThrows: new InvalidOperationException("report renderer bug"));

        await fixture.Service.RunAsync("run00018", FirstWeekdayJul2026, CancellationToken.None);

        fixture.Broker.Verify(b => b.DisconnectAsync(), Times.Once);
    }
}
