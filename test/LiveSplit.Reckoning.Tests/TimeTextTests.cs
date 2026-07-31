using System;
using LiveSplit.Reckoning.UI;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class TimeTextTests
{
    [Fact]
    public void FormatHitUnderAMinuteIsSecondsWithTenths()
        => Assert.Equal("-22.4", TimeText.FormatHit(TimeSpan.FromSeconds(22.45)));

    [Fact]
    public void FormatHitOverAMinuteIncludesMinutes()
        => Assert.Equal("-1:02.4", TimeText.FormatHit(TimeSpan.FromSeconds(62.45)));

    [Fact]
    public void FormatHitZeroIsStillNegativeByConvention()
        => Assert.Equal("-0.0", TimeText.FormatHit(TimeSpan.Zero));
}
