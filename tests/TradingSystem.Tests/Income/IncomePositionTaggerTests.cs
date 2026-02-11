using TradingSystem.Core.Configuration;
using TradingSystem.Core.Models;
using TradingSystem.Strategies.Income;
using Xunit;

namespace TradingSystem.Tests.Income;

public class IncomePositionTaggerTests
{
    private readonly IncomeUniverse _universe = new();

    [Fact]
    public void TagPositions_TagsKnownSymbols()
    {
        var positions = new List<Position>
        {
            new() { Symbol = "VIG", Quantity = 100, MarketPrice = 180 },
            new() { Symbol = "ARCC", Quantity = 200, MarketPrice = 20 }
        };

        IncomePositionTagger.TagPositions(positions, _universe);

        Assert.Equal(SleeveType.Income, positions[0].Sleeve);
        Assert.Equal("DividendGrowthETF", positions[0].Category);
        Assert.Equal(SleeveType.Income, positions[1].Sleeve);
        Assert.Equal("BDC", positions[1].Category);
    }

    [Fact]
    public void TagPositions_LeavesUnknownUntagged()
    {
        var positions = new List<Position>
        {
            new() { Symbol = "AAPL", Quantity = 50, MarketPrice = 200 }
        };

        IncomePositionTagger.TagPositions(positions, _universe);

        // AAPL is not in income universe -- default sleeve is Income but Category stays empty
        Assert.Equal(string.Empty, positions[0].Category);
    }

    [Fact]
    public void TagPositions_CaseInsensitive()
    {
        var positions = new List<Position>
        {
            new() { Symbol = "vig", Quantity = 100, MarketPrice = 180 }
        };

        IncomePositionTagger.TagPositions(positions, _universe);

        Assert.Equal(SleeveType.Income, positions[0].Sleeve);
        Assert.Equal("DividendGrowthETF", positions[0].Category);
    }

    [Fact]
    public void TagPositions_EmptyList_DoesNotThrow()
    {
        var positions = new List<Position>();

        IncomePositionTagger.TagPositions(positions, _universe);

        Assert.Empty(positions);
    }

    [Fact]
    public void GetIncomePositions_FiltersCorrectly()
    {
        var positions = new List<Position>
        {
            new() { Symbol = "VIG", Sleeve = SleeveType.Income, Quantity = 100, MarketPrice = 180 },
            new() { Symbol = "SPY", Sleeve = SleeveType.Tactical, Quantity = 50, MarketPrice = 500 },
            new() { Symbol = "ARCC", Sleeve = SleeveType.Income, Quantity = 200, MarketPrice = 20 }
        };

        var incomeOnly = IncomePositionTagger.GetIncomePositions(positions);

        Assert.Equal(2, incomeOnly.Count);
        Assert.All(incomeOnly, p => Assert.Equal(SleeveType.Income, p.Sleeve));
    }
}
