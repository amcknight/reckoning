using System;
using DeathPace.Engine;
using Xunit;

namespace DeathPace.Tests;

public class BestsStoreTests
{
    private static TimeSpan Ms(long ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void UnknownKeyHasNoBestAndZeroAttempts()
    {
        var store = new BestsStore();
        Assert.False(store.TryGetBest(0, 0, Variant.Hot, out _));
        Assert.Equal(0, store.GetAttempts(0, 0, Variant.Hot));
    }

    [Fact]
    public void RecordSetsBestAndCountsAttempt()
    {
        var store = new BestsStore();
        store.Record(2, 1, Variant.Cold, Ms(41_500));
        Assert.True(store.TryGetBest(2, 1, Variant.Cold, out var best));
        Assert.Equal(Ms(41_500), best);
        Assert.Equal(1, store.GetAttempts(2, 1, Variant.Cold));
    }

    [Fact]
    public void SlowerObservationKeepsBestButCountsAttempt()
    {
        var store = new BestsStore();
        store.Record(0, 0, Variant.Hot, Ms(30_000));
        store.Record(0, 0, Variant.Hot, Ms(45_000));
        Assert.True(store.TryGetBest(0, 0, Variant.Hot, out var best));
        Assert.Equal(Ms(30_000), best);
        Assert.Equal(2, store.GetAttempts(0, 0, Variant.Hot));
    }

    [Fact]
    public void FasterObservationImprovesBest()
    {
        var store = new BestsStore();
        store.Record(0, 0, Variant.Hot, Ms(30_000));
        store.Record(0, 0, Variant.Hot, Ms(28_250));
        Assert.True(store.TryGetBest(0, 0, Variant.Hot, out var best));
        Assert.Equal(Ms(28_250), best);
    }

    [Fact]
    public void VariantsAreIndependent()
    {
        var store = new BestsStore();
        store.Record(1, 1, Variant.Hot, Ms(20_000));
        store.Record(1, 1, Variant.Cold, Ms(25_000));
        store.TryGetBest(1, 1, Variant.Hot, out var hot);
        store.TryGetBest(1, 1, Variant.Cold, out var cold);
        Assert.Equal(Ms(20_000), hot);
        Assert.Equal(Ms(25_000), cold);
    }

    [Fact]
    public void SetEntryAndRemoveEntryRoundTrip()
    {
        var store = new BestsStore();
        var key = new MarkerKey(3, 2, Variant.Cold);
        store.SetEntry(key, new BestEntry(12_345, 7));
        Assert.True(store.TryGetEntry(key, out var entry));
        Assert.Equal(12_345, entry.BestMs);
        Assert.Equal(7, entry.Attempts);
        store.RemoveEntry(key);
        Assert.False(store.TryGetEntry(key, out _));
        Assert.Empty(store.Keys);
    }
}
