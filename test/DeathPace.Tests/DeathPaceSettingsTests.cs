using System.Drawing;
using System.Xml;
using LiveSplit.TimeFormatters;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using Xunit;

namespace DeathPace.Tests;

public class DeathPaceSettingsTests
{
    private static DeathPaceComponentSettings Roundtrip(DeathPaceComponentSettings s)
    {
        var doc = new XmlDocument();
        var node = s.GetSettings(doc);
        var fresh = new DeathPaceComponentSettings();
        fresh.SetSettings(node);
        return fresh;
    }

    [Fact]
    public void DefaultsMatchStockRunPrediction()
    {
        var s = new DeathPaceComponentSettings();
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
        var s = new DeathPaceComponentSettings
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
        var node = new DeathPaceComponentSettings().GetSettings(doc);
        foreach (var key in new[] { "Comparison", "OverrideTextColor", "TextColor",
            "OverrideTimeColor", "TimeColor", "BackgroundColor", "BackgroundColor2",
            "BackgroundGradient", "Accuracy", "Display2Rows", "ShowStatusDot" })
            Assert.NotNull(node[key]);
    }

    [Fact]
    public void GarbageAccuracyFallsBackToDefault()
    {
        var doc = new XmlDocument();
        var node = new DeathPaceComponentSettings().GetSettings(doc);
        node["Accuracy"].InnerText = "Garbage";
        var fresh = new DeathPaceComponentSettings();
        fresh.SetSettings(node);
        Assert.Equal(TimeAccuracy.Seconds, fresh.Accuracy);
    }

    [Fact]
    public void GarbageBackgroundGradientFallsBackToDefault()
    {
        var doc = new XmlDocument();
        var node = new DeathPaceComponentSettings().GetSettings(doc);
        node["BackgroundGradient"].InnerText = "Garbage";
        var fresh = new DeathPaceComponentSettings();
        fresh.SetSettings(node);
        Assert.Equal(GradientType.Plain, fresh.BackgroundGradient);
    }

    [Fact]
    public void NumericGarbageAccuracyFallsBackToDefault()
    {
        // Enum.TryParse happily parses an in-range numeric string like "7"
        // into an out-of-range TimeAccuracy value (no defined member), even
        // though it isn't a recognized name — must fall back same as
        // "Garbage" rather than sail through as a bogus enum value.
        var doc = new XmlDocument();
        var node = new DeathPaceComponentSettings().GetSettings(doc);
        node["Accuracy"].InnerText = "7";
        var fresh = new DeathPaceComponentSettings();
        fresh.SetSettings(node);
        Assert.Equal(TimeAccuracy.Seconds, fresh.Accuracy);
    }
}
