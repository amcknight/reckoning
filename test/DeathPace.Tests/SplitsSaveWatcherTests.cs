using DeathPace.Persistence;
using Xunit;

namespace DeathPace.Tests;

public class SplitsSaveWatcherTests
{
    [Fact]
    public void FirstEventFires() => Assert.True(SplitsSaveWatcher.ShouldFire(lastFireMs: long.MinValue, nowMs: 0));

    [Fact]
    public void EventInsideSuppressWindowIsSwallowed()
        => Assert.False(SplitsSaveWatcher.ShouldFire(lastFireMs: 1000, nowMs: 1000 + SplitsSaveWatcher.SuppressWindowMs - 1));

    [Fact]
    public void EventAfterSuppressWindowFires()
        => Assert.True(SplitsSaveWatcher.ShouldFire(lastFireMs: 1000, nowMs: 1000 + SplitsSaveWatcher.SuppressWindowMs));
}
