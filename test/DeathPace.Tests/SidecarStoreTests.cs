using System;
using System.IO;
using System.Linq;
using DeathPace.Engine;
using DeathPace.Persistence;
using Xunit;

namespace DeathPace.Tests;

public class SidecarStoreTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "deathpace-tests-" + Guid.NewGuid().ToString("N"));

    public SidecarStoreTests() => Directory.CreateDirectory(dir);
    public void Dispose() => Directory.Delete(dir, true);

    private string SidecarPath => Path.Combine(dir, "run.lss.deathpace.json");

    private static readonly string[] SegmentNames = { "Yump 1", "Yump 2" };

    private static BestsStore SampleStore()
    {
        var store = new BestsStore();
        store.SetEntry(new MarkerKey(0, 0, Variant.Hot), new BestEntry(51_230, 12));
        store.SetEntry(new MarkerKey(0, 1, Variant.Cold), new BestEntry(22_050, 4));
        store.SetEntry(new MarkerKey(1, 0, Variant.Cold), new BestEntry(63_000, 1));
        return store;
    }

    [Fact]
    public void PathForAppendsSuffix()
    {
        Assert.Equal(@"C:\s\run.lss.deathpace.json", SidecarStore.PathFor(@"C:\s\run.lss"));
    }

    [Fact]
    public void RoundTripPreservesEveryEntry()
    {
        SidecarStore.Save(SidecarPath, SampleStore(), @"C:\s\run.lss", "SMW Kaizo", "Any%", SegmentNames);
        var loaded = SidecarStore.Load(SidecarPath);
        Assert.Equal(3, loaded.Keys.Count);
        Assert.True(loaded.TryGetEntry(new MarkerKey(0, 0, Variant.Hot), out var e));
        Assert.Equal(new BestEntry(51_230, 12), e);
        Assert.True(loaded.TryGetEntry(new MarkerKey(0, 1, Variant.Cold), out e));
        Assert.Equal(new BestEntry(22_050, 4), e);
        Assert.True(loaded.TryGetEntry(new MarkerKey(1, 0, Variant.Cold), out e));
        Assert.Equal(new BestEntry(63_000, 1), e);
    }

    [Fact]
    public void MissingFileLoadsEmpty()
    {
        var loaded = SidecarStore.Load(SidecarPath);
        Assert.Empty(loaded.Keys);
    }

    [Fact]
    public void CorruptJsonLoadsEmpty()
    {
        File.WriteAllText(SidecarPath, "{ this is not json");
        Assert.Empty(SidecarStore.Load(SidecarPath).Keys);
    }

    [Fact]
    public void WrongShapeLoadsEmpty()
    {
        File.WriteAllText(SidecarPath, "{ \"version\": 1, \"segments\": \"nope\" }");
        Assert.Empty(SidecarStore.Load(SidecarPath).Keys);
    }

    [Fact]
    public void UnknownVariantEntriesAreSkippedNotFatal()
    {
        SidecarStore.Save(SidecarPath, SampleStore(), @"C:\s\run.lss", "g", "c", SegmentNames);
        var text = File.ReadAllText(SidecarPath).Replace("\"cold\"", "\"tepid\"");
        File.WriteAllText(SidecarPath, text);
        var loaded = SidecarStore.Load(SidecarPath);
        Assert.True(loaded.TryGetEntry(new MarkerKey(0, 0, Variant.Hot), out _));   // hot survives
        Assert.False(loaded.TryGetEntry(new MarkerKey(0, 1, Variant.Cold), out _)); // tepid skipped
    }

    [Fact]
    public void SaveOverwritesAtomicallyLeavingNoTempFile()
    {
        SidecarStore.Save(SidecarPath, SampleStore(), @"C:\s\run.lss", "g", "c", SegmentNames);
        SidecarStore.Save(SidecarPath, SampleStore(), @"C:\s\run.lss", "g", "c", SegmentNames);
        Assert.Single(Directory.GetFiles(dir));   // no stray .tmp
        Assert.NotEmpty(SidecarStore.Load(SidecarPath).Keys);
    }

    [Fact]
    public void SaveRecordsIdentityAndSegmentNames()
    {
        SidecarStore.Save(SidecarPath, SampleStore(), @"C:\s\run.lss", "SMW Kaizo", "Any%", SegmentNames);
        var text = File.ReadAllText(SidecarPath);
        Assert.Contains("\"SMW Kaizo\"", text);
        Assert.Contains("\"Any%\"", text);
        Assert.Contains("\"Yump 2\"", text);
        Assert.Contains("\"version\":1", text.Replace(" ", ""));
    }
}
