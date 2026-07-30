using System;
using LiveSplit.Reckoning.UI;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class TimeTextTests
{
    [Fact]
    public void NullRendersEmDash()
    {
        Assert.Equal("—", TimeText.Format(null, RowAccuracy.Seconds));
        Assert.Equal("—", TimeText.FormatSunk(null, RowAccuracy.Tenths));
    }

    [Theory]
    [InlineData(83.0, RowAccuracy.Seconds, "1:23")]
    [InlineData(83.46, RowAccuracy.Tenths, "1:23.4")]
    [InlineData(83.46, RowAccuracy.Hundredths, "1:23.46")]
    [InlineData(3723.0, RowAccuracy.Seconds, "1:02:03")]
    [InlineData(3723.5, RowAccuracy.Tenths, "1:02:03.5")]
    [InlineData(7.25, RowAccuracy.Hundredths, "0:07.25")]
    public void FormatsMinutesSecondsHours(double seconds, object accuracy, string expected)
    {
        // Theory methods must be public for xUnit discovery, but the internal
        // RowAccuracy type can't appear directly in a public signature
        // (CS0051) even with InternalsVisibleTo — box through object instead.
        Assert.Equal(expected, TimeText.Format(TimeSpan.FromSeconds(seconds), (RowAccuracy)accuracy));
    }

    [Theory]
    [InlineData(3.42, RowAccuracy.Tenths, "+0:03.4")]
    [InlineData(0.0, RowAccuracy.Tenths, "0:00.0")]
    [InlineData(75.0, RowAccuracy.Seconds, "+1:15")]
    public void SunkGetsPlusPrefixWhenPositive(double seconds, object accuracy, string expected)
    {
        Assert.Equal(expected, TimeText.FormatSunk(TimeSpan.FromSeconds(seconds), (RowAccuracy)accuracy));
    }
}
