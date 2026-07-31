using LiveSplit.UI.Components;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class ComparisonNamingTests
{
    [Theory]
    [InlineData("Current Comparison", "Current Pace")]
    [InlineData("Personal Best", "Current Pace")]
    [InlineData("Best Segments", "Best Possible Time")]
    [InlineData("Worst Segments", "Worst Possible Time")]
    [InlineData("Average Segments", "Predicted Time")]
    public void DisplayedNamesMatchStockRunPrediction(string comparison, string expected)
        => Assert.Equal(expected, ComparisonNaming.GetDisplayedName(comparison));

    [Fact]
    public void CustomComparisonGetsCurrentPaceParenthetical()
        => Assert.Equal("Current Pace (My Comp)", ComparisonNaming.GetDisplayedName("My Comp"));

    [Fact]
    public void BestSegmentsAbbreviationsMatchStock()
        => Assert.Equal(new[] { "Best Poss. Time", "Best Time", "BPT" },
            ComparisonNaming.GetAbbreviations("Best Segments"));
}
