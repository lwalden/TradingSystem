using Xunit;
using TradingSystem.Strategies.Options;
using TradingSystem.Core.Interfaces;
using TradingSystem.Core.Models;

namespace TradingSystem.Tests.Options;

public class OptionCandidateScorerTests
{
    [Fact]
    public void Score_CSP_HighIVAndGoodRoR_ScoresHigh()
    {
        // Arrange
        var candidate = new OptionCandidate
        {
            UnderlyingSymbol = "AAPL",
            Strategy = StrategyType.CashSecuredPut,
            MaxProfit = 150m,
            MaxLoss = -1000m,
            NetCredit = 150m,
            ProbabilityOfProfit = 75m,
            IVRank = 80m,
            IVPercentile = 85m,
            UnderlyingPrice = 170m,
            Legs = new List<OptionLeg>
            {
                new OptionLeg { Expiration = DateTime.Today.AddDays(35) }
            }
        };

        // Act
        var score = OptionCandidateScorer.Score(candidate);

        // Assert
        Assert.True(score > 70, $"Expected score > 70, got {score}");
        Assert.Equal(score, candidate.Score);
        Assert.False(string.IsNullOrEmpty(candidate.ScoreBreakdown));
    }

    [Fact]
    public void Score_CSP_LowIVRank_ScoresLower()
    {
        // Arrange
        var highIVCandidate = new OptionCandidate
        {
            UnderlyingSymbol = "AAPL",
            Strategy = StrategyType.CashSecuredPut,
            MaxProfit = 150m,
            MaxLoss = -1000m,
            NetCredit = 150m,
            ProbabilityOfProfit = 75m,
            IVRank = 80m,
            IVPercentile = 85m,
            UnderlyingPrice = 170m,
            Legs = new List<OptionLeg>
            {
                new OptionLeg { Expiration = DateTime.Today.AddDays(35) }
            }
        };

        var lowIVCandidate = new OptionCandidate
        {
            UnderlyingSymbol = "AAPL",
            Strategy = StrategyType.CashSecuredPut,
            MaxProfit = 150m,
            MaxLoss = -1000m,
            NetCredit = 150m,
            ProbabilityOfProfit = 75m,
            IVRank = 20m,
            IVPercentile = 25m,
            UnderlyingPrice = 170m,
            Legs = new List<OptionLeg>
            {
                new OptionLeg { Expiration = DateTime.Today.AddDays(35) }
            }
        };

        // Act
        var highScore = OptionCandidateScorer.Score(highIVCandidate);
        var lowScore = OptionCandidateScorer.Score(lowIVCandidate);

        // Assert
        Assert.True(highScore > lowScore, $"High IV score ({highScore}) should be > low IV score ({lowScore})");
    }

    [Fact]
    public void Score_BullPutSpread_OptimalDTE_ScoresHigher()
    {
        // Arrange
        var optimalDTECandidate = new OptionCandidate
        {
            UnderlyingSymbol = "AAPL",
            Strategy = StrategyType.BullPutSpread,
            MaxProfit = 100m,
            MaxLoss = -400m,
            NetCredit = 100m,
            ProbabilityOfProfit = 70m,
            IVRank = 60m,
            IVPercentile = 65m,
            UnderlyingPrice = 170m,
            Legs = new List<OptionLeg>
            {
                new OptionLeg { Expiration = DateTime.Today.AddDays(35) }
            }
        };

        var subOptimalDTECandidate = new OptionCandidate
        {
            UnderlyingSymbol = "AAPL",
            Strategy = StrategyType.BullPutSpread,
            MaxProfit = 100m,
            MaxLoss = -400m,
            NetCredit = 100m,
            ProbabilityOfProfit = 70m,
            IVRank = 60m,
            IVPercentile = 65m,
            UnderlyingPrice = 170m,
            Legs = new List<OptionLeg>
            {
                new OptionLeg { Expiration = DateTime.Today.AddDays(70) }
            }
        };

        // Act
        var optimalScore = OptionCandidateScorer.Score(optimalDTECandidate);
        var subOptimalScore = OptionCandidateScorer.Score(subOptimalDTECandidate);

        // Assert
        Assert.True(optimalScore > subOptimalScore,
            $"Optimal DTE score ({optimalScore}) should be > suboptimal DTE score ({subOptimalScore})");
    }

    [Fact]
    public void Score_BearCallSpread_HighPOP_ScoresHigher()
    {
        // Arrange
        var highPOPCandidate = new OptionCandidate
        {
            UnderlyingSymbol = "AAPL",
            Strategy = StrategyType.BearCallSpread,
            MaxProfit = 100m,
            MaxLoss = -400m,
            NetCredit = 100m,
            ProbabilityOfProfit = 85m,
            IVRank = 60m,
            IVPercentile = 65m,
            UnderlyingPrice = 170m,
            Legs = new List<OptionLeg>
            {
                new OptionLeg { Expiration = DateTime.Today.AddDays(35) }
            }
        };

        var mediumPOPCandidate = new OptionCandidate
        {
            UnderlyingSymbol = "AAPL",
            Strategy = StrategyType.BearCallSpread,
            MaxProfit = 100m,
            MaxLoss = -400m,
            NetCredit = 100m,
            ProbabilityOfProfit = 60m,
            IVRank = 60m,
            IVPercentile = 65m,
            UnderlyingPrice = 170m,
            Legs = new List<OptionLeg>
            {
                new OptionLeg { Expiration = DateTime.Today.AddDays(35) }
            }
        };

        // Act
        var highScore = OptionCandidateScorer.Score(highPOPCandidate);
        var mediumScore = OptionCandidateScorer.Score(mediumPOPCandidate);

        // Assert
        Assert.True(highScore > mediumScore,
            $"High POP score ({highScore}) should be > medium POP score ({mediumScore})");
    }

    [Fact]
    public void Score_IronCondor_HighReturnOnRisk_ScoresHigher()
    {
        // Arrange
        var highRoRCandidate = new OptionCandidate
        {
            UnderlyingSymbol = "AAPL",
            Strategy = StrategyType.IronCondor,
            MaxProfit = 200m,
            MaxLoss = -800m,
            NetCredit = 200m,
            ProbabilityOfProfit = 75m,
            IVRank = 70m,
            IVPercentile = 75m,
            UnderlyingPrice = 170m,
            Legs = new List<OptionLeg>
            {
                new OptionLeg { Expiration = DateTime.Today.AddDays(35) }
            }
        };

        var lowRoRCandidate = new OptionCandidate
        {
            UnderlyingSymbol = "AAPL",
            Strategy = StrategyType.IronCondor,
            MaxProfit = 100m,
            MaxLoss = -2000m,
            NetCredit = 100m,
            ProbabilityOfProfit = 75m,
            IVRank = 70m,
            IVPercentile = 75m,
            UnderlyingPrice = 170m,
            Legs = new List<OptionLeg>
            {
                new OptionLeg { Expiration = DateTime.Today.AddDays(35) }
            }
        };

        // Act
        var highScore = OptionCandidateScorer.Score(highRoRCandidate);
        var lowScore = OptionCandidateScorer.Score(lowRoRCandidate);

        // Assert
        Assert.True(highScore > lowScore,
            $"High RoR score ({highScore}) should be > low RoR score ({lowScore})");
    }

    [Fact]
    public void Score_CalendarSpread_OptimalDTE_ScoresHigher()
    {
        // Arrange
        var optimalDTECandidate = new OptionCandidate
        {
            UnderlyingSymbol = "AAPL",
            Strategy = StrategyType.CalendarSpread,
            MaxProfit = 150m,
            MaxLoss = -500m,
            NetCredit = 150m,
            ProbabilityOfProfit = 65m,
            IVRank = 50m,
            IVPercentile = 55m,
            UnderlyingPrice = 170m,
            Legs = new List<OptionLeg>
            {
                new OptionLeg { Expiration = DateTime.Today.AddDays(45) }
            }
        };

        var subOptimalDTECandidate = new OptionCandidate
        {
            UnderlyingSymbol = "AAPL",
            Strategy = StrategyType.CalendarSpread,
            MaxProfit = 150m,
            MaxLoss = -500m,
            NetCredit = 150m,
            ProbabilityOfProfit = 65m,
            IVRank = 50m,
            IVPercentile = 55m,
            UnderlyingPrice = 170m,
            Legs = new List<OptionLeg>
            {
                new OptionLeg { Expiration = DateTime.Today.AddDays(90) }
            }
        };

        // Act
        var optimalScore = OptionCandidateScorer.Score(optimalDTECandidate);
        var subOptimalScore = OptionCandidateScorer.Score(subOptimalDTECandidate);

        // Assert
        Assert.True(optimalScore > subOptimalScore,
            $"Optimal DTE score ({optimalScore}) should be > suboptimal DTE score ({subOptimalScore})");
    }

    [Fact]
    public void Score_SetsScoreBreakdownString()
    {
        // Arrange
        var candidate = new OptionCandidate
        {
            UnderlyingSymbol = "AAPL",
            Strategy = StrategyType.CashSecuredPut,
            MaxProfit = 150m,
            MaxLoss = -1000m,
            NetCredit = 150m,
            ProbabilityOfProfit = 75m,
            IVRank = 80m,
            IVPercentile = 85m,
            UnderlyingPrice = 170m,
            Legs = new List<OptionLeg>
            {
                new OptionLeg { Expiration = DateTime.Today.AddDays(35) }
            }
        };

        // Act
        OptionCandidateScorer.Score(candidate);

        // Assert
        Assert.False(string.IsNullOrEmpty(candidate.ScoreBreakdown));
        Assert.Contains("RoR", candidate.ScoreBreakdown);
        Assert.Contains("IV", candidate.ScoreBreakdown);
        Assert.Contains("POP", candidate.ScoreBreakdown);
        Assert.Contains("DTE", candidate.ScoreBreakdown);
    }

    [Fact]
    public void Score_ClampsTo0To100()
    {
        // Arrange - extreme values that might push score out of bounds
        var extremeCandidate = new OptionCandidate
        {
            UnderlyingSymbol = "AAPL",
            Strategy = StrategyType.CashSecuredPut,
            MaxProfit = 10000m,
            MaxLoss = -100m,
            NetCredit = 10000m,
            ProbabilityOfProfit = 99m,
            IVRank = 100m,
            IVPercentile = 100m,
            UnderlyingPrice = 170m,
            Legs = new List<OptionLeg>
            {
                new OptionLeg { Expiration = DateTime.Today.AddDays(35) }
            }
        };

        // Act
        var score = OptionCandidateScorer.Score(extremeCandidate);

        // Assert
        Assert.InRange(score, 0, 100);
    }

    [Fact]
    public void Score_ZeroMaxLoss_HandlesGracefully()
    {
        // Arrange
        var candidate = new OptionCandidate
        {
            UnderlyingSymbol = "AAPL",
            Strategy = StrategyType.CashSecuredPut,
            MaxProfit = 150m,
            MaxLoss = 0m,
            NetCredit = 150m,
            ProbabilityOfProfit = 75m,
            IVRank = 80m,
            IVPercentile = 85m,
            UnderlyingPrice = 170m,
            Legs = new List<OptionLeg>
            {
                new OptionLeg { Expiration = DateTime.Today.AddDays(35) }
            }
        };

        // Act
        var score = OptionCandidateScorer.Score(candidate);

        // Assert
        Assert.Equal(0, candidate.ReturnOnRisk);
        Assert.InRange(score, 0, 100);
        Assert.False(string.IsNullOrEmpty(candidate.ScoreBreakdown));
    }

    [Fact]
    public void Score_DifferentStrategiesDifferentWeights()
    {
        // Arrange - same metrics but different strategies
        var cspCandidate = new OptionCandidate
        {
            UnderlyingSymbol = "AAPL",
            Strategy = StrategyType.CashSecuredPut,
            MaxProfit = 150m,
            MaxLoss = -1000m,
            NetCredit = 150m,
            ProbabilityOfProfit = 75m,
            IVRank = 70m,
            IVPercentile = 75m,
            UnderlyingPrice = 170m,
            Legs = new List<OptionLeg>
            {
                new OptionLeg { Expiration = DateTime.Today.AddDays(35) }
            }
        };

        var calendarCandidate = new OptionCandidate
        {
            UnderlyingSymbol = "AAPL",
            Strategy = StrategyType.CalendarSpread,
            MaxProfit = 150m,
            MaxLoss = -1000m,
            NetCredit = 150m,
            ProbabilityOfProfit = 75m,
            IVRank = 70m,
            IVPercentile = 75m,
            UnderlyingPrice = 170m,
            Legs = new List<OptionLeg>
            {
                new OptionLeg { Expiration = DateTime.Today.AddDays(35) }
            }
        };

        // Act
        var cspScore = OptionCandidateScorer.Score(cspCandidate);
        var calendarScore = OptionCandidateScorer.Score(calendarCandidate);

        // Assert
        Assert.NotEqual(cspScore, calendarScore);
        Assert.InRange(cspScore, 0, 100);
        Assert.InRange(calendarScore, 0, 100);
    }
}
