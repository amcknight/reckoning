using System;
using System.Diagnostics;
using System.IO;

namespace LiveSplit.Reckoning.Persistence;

/// <summary>Fires a callback when LiveSplit writes the watched .lss file, so
/// the sidecar persists exactly when the user saves splits — learned data
/// shadows the splits file the same way golds do. One save produces several
/// FileSystemWatcher events; a suppress window coalesces them.</summary>
public sealed class SplitsSaveWatcher : IDisposable
{
    // 500 ms: comfortably wider than the event burst from one file write,
    // far narrower than any two deliberate user saves.
    public const long SuppressWindowMs = 500;

    // Environment.TickCount64 isn't available on net481 (verified: CS0117 —
    // it was added in .NET Core 3.0 and never backported to .NET Framework).
    // Stopwatch.ElapsedMilliseconds is monotonic and framework-safe instead.
    private static readonly Stopwatch clock = Stopwatch.StartNew();

    private readonly Action onSplitsSaved;
    private FileSystemWatcher watcher;
    private long lastFireMs = long.MinValue;

    public SplitsSaveWatcher(Action onSplitsSaved) => this.onSplitsSaved = onSplitsSaved;

    // Written as nowMs >= lastFireMs + SuppressWindowMs, not the algebraically
    // equivalent "nowMs - lastFireMs >= SuppressWindowMs": lastFireMs starts
    // at long.MinValue (never-fired sentinel), and negating/subtracting from
    // MinValue overflows unchecked long arithmetic, wrongly swallowing the
    // very first event. Adding SuppressWindowMs to MinValue never overflows.
    public static bool ShouldFire(long lastFireMs, long nowMs) => nowMs >= lastFireMs + SuppressWindowMs;

    public void WatchPath(string lssPath)
    {
        watcher?.Dispose();
        watcher = null;
        if (string.IsNullOrEmpty(lssPath)) return;
        string dir = Path.GetDirectoryName(lssPath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        watcher = new FileSystemWatcher(dir, Path.GetFileName(lssPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
        };
        // Created/Renamed too: editors and LiveSplit may write via temp+rename.
        watcher.Changed += OnFsEvent;
        watcher.Created += OnFsEvent;
        watcher.Renamed += OnFsEvent;
        watcher.EnableRaisingEvents = true;
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        long now = clock.ElapsedMilliseconds;
        if (!ShouldFire(lastFireMs, now)) return;
        lastFireMs = now;
        try { onSplitsSaved(); }
        catch { /* a failed save must never take down the watcher thread */ }
    }

    public void Dispose()
    {
        watcher?.Dispose();
        watcher = null;
    }
}
