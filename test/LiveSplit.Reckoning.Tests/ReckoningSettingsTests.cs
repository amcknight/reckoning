using System.Drawing;
using System.Xml;
using LiveSplit.TimeFormatters;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class ReckoningSettingsTests
{
    private static ReckoningComponentSettings Roundtrip(ReckoningComponentSettings s)
    {
        var doc = new XmlDocument();
        var node = s.GetSettings(doc);
        var fresh = new ReckoningComponentSettings();
        fresh.SetSettings(node);
        return fresh;
    }

    [Fact]
    public void DefaultsMatchStockRunPrediction()
    {
        var s = new ReckoningComponentSettings();
        Assert.Equal("Current Comparison", s.Comparison);
        Assert.False(s.OverrideTextColor);
        Assert.False(s.OverrideTimeColor);
        Assert.Equal(TimeAccuracy.Seconds, s.Accuracy);
        Assert.False(s.Display2Rows);
        Assert.Equal(GradientType.Plain, s.BackgroundGradient);
        Assert.True(s.ShowStatusDot);
    }

    [Fact]
    public void AllFieldsSurviveRoundtrip()
    {
        var s = new ReckoningComponentSettings
        {
            Comparison = "Best Segments",
            OverrideTextColor = true,
            TextColor = Color.FromArgb(1, 2, 3),
            OverrideTimeColor = true,
            TimeColor = Color.FromArgb(4, 5, 6),
            BackgroundColor = Color.FromArgb(7, 8, 9),
            BackgroundColor2 = Color.FromArgb(10, 11, 12),
            BackgroundGradient = GradientType.Vertical,
            Accuracy = TimeAccuracy.Hundredths,
            Display2Rows = true,
            ShowStatusDot = false,
        };
        var r = Roundtrip(s);
        Assert.Equal("Best Segments", r.Comparison);
        Assert.True(r.OverrideTextColor);
        Assert.Equal(Color.FromArgb(1, 2, 3).ToArgb(), r.TextColor.ToArgb());
        Assert.True(r.OverrideTimeColor);
        Assert.Equal(Color.FromArgb(4, 5, 6).ToArgb(), r.TimeColor.ToArgb());
        Assert.Equal(Color.FromArgb(7, 8, 9).ToArgb(), r.BackgroundColor.ToArgb());
        Assert.Equal(Color.FromArgb(10, 11, 12).ToArgb(), r.BackgroundColor2.ToArgb());
        Assert.Equal(GradientType.Vertical, r.BackgroundGradient);
        Assert.Equal(TimeAccuracy.Hundredths, r.Accuracy);
        Assert.True(r.Display2Rows);
        Assert.False(r.ShowStatusDot);
    }

    [Fact]
    public void StockXmlKeysAreUsed()
    {
        var doc = new XmlDocument();
        var node = new ReckoningComponentSettings().GetSettings(doc);
        foreach (var key in new[] { "Comparison", "OverrideTextColor", "TextColor",
            "OverrideTimeColor", "TimeColor", "BackgroundColor", "BackgroundColor2",
            "BackgroundGradient", "Accuracy", "Display2Rows", "ShowStatusDot" })
            Assert.NotNull(node[key]);
    }
}
