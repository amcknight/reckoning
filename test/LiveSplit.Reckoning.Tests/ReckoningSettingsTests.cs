using System.Xml;
using LiveSplit.Reckoning.UI;
using LiveSplit.UI.Components;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class ReckoningSettingsTests
{
    [Fact]
    public void DefaultsAreSpecCompliant()
    {
        using var s = new ReckoningComponentSettings();
        Assert.True(s.ShowSunkRow);
        Assert.True(s.ShowStatusDot);
        Assert.Equal(RowAccuracy.Tenths, s.Accuracy);
    }

    [Fact]
    public void SettingsRoundTripThroughXml()
    {
        using var a = new ReckoningComponentSettings
        {
            ShowSunkRow = false,
            ShowStatusDot = false,
            Accuracy = RowAccuracy.Hundredths,
        };
        var doc = new XmlDocument();
        var node = a.GetSettings(doc);

        using var b = new ReckoningComponentSettings();
        b.SetSettings(node);
        Assert.False(b.ShowSunkRow);
        Assert.False(b.ShowStatusDot);
        Assert.Equal(RowAccuracy.Hundredths, b.Accuracy);
    }

    [Fact]
    public void GarbageAccuracyFallsBackToDefault()
    {
        using var a = new ReckoningComponentSettings();
        var doc = new XmlDocument();
        var node = a.GetSettings(doc);
        node.SelectSingleNode("Accuracy").InnerText = "Nanoseconds";
        using var b = new ReckoningComponentSettings();
        b.SetSettings(node);
        Assert.Equal(RowAccuracy.Tenths, b.Accuracy);
    }

    [Fact]
    public void HashChangesWhenASettingChanges()
    {
        using var a = new ReckoningComponentSettings();
        int before = a.GetSettingsHashCode();
        a.ShowSunkRow = false;
        Assert.NotEqual(before, a.GetSettingsHashCode());
    }
}
