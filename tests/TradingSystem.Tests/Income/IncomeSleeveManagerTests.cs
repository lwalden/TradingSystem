using Microsoft.Extensions.Logging;
using Moq;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;
using TradingSystem.Strategies.Income;
using Xunit;

namespace TradingSystem.Tests.Income;

public class IncomeSleeveManagerTests
{
    private readonly Mock<IBrokerService> _mockBroker;
    private readonly Mock<IExecutionService> _mockExecution;
    private readonly IncomeUniverse _universe;
    private readonly IncomeConfig _incomeConfig;
    private readonly ExecutionConfig _executionConfig;
    private readonly IncomeSleeveManager _manager;

    public IncomeSleeveManagerTests()
    {
        _mockBroker = new Mock<IBrokerService>();
        _mockExecution = new Mock<IExecutionService>();
        _universe = new IncomeUniverse();
        _incomeConfig = new IncomeConfig();
        _executionConfig = new ExecutionConfig();

        _manager = new IncomeSleeveManager(
            _mockBroker.Object,
            _mockExecution.Object,
            _universe,
            _incomeConfig,
            _executionConfig,
            Mock.Of<ILogger<IncomeSleeveManager>>());
    }

    private List<Position> CreateIncomePositions()
    {
        return new List<Position>
        {
            new() { Symbol = "VIG", Quantity = 10, MarketPrice = 180, Category = "DividendGrowthETF", Sleeve = SleeveType.Income },
            new() { Symbol = "JEPI", Quantity = 20, MarketPrice = 55, Category = "CoveredCallETF", Sleeve = SleeveType.Income },
            new() { Symbol = "ARCC", Quantity = 50, MarketPrice = 20, Category = "BDC", Sleeve = SleeveType.Income },
            new() { Symbol = "O", Quantity = 10, MarketPrice = 55, Category = "EquityREIT", Sleeve = SleeveType.Income },
            new() { Symbol = "AGNC", Quantity = 30, MarketPrice = 10, Category = "MortgageREIT", Sleeve = SleeveType.Income },
            new() { Symbol = "PFF", Quantity = 15, MarketPrice = 32, Category = "PreferredsIGCredit", Sleeve = SleeveType.Income }
        };
    }

    [Fact]
    public async Task GetSleeveStateAsync_TagsAndBuildsState()
    {
        _mockBroker.Setup(b => b.GetPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>
            {
                new() { Symbol = "VIG", Quantity = 10, MarketPrice = 180 },
                new() { Symbol = "AAPL", Quantity = 5, MarketPrice = 200 } // not income
            });

        _mockBroker.Setup(b => b.GetAccountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Account { CashBufferValue = 500 });

        var state = await _manager.GetSleeveStateAsync();

        Assert.True(state.TotalValue > 0);
        // VIG should be tagged as income, AAPL should not contribute
        Assert.Contains(state.IssuerExposures, e => e.Issuer == "VIG");
    }

    [Fact]
    public void GenerateReinvestmentPlan_InsufficientCash_ReturnsEmptyPlan()
    {
        var positions = CreateIncomePositions();
        var state = IncomeDriftCalculator.BuildSleeveState(positions, 0, _incomeConfig);

        var plan = _manager.GenerateReinvestmentPlan(state, 50); // below $100 min

        Assert.Empty(plan.ProposedBuys);
    }

    [Fact]
    public void GenerateReinvestmentPlan_PrioritizesMostUnderweight()
    {
        // All in DividendGrowthETF -> other categories very underweight
        var positions = new List<Position>
        {
            new() { Symbol = "VIG", Quantity = 100, MarketPrice = 100, Category = "DividendGrowthETF", Sleeve = SleeveType.Income }
        };
        var state = IncomeDriftCalculator.BuildSleeveState(positions, 0, _incomeConfig);

        var plan = _manager.GenerateReinvestmentPlan(state, 2000);

        Assert.True(plan.ProposedBuys.Count > 0);
        // First buy should be for one of the categories at -20% target (CoveredCallETF or BDC)
        // or -10% target (EquityREIT, MortgageREIT, PreferredsIGCredit)
        // The exact ordering depends on sorting by drift value
        var firstBuy = plan.ProposedBuys[0];
        // Should NOT be DividendGrowthETF since that's overweight
        Assert.NotEqual(IncomeCategory.DividendGrowthETF, firstBuy.Category);
        // First buy's category should have negative drift (underweight)
        Assert.True(state.CategoryDrift[firstBuy.Category] < 0,
            $"First buy category {firstBuy.Category} should be underweight");
    }

    [Fact]
    public void GenerateReinvestmentPlan_RespectsIssuerCap()
    {
        // Set up a state where the only available candidate in a category already exceeds issuer cap
        var config = new IncomeConfig { MaxIssuerPercent = 0.05m }; // 5% cap
        var execConfig = new ExecutionConfig();
        var manager = new IncomeSleeveManager(
            _mockBroker.Object, _mockExecution.Object, _universe,
            config, execConfig, Mock.Of<ILogger<IncomeSleeveManager>>());

        // JEPI is at 9% of total (900/10000) -- adding more would exceed 5% easily
        var positions = new List<Position>
        {
            new() { Symbol = "JEPI", Quantity = 90, MarketPrice = 10, Category = "CoveredCallETF", Sleeve = SleeveType.Income }
        };
        var state = IncomeDriftCalculator.BuildSleeveState(positions, 9100, config);

        var plan = manager.GenerateReinvestmentPlan(state, 1000);

        // Should not buy any CoveredCallETF since all candidates (JEPI etc.) could violate 5% cap
        // JEPI is already at 9% so it should be skipped
        var ccBuys = plan.ProposedBuys.Where(b => b.Category == IncomeCategory.CoveredCallETF).ToList();
        // Some candidates in CoveredCallETF (JEPQ, XYLD, QYLD) have 0% exposure so they could pass
        // But JEPI specifically should not be selected since it's above 5%
        Assert.DoesNotContain(ccBuys, b => b.Symbol == "JEPI");
    }

    [Fact]
    public void GenerateReinvestmentPlan_RespectsMinLotDollars()
    {
        var execConfig = new ExecutionConfig { MinLotDollars = 500m };
        var manager = new IncomeSleeveManager(
            _mockBroker.Object, _mockExecution.Object, _universe,
            _incomeConfig, execConfig, Mock.Of<ILogger<IncomeSleeveManager>>());

        // Only $200 cash available, but min lot is $500
        var positions = CreateIncomePositions();
        var state = IncomeDriftCalculator.BuildSleeveState(positions, 0, _incomeConfig);

        var plan = manager.GenerateReinvestmentPlan(state, 200);

        Assert.Empty(plan.ProposedBuys);
    }

    [Fact]
    public void GenerateReinvestmentPlan_NoBuysWhenCashInsufficient()
    {
        var positions = CreateIncomePositions();
        var state = IncomeDriftCalculator.BuildSleeveState(positions, 0, _incomeConfig);

        var plan = _manager.GenerateReinvestmentPlan(state, 0);

        Assert.Empty(plan.ProposedBuys);
    }

    [Fact]
    public void GenerateReinvestmentPlan_MultiCategory_AllocatesAcrossCategories()
    {
        // All positions in one category -- all others are underweight
        var positions = new List<Position>
        {
            new() { Symbol = "VIG", Quantity = 50, MarketPrice = 200, Category = "DividendGrowthETF", Sleeve = SleeveType.Income }
        };
        var state = IncomeDriftCalculator.BuildSleeveState(positions, 0, _incomeConfig);

        var plan = _manager.GenerateReinvestmentPlan(state, 5000);

        // Should have buys across multiple categories
        var categories = plan.ProposedBuys.Select(b => b.Category).Distinct().ToList();
        Assert.True(categories.Count >= 2, $"Expected at least 2 categories, got {categories.Count}");
    }

    [Fact]
    public async Task ExecuteReinvestmentPlanAsync_EmptyPlan_ReturnsEmpty()
    {
        var plan = new ReinvestmentPlan();

        var results = await _manager.ExecuteReinvestmentPlanAsync(plan);

        Assert.Empty(results);
        Assert.False(plan.WasExecuted);
    }

    [Fact]
    public async Task ExecuteReinvestmentPlanAsync_PlacesLimitOrders()
    {
        var plan = new ReinvestmentPlan
        {
            ProposedBuys = new List<ReinvestmentOrder>
            {
                new() { Symbol = "VIG", Category = IncomeCategory.DividendGrowthETF, Amount = 500, Rationale = "Test" }
            }
        };

        _mockBroker.Setup(b => b.GetQuotesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Quote>
            {
                new() { Symbol = "VIG", Ask = 180m, Last = 179m, Bid = 178m }
            });

        _mockExecution.Setup(e => e.ExecuteSignalAsync(It.IsAny<Signal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                Success = true,
                Orders = new List<Order> { new() { Id = "order-1", BrokerId = "1001" } }
            });

        var results = await _manager.ExecuteReinvestmentPlanAsync(plan);

        Assert.Single(results);
        Assert.True(results[0].Success);
        Assert.True(plan.WasExecuted);
        Assert.Single(plan.ExecutedOrderIds);

        // Verify signal was created with correct limit price (ask)
        _mockExecution.Verify(e => e.ExecuteSignalAsync(
            It.Is<Signal>(s =>
                s.Symbol == "VIG" &&
                s.SuggestedEntryPrice == 180m &&
                s.Direction == SignalDirection.Long),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteReinvestmentPlanAsync_SkipsWhenNoQuote()
    {
        var plan = new ReinvestmentPlan
        {
            ProposedBuys = new List<ReinvestmentOrder>
            {
                new() { Symbol = "VIG", Category = IncomeCategory.DividendGrowthETF, Amount = 500, Rationale = "Test" }
            }
        };

        _mockBroker.Setup(b => b.GetQuotesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Quote>()); // No quotes returned

        var results = await _manager.ExecuteReinvestmentPlanAsync(plan);

        Assert.Empty(results);
        Assert.False(plan.WasExecuted);
    }

    [Fact]
    public async Task ExecuteReinvestmentPlanAsync_CalculatesShareCount()
    {
        var plan = new ReinvestmentPlan
        {
            ProposedBuys = new List<ReinvestmentOrder>
            {
                new() { Symbol = "VIG", Category = IncomeCategory.DividendGrowthETF, Amount = 1000, Rationale = "Test" }
            }
        };

        _mockBroker.Setup(b => b.GetQuotesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Quote>
            {
                new() { Symbol = "VIG", Ask = 180m, Last = 179m }
            });

        _mockExecution.Setup(e => e.ExecuteSignalAsync(It.IsAny<Signal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecutionResult
            {
                Success = true,
                Orders = new List<Order> { new() { Id = "order-1" } }
            });

        await _manager.ExecuteReinvestmentPlanAsync(plan);

        // 1000 / 180 = 5.55 -> 5 shares
        _mockExecution.Verify(e => e.ExecuteSignalAsync(
            It.Is<Signal>(s => s.SuggestedPositionSize == 5),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void GenerateReinvestmentPlan_RespectsCategoryCap()
    {
        var config = new IncomeConfig { MaxCategoryPercent = 0.05m }; // Very low 5% cap
        var execConfig = new ExecutionConfig();
        var manager = new IncomeSleeveManager(
            _mockBroker.Object, _mockExecution.Object, _universe,
            config, execConfig, Mock.Of<ILogger<IncomeSleeveManager>>());

        // CoveredCallETF already at 4% of a large portfolio
        var positions = new List<Position>
        {
            new() { Symbol = "VIG", Quantity = 480, MarketPrice = 10, Category = "DividendGrowthETF", Sleeve = SleeveType.Income },
            new() { Symbol = "JEPI", Quantity = 40, MarketPrice = 10, Category = "CoveredCallETF", Sleeve = SleeveType.Income }
        };
        var state = IncomeDriftCalculator.BuildSleeveState(positions, 4800, config);

        var plan = manager.GenerateReinvestmentPlan(state, 1000);

        // With 5% category cap, many adds would violate. CoveredCallETF at 400/10000=4% + 1000 => 1400/11000=12.7% > 5%
        var ccBuys = plan.ProposedBuys.Where(b => b.Category == IncomeCategory.CoveredCallETF).ToList();
        Assert.Empty(ccBuys);
    }

    [Fact]
    public void GenerateReinvestmentPlan_SelectsLowestExposureCandidate()
    {
        // Create positions where one BDC candidate already has high exposure
        var positions = new List<Position>
        {
            new() { Symbol = "ARCC", Quantity = 50, MarketPrice = 20, Category = "BDC", Sleeve = SleeveType.Income },
            // Total BDC value = 1000, but ARCC has all of it
        };
        var state = IncomeDriftCalculator.BuildSleeveState(positions, 9000, _incomeConfig);

        var plan = _manager.GenerateReinvestmentPlan(state, 2000);

        // If BDC is underweight, the manager should prefer MAIN or HTGC (0% exposure) over ARCC (10%)
        var bdcBuys = plan.ProposedBuys.Where(b => b.Category == IncomeCategory.BDC).ToList();
        if (bdcBuys.Count > 0)
        {
            Assert.NotEqual("ARCC", bdcBuys[0].Symbol);
        }
    }
}
