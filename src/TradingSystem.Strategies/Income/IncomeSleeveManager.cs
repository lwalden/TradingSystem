using Microsoft.Extensions.Logging;
using TradingSystem.Core.Configuration;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Strategies.Income;

/// <summary>
/// Orchestrates income sleeve operations: drift calculation, buy list generation,
/// and reinvestment execution. This is NOT a strategy (IStrategy) -- it's a higher-level
/// manager that coordinates the MonthlyReinvestStrategy's output with execution.
/// </summary>
public class IncomeSleeveManager
{
    private readonly IBrokerService _broker;
    private readonly IExecutionService _executionService;
    private readonly IncomeUniverse _universe;
    private readonly IncomeConfig _config;
    private readonly ExecutionConfig _executionConfig;
    private readonly ILogger<IncomeSleeveManager> _logger;

    public IncomeSleeveManager(
        IBrokerService broker,
        IExecutionService executionService,
        IncomeUniverse universe,
        IncomeConfig config,
        ExecutionConfig executionConfig,
        ILogger<IncomeSleeveManager> logger)
    {
        _broker = broker;
        _executionService = executionService;
        _universe = universe;
        _config = config;
        _executionConfig = executionConfig;
        _logger = logger;
    }

    /// <summary>
    /// Fetches current positions from the broker, tags them, and builds the sleeve state.
    /// </summary>
    public async Task<IncomeSleeveState> GetSleeveStateAsync(
        CancellationToken cancellationToken = default)
    {
        var positions = await _broker.GetPositionsAsync(cancellationToken);
        IncomePositionTagger.TagPositions(positions, _universe);

        var incomePositions = IncomePositionTagger.GetIncomePositions(positions);

        var account = await _broker.GetAccountAsync(cancellationToken);
        var cashBuffer = account.CashBufferValue;

        var state = IncomeDriftCalculator.BuildSleeveState(incomePositions, cashBuffer, _config);

        _logger.LogInformation(
            "Income sleeve state: total={Total:C}, positions={Count}, categories={Categories}",
            state.TotalValue, incomePositions.Count, state.Categories.Count);

        return state;
    }

    /// <summary>
    /// Generates a reinvestment plan that allocates available cash to underweight categories.
    /// Does NOT execute anything -- returns a plan for review or execution.
    /// </summary>
    public async Task<ReinvestmentPlan> GenerateReinvestmentPlanAsync(
        decimal availableCash,
        CancellationToken cancellationToken = default)
    {
        var state = await GetSleeveStateAsync(cancellationToken);
        return GenerateReinvestmentPlan(state, availableCash, cancellationToken);
    }

    /// <summary>
    /// Generates a reinvestment plan from a pre-built sleeve state.
    /// Pure logic -- no I/O except quote fetching.
    /// </summary>
    public ReinvestmentPlan GenerateReinvestmentPlan(
        IncomeSleeveState state,
        decimal availableCash,
        CancellationToken cancellationToken = default)
    {
        var plan = new ReinvestmentPlan
        {
            PlanDate = DateTime.UtcNow,
            AvailableCash = availableCash
        };

        if (availableCash < _executionConfig.MinLotDollars)
        {
            _logger.LogInformation(
                "Insufficient cash for reinvestment: {Cash:C} < {Min:C}",
                availableCash, _executionConfig.MinLotDollars);
            return plan;
        }

        var underweightCategories = IncomeDriftCalculator.GetUnderweightCategories(state);
        var remainingCash = availableCash;

        foreach (var (category, drift) in underweightCategories)
        {
            if (remainingCash < _executionConfig.MinLotDollars)
                break;

            var candidates = _universe.GetByCategory(category)
                .Where(s => s.IsEnabled)
                .ToList();

            if (candidates.Count == 0) continue;

            // Pick the candidate that least increases issuer concentration
            var bestCandidate = PickBestCandidate(candidates, state);
            if (bestCandidate == null) continue;

            // Calculate how much to allocate to this category
            var targetAmount = Math.Abs(drift) * state.TotalValue;
            var allocateAmount = Math.Min(targetAmount, remainingCash);

            if (allocateAmount < _executionConfig.MinLotDollars)
                continue;

            // Check issuer cap
            if (IncomeDriftCalculator.WouldViolateIssuerCap(
                bestCandidate.Symbol, allocateAmount, state, _config.MaxIssuerPercent))
            {
                _logger.LogInformation(
                    "Skipping {Symbol}: would violate issuer cap of {Cap:P0}",
                    bestCandidate.Symbol, _config.MaxIssuerPercent);
                continue;
            }

            // Check category cap
            if (IncomeDriftCalculator.WouldViolateCategoryCap(
                category, allocateAmount, state, _config.MaxCategoryPercent))
            {
                _logger.LogInformation(
                    "Skipping {Category}: would violate category cap of {Cap:P0}",
                    category, _config.MaxCategoryPercent);
                continue;
            }

            plan.ProposedBuys.Add(new ReinvestmentOrder
            {
                Symbol = bestCandidate.Symbol,
                Category = category,
                Amount = allocateAmount,
                LimitPrice = 0, // Set during execution with live quote
                Shares = 0, // Set during execution with live quote
                Rationale = $"Reduce {category} drift of {drift:P1}",
                DriftReduction = Math.Abs(drift)
            });

            remainingCash -= allocateAmount;
        }

        _logger.LogInformation(
            "Generated reinvestment plan: {Count} buys totaling {Total:C} from {Available:C} available",
            plan.ProposedBuys.Count, plan.TotalProposedAmount, availableCash);

        return plan;
    }

    /// <summary>
    /// Executes a reinvestment plan by fetching live quotes, calculating share counts,
    /// and placing limit orders at the ask price.
    /// </summary>
    public async Task<List<ExecutionResult>> ExecuteReinvestmentPlanAsync(
        ReinvestmentPlan plan,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ExecutionResult>();

        if (plan.ProposedBuys.Count == 0)
        {
            _logger.LogInformation("No buys in reinvestment plan -- nothing to execute");
            return results;
        }

        // Fetch live quotes for all symbols
        var symbols = plan.ProposedBuys.Select(b => b.Symbol).Distinct().ToList();
        var quotes = await _broker.GetQuotesAsync(symbols, cancellationToken);
        var quoteMap = quotes.ToDictionary(q => q.Symbol, StringComparer.OrdinalIgnoreCase);

        foreach (var buy in plan.ProposedBuys)
        {
            if (!quoteMap.TryGetValue(buy.Symbol, out var quote))
            {
                _logger.LogWarning("No quote available for {Symbol}, skipping", buy.Symbol);
                continue;
            }

            var limitPrice = quote.Ask > 0 ? quote.Ask : quote.Last;
            if (limitPrice <= 0)
            {
                _logger.LogWarning("Invalid price for {Symbol}, skipping", buy.Symbol);
                continue;
            }

            var shares = (int)(buy.Amount / limitPrice);
            if (shares < 1)
            {
                _logger.LogInformation(
                    "Amount {Amount:C} too small for 1 share of {Symbol} @ {Price:C}",
                    buy.Amount, buy.Symbol, limitPrice);
                continue;
            }

            buy.LimitPrice = limitPrice;
            buy.Shares = shares;

            var signal = new Signal
            {
                StrategyId = "income-monthly-reinvest",
                StrategyName = "Income Monthly Reinvest",
                SetupType = "MonthlyReinvest",
                Symbol = buy.Symbol,
                Direction = SignalDirection.Long,
                Strength = SignalStrength.Moderate,
                SuggestedEntryPrice = limitPrice,
                SuggestedPositionSize = shares,
                SuggestedRiskAmount = shares * limitPrice,
                Rationale = buy.Rationale,
                GeneratedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(8)
            };

            var result = await _executionService.ExecuteSignalAsync(signal, cancellationToken);
            results.Add(result);

            if (result.Success && result.Orders.Count > 0)
            {
                plan.ExecutedOrderIds.Add(result.Orders[0].Id);
            }
        }

        if (results.Any(r => r.Success))
        {
            plan.WasExecuted = true;
            plan.ExecutedAt = DateTime.UtcNow;
        }

        _logger.LogInformation(
            "Executed reinvestment plan: {Success}/{Total} orders placed",
            results.Count(r => r.Success), results.Count);

        return results;
    }

    /// <summary>
    /// Full monthly reinvest workflow: calculate state, generate plan, execute.
    /// </summary>
    public async Task<(ReinvestmentPlan Plan, List<ExecutionResult> Results)> RunMonthlyReinvestAsync(
        decimal availableCash,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting monthly reinvestment with {Cash:C} available", availableCash);

        var state = await GetSleeveStateAsync(cancellationToken);
        var plan = GenerateReinvestmentPlan(state, availableCash, cancellationToken);

        if (plan.ProposedBuys.Count == 0)
        {
            _logger.LogInformation("No reinvestment orders to place");
            return (plan, new List<ExecutionResult>());
        }

        var results = await ExecuteReinvestmentPlanAsync(plan, cancellationToken);
        return (plan, results);
    }

    /// <summary>
    /// Pick the best candidate from a list: the one that least increases issuer concentration.
    /// </summary>
    private IncomeSecurity? PickBestCandidate(
        List<IncomeSecurity> candidates,
        IncomeSleeveState state)
    {
        return candidates
            .OrderBy(c =>
            {
                var exposure = state.IssuerExposures
                    .FirstOrDefault(e => e.Issuer.Equals(c.Symbol, StringComparison.OrdinalIgnoreCase));
                return exposure?.ExposurePercent ?? 0m;
            })
            .FirstOrDefault(c =>
            {
                // Ensure the candidate doesn't already exceed issuer cap
                var exposure = state.IssuerExposures
                    .FirstOrDefault(e => e.Issuer.Equals(c.Symbol, StringComparison.OrdinalIgnoreCase));
                return (exposure?.ExposurePercent ?? 0m) < _config.MaxIssuerPercent;
            });
    }
}
