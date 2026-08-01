using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using LiveSplit.Model;
using DeathPace.Engine;
using DeathPace.Persistence;
using DeathPace.Snes;
using DeathPace.UI;
using DeathPace.Watchers;
using LiveSplit.TimeFormatters;
using LiveSplit.UI;

namespace LiveSplit.UI.Components;

public class DeathPaceComponent : IComponent
{
    // 15 ms poll (SMWCounters cadence): under one 60 fps frame, so no
    // death/checkpoint edge can slip between polls.
    private const int PollIntervalMs = 15;
    // 5x5 px matches SMWCounters' proven status-pixel size — small enough to
    // be unobtrusive, large enough to read color at a glance.
    private const float StatusDotSizePx = 5f;
    // Matches SMWCounters' fixed dot position: pinned near the component's
    // left edge.
    private const float StatusDotLeftPx = 3f;
    // Width reserved on the left for the status dot when shown: the dot's own
    // footprint plus a small gap. InfoTextComponent hard-codes its
    // NameLabel.X at 5 in both the single-row and two-row draw paths and
    // never applies our PaddingLeft as an X offset, so without this gutter
    // the dot collides with the start of the name text.
    private const float DotGutterPx = StatusDotLeftPx + StatusDotSizePx + 2f;   // 2f: breathing room before the name text starts
    // Unlearned values render in a fixed dim gray: legible on light and dark
    // layouts where alpha-dimming vanished into dark backgrounds (live-test 1).
    private static readonly Color UnlearnedColor = Color.Gray;
    // Damage red, matching LiveSplit's default "behind, losing time" red so the
    // hit reads instantly as lost time.
    private static readonly Color HitColor = Color.FromArgb(255, 51, 51);
    // Gap between the hit number and the value text; one character-ish at
    // default fonts, so the hit reads as a separate transient, not a prefix.
    private const float HitGapPx = 8f;

    private readonly LiveSplitState state;
    private readonly SnesConnection connection = new();
    private readonly SmwEventDetector detector = new();
    private readonly Timer pollTimer;
    private readonly GraphicsCache cache = new();
    private readonly InfoTimeComponent internalComponent;
    private readonly SplitTimeFormatter formatter;
    private readonly DamageHit hit = new();
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly SimpleLabel hitLabel = new();

    private readonly SplitsSaveWatcher saveWatcher;
    // Guards store/model against a torn read/write: SaveSidecar can now run
    // on the watcher's threadpool thread while ReloadSidecarIfPathChanged and
    // the split handlers mutate the same fields on the UI thread.
    private readonly object storeLock = new();

    private BestsStore store = new();
    private DeathPaceModel model;
    private string loadedLssPath;
    private int lastGeneration = -1;
    private ComposedPrediction lastComposed;
    private bool lastUnlearned;
    private string previousInformationName;
    // The hit's baseline (DamageHit.OnDeath's valueNow) is a value computed
    // relative to whichever comparison/timing method was active at arm time;
    // a mid-hit switch of either changes what "the value" even means and
    // would present as a fake instantaneous jump in the drawn amount, so we
    // track the pair here and clear the hit on any change.
    private string lastHitComparison;
    private TimingMethod lastHitTimingMethod;

    // Wall-clock (not Stopwatch) so both are directly comparable against
    // File.GetLastWriteTimeUtc in Dispose's exit-save race fix below.
    private readonly DateTime componentStartUtc = DateTime.UtcNow;
    private DateTime lastSidecarSaveUtc = DateTime.MinValue;

    public DeathPaceComponentSettings Settings { get; } = new();

    public DeathPaceComponent(LiveSplitState state)
    {
        this.state = state;
        model = new DeathPaceModel(store);
        saveWatcher = new SplitsSaveWatcher(SaveSidecar);
        formatter = new SplitTimeFormatter(Settings.Accuracy);
        internalComponent = new InfoTimeComponent(null, null, formatter);
        Settings.CurrentState = state;

        state.OnStart += OnStart;
        state.OnSplit += OnSplit;
        state.OnUndoSplit += OnUndoSplit;
        state.OnSkipSplit += OnSkipSplit;
        state.OnReset += OnReset;
        // Ported from LiveSplit's RunPrediction component (MIT).
        state.ComparisonRenamed += OnComparisonRenamed;

        pollTimer = new Timer { Interval = PollIntervalMs };
        pollTimer.Tick += (_, _) => Poll();
        pollTimer.Enabled = true;
    }

    // Ported from LiveSplit's RunPrediction component (MIT).
    private void OnComparisonRenamed(object sender, EventArgs e)
    {
        var args = (RenameEventArgs)e;
        if (Settings.Comparison == args.OldName)
        {
            Settings.Comparison = args.NewName;
            ((LiveSplitState)sender).Layout.HasChanged = true;
        }
    }

    // Deviation from stock (which shows the comparison name here): the settings
    // tab in the layout editor must be findable under one stable name, so this
    // stays "SMW Death Pace" regardless of comparison. The on-layout row label still
    // follows the comparison via InformationName, set each frame in Update().
    public string ComponentName => "DeathPace";
    public float VerticalHeight => internalComponent.VerticalHeight;
    public float MinimumHeight => internalComponent.MinimumHeight;
    // Widened by the dot gutter when the dot is shown, so the layout engine
    // reserves enough horizontal room and the internal component's own
    // content is never squeezed into (or under) the dot.
    public float HorizontalWidth => internalComponent.HorizontalWidth + DotGutter;
    public float MinimumWidth => internalComponent.MinimumWidth + DotGutter;
    // Width actually reserved this frame: DotGutterPx when the dot is shown,
    // zero otherwise. Shared by the width properties above and by DrawInset.
    private float DotGutter => Settings.ShowStatusDot ? DotGutterPx : 0f;
    public float PaddingTop => internalComponent.PaddingTop;
    public float PaddingBottom => internalComponent.PaddingBottom;
    public float PaddingLeft => internalComponent.PaddingLeft;
    public float PaddingRight => internalComponent.PaddingRight;
    public IDictionary<string, Action> ContextMenuControls => null;

    private TimeSpan? Elapsed() => state.CurrentTime[state.CurrentTimingMethod];

    private void PollCore()
    {
        connection.Tick();
        if (connection.Generation != lastGeneration)
        {
            lastGeneration = connection.Generation;
            detector.Reset();   // rebind: never edge across a base change
        }

        bool timerActive = state.CurrentPhase == TimerPhase.Running || state.CurrentPhase == TimerPhase.Paused;
        var tick = detector.Poll(connection);
        if (!timerActive || !model.IsRunning) return;
        if (Elapsed() is not TimeSpan elapsed) return;

        if (tick.Death) { hit.OnDeath(lastComposed.Value); model.OnDeath(); }
        if (tick.Checkpoint) model.OnCheckpoint(elapsed);
        if (tick.Respawn) { hit.OnRespawn(); model.OnRespawn(elapsed); }
    }

    private void Poll()
    {
        try { PollCore(); }
        catch
        {
            // Never let a poll fault escape into LiveSplit's UI thread; next
            // tick retries and the status dot shows degraded state.
        }
    }

    private void ReloadSidecarIfPathChanged()
    {
        string lss = state.Run.FilePath;
        if (lss == loadedLssPath) return;
        // Disk load happens outside the lock (nothing published yet to race
        // on); loadedLssPath and store/model are then swapped together under
        // the lock so a concurrent watcher-thread SaveSidecar can never read
        // one half-updated (e.g. the new path paired with the old store).
        var loaded = string.IsNullOrEmpty(lss) ? new BestsStore() : SidecarStore.Load(SidecarStore.PathFor(lss));
        lock (storeLock)
        {
            loadedLssPath = lss;
            store = loaded;
            model = new DeathPaceModel(store);
        }
        saveWatcher.WatchPath(lss);
    }

    private void SaveSidecar()
    {
        try
        {
            lock (storeLock)
            {
                string lss = loadedLssPath;
                if (string.IsNullOrEmpty(lss)) return;
                SidecarStore.Save(SidecarStore.PathFor(lss), store, lss,
                    state.Run.GameName, state.Run.CategoryName,
                    state.Run.Select(seg => seg.Name).ToList());
                lastSidecarSaveUtc = DateTime.UtcNow;
            }
        }
        catch
        {
            // A failed save must never take down LiveSplit; next split retries.
        }
    }

    private void OnStart(object sender, EventArgs e)
    {
        detector.Reset();
        hit.Clear();
        model.OnStart(Elapsed() ?? TimeSpan.Zero);
    }

    private void OnSplit(object sender, EventArgs e)
    {
        hit.Clear();
        if (Elapsed() is TimeSpan t) lock (storeLock) { model.OnSplit(t); }
    }

    private void OnUndoSplit(object sender, EventArgs e)
    {
        hit.Clear();
        lock (storeLock) { model.OnUndoSplit(Elapsed() ?? TimeSpan.Zero); }
    }

    private void OnSkipSplit(object sender, EventArgs e)
    {
        hit.Clear();
        model.OnSkipSplit(Elapsed() ?? TimeSpan.Zero);
    }

    private void OnReset(object sender, TimerPhase phase)
    {
        hit.Clear();
        model.OnReset();
    }

    private SituationPrediction ComputePrediction(LiveSplitState state, TimingMethod method, TimeSpan elapsed)
    {
        int index = state.CurrentSplitIndex;
        // Segment start = last non-null earlier split time (skips leave nulls).
        TimeSpan segmentStart = TimeSpan.Zero;
        for (int i = index - 1; i >= 0; i--)
        {
            if (state.Run[i].SplitTime[method] is TimeSpan st) { segmentStart = st; break; }
        }

        // Compute reads store entries (via the markerBest lambda) on this UI
        // thread; SaveSidecar enumerates the same store under storeLock on
        // the watcher thread. Lock here too so store access is never read
        // outside stated lock discipline, not merely safe by Dictionary's
        // tolerance of concurrent readers with no concurrent writer.
        lock (storeLock)
        {
            return model.Compute(elapsed, segmentStart, state.Run[index].BestSegmentTime[method]);
        }
    }

    public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        ReloadSidecarIfPathChanged();

        // Ported from LiveSplit's RunPrediction component (MIT).
        string comparison = Settings.Comparison == "Current Comparison" ? state.CurrentComparison : Settings.Comparison;
        if (!state.Run.Comparisons.Contains(comparison)) comparison = state.CurrentComparison;

        internalComponent.InformationName = internalComponent.LongestString = ComparisonNaming.GetDisplayedName(comparison);
        if (internalComponent.InformationName != previousInformationName)
        {
            internalComponent.AlternateNameText = ComparisonNaming.GetAbbreviations(comparison);
            previousInformationName = internalComponent.InformationName;
        }

        var method = state.CurrentTimingMethod;
        // Baseline is comparison/method-relative (see field comment above) —
        // clear before computing so a switch mid-hit never leaks a fake jump.
        if (comparison != lastHitComparison || method != lastHitTimingMethod) hit.Clear();
        lastHitComparison = comparison;
        lastHitTimingMethod = method;

        lastComposed = default;
        lastUnlearned = false;

        if (ComparisonNaming.IsPaceLike(comparison) && state.CurrentPhase == TimerPhase.NotRunning)
        {
            internalComponent.TimeValue = null;
        }
        else if (state.CurrentPhase is TimerPhase.Running or TimerPhase.Paused
                 && state.CurrentSplitIndex >= 0 && state.CurrentSplitIndex < state.Run.Count)
        {
            // Stock parity: stock RunPrediction stays in this Running/Paused
            // branch even when CurrentTime[method] is null (e.g. game time
            // with no game-time signal yet) — it just drops the live term and
            // lets the locked delta survive. A CurrentTime[method]-guarded
            // branch condition would instead fall through to the bare
            // comparison-final else below, breaking parity. There is no
            // death-aware prediction to compute without an elapsed to anchor
            // it against, so ComputePrediction is skipped entirely.
            TimeSpan? elapsed = state.CurrentTime[method];
            var prediction = model.IsRunning && elapsed is TimeSpan e ? ComputePrediction(state, method, e) : null;
            lastUnlearned = prediction?.Unlearned ?? false;
            lastComposed = PredictionMath.Compose(
                LiveSplitStateHelper.GetLastDelta(state, state.CurrentSplitIndex, comparison, method),
                elapsed,
                state.CurrentSplit.Comparisons[comparison][method],
                state.Run.Last().Comparisons[comparison][method],
                prediction?.Finish);
            internalComponent.TimeValue = lastComposed.Value;
        }
        else if (state.CurrentPhase == TimerPhase.Ended)
        {
            internalComponent.TimeValue = state.Run.Last().SplitTime[method];
        }
        else
        {
            internalComponent.TimeValue = state.Run.Last().Comparisons[comparison][method];
        }

        hit.Update(lastComposed.Value, clock.ElapsedMilliseconds);

        cache.Restart();
        cache["dot"] = Settings.ShowStatusDot ? connection.DotColor.ToArgb() : 0;
        cache["unlearned"] = lastUnlearned;
        cache["hitAmount"] = hit.Visible ? hit.Amount.Ticks : 0L;
        // Bucketed so the fade repaints smoothly without invalidating every tick.
        cache["hitAlpha"] = hit.Alpha(clock.ElapsedMilliseconds) / 16;
        if (cache.HasChanged) invalidator?.Invalidate(0, 0, width, height);

        internalComponent.Update(invalidator, state, width, height, mode);
    }

    private void PrepareDraw(LiveSplitState state)
    {
        // Ported from LiveSplit's RunPrediction component (MIT).
        internalComponent.DisplayTwoRows = Settings.Display2Rows;
        internalComponent.NameLabel.HasShadow = internalComponent.ValueLabel.HasShadow = state.LayoutSettings.DropShadows;
        formatter.Accuracy = Settings.Accuracy;
        internalComponent.NameLabel.ForeColor = Settings.OverrideTextColor ? Settings.TextColor : state.LayoutSettings.TextColor;
        var valueColor = Settings.OverrideTimeColor ? Settings.TimeColor : state.LayoutSettings.TextColor;
        internalComponent.ValueLabel.ForeColor = lastUnlearned ? UnlearnedColor : valueColor;
    }

    // Ported from LiveSplit's RunPrediction component (MIT).
    private void DrawBackground(Graphics g, LiveSplitState state, float width, float height)
    {
        if (Settings.BackgroundColor.A > 0
            || (Settings.BackgroundGradient != GradientType.Plain
            && Settings.BackgroundColor2.A > 0))
        {
            var gradientBrush = new LinearGradientBrush(
                        new PointF(0, 0),
                        Settings.BackgroundGradient == GradientType.Horizontal
                        ? new PointF(width, 0)
                        : new PointF(0, height),
                        Settings.BackgroundColor,
                        Settings.BackgroundGradient == GradientType.Plain
                        ? Settings.BackgroundColor
                        : Settings.BackgroundColor2);
            g.FillRectangle(gradientBrush, 0, 0, width, height);
        }
    }

    // Draws the internal component, inset into the dot gutter when the dot is
    // shown, so its (hard-coded) name-label origin lands to the right of the
    // dot instead of under it; the dot itself is drawn afterward, at full
    // (untranslated) coordinates, in DrawOverlays.
    private void DrawInset(Graphics g, Action<float> draw)
    {
        if (!Settings.ShowStatusDot) { draw(0f); return; }
        var saved = g.Save();
        // Must restore even if the internal draw throws, or every subsequent
        // frame (including DrawOverlays below) inherits a stale translated
        // origin instead of the untranslated one it expects.
        try { g.TranslateTransform(DotGutterPx, 0); draw(DotGutterPx); }
        finally { g.Restore(saved); }
    }

    private void DrawOverlays(Graphics g, LiveSplitState state, float width, float height)
    {
        int alpha = hit.Alpha(clock.ElapsedMilliseconds);
        // hit.Amount == Zero happens on fresh splits and on deaths the model
        // fully absorbed (no time actually lost) — a red "-0.0" there reads
        // as a phantom loss, so suppress the label rather than draw a
        // meaningless zero.
        if (hit.Visible && alpha > 0 && hit.Amount != TimeSpan.Zero)
        {
            // ValueLabel.ActualWidth (not a fresh g.MeasureString of its text)
            // because it's monospace-aware — InfoTimeComponent.PrepareDraw sets
            // ValueLabel.IsMonospaced, under which every digit occupies the '0'
            // glyph's width, so ActualWidth stays stable tick to tick instead of
            // drifting with which digits happen to be showing. Proportional
            // measurement here would reopen the same jitter this fix removes
            // from the hit label itself, just one frame removed.
            float valueWidth = internalComponent.ValueLabel.ActualWidth;
            hitLabel.Text = TimeText.FormatHit(hit.Amount);
            hitLabel.Font = state.LayoutSettings.TimesFont;
            // Matches InfoTimeComponent.ValueLabel: fixed per-digit glyph width
            // ('0''s width) instead of proportional, so the ticking damage
            // number doesn't jitter horizontally as its digits change every
            // frame — the same reason LiveSplit's own time displays set this.
            hitLabel.IsMonospaced = true;
            hitLabel.ForeColor = Color.FromArgb(alpha, HitColor);
            hitLabel.HasShadow = state.LayoutSettings.DropShadows;
            hitLabel.ShadowColor = state.LayoutSettings.ShadowsColor;
            hitLabel.HorizontalAlignment = StringAlignment.Far;
            hitLabel.VerticalAlignment = StringAlignment.Center;
            hitLabel.X = 0;
            hitLabel.Y = 0;
            // Clamped to 0: a narrow component (long value text, or a
            // shrunk layout row) can otherwise drive this negative, which
            // SimpleLabel/StringFormat has no defined behavior for.
            hitLabel.Width = Math.Max(0f, width - valueWidth - HitGapPx - 12);   // 12: InfoTextComponent's own value-label right inset
            hitLabel.Height = height;
            hitLabel.Draw(g);
        }

        if (Settings.ShowStatusDot)
        {
            using var dotBrush = new SolidBrush(connection.DotColor);
            g.FillRectangle(dotBrush, StatusDotLeftPx, (height - StatusDotSizePx) / 2f, StatusDotSizePx, StatusDotSizePx);
        }
    }

    public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion)
    {
        DrawBackground(g, state, width, VerticalHeight);
        PrepareDraw(state);
        DrawInset(g, inset => internalComponent.DrawVertical(g, state, Math.Max(0f, width - inset), clipRegion));
        DrawOverlays(g, state, width, VerticalHeight);
    }

    public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion)
    {
        DrawBackground(g, state, HorizontalWidth, height);
        PrepareDraw(state);
        // HorizontalWidth is already widened by DotGutter above, so the
        // internal component's own (unchanged) intrinsic width plus this
        // inset exactly fills the advertised row width.
        DrawInset(g, _ => internalComponent.DrawHorizontal(g, state, height, clipRegion));
        DrawOverlays(g, state, HorizontalWidth, height);
    }

    public Control GetSettingsControl(LayoutMode mode) => Settings;
    public XmlNode GetSettings(XmlDocument document) => Settings.GetSettings(document);
    public void SetSettings(XmlNode settings) => Settings.SetSettings(settings);
    public int GetSettingsHashCode() => Settings.GetSettingsHashCode();

    public void Dispose()
    {
        // Exit-save race fix: LiveSplit's exit flow writes the .lss then
        // immediately disposes components, racing the FileSystemWatcher's
        // threadpool callback — the watcher may never get to run before the
        // process tears down, silently losing the session's learning. If the
        // splits file's mtime is newer than both this component's start and
        // our last completed sidecar save, the save-gated watcher plainly
        // hasn't caught up yet, so save synchronously here. Wall-clock
        // (DateTime.UtcNow) rather than Stopwatch ticks: only wall-clock is
        // comparable against a file mtime. If the .lss was never (re)written
        // since start, this correctly stays silent: closing WITHOUT saving
        // splits must still discard the session's learning, exactly like an
        // unsaved gold.
        try
        {
            if (!string.IsNullOrEmpty(loadedLssPath))
            {
                DateTime lssWriteUtc = File.GetLastWriteTimeUtc(loadedLssPath);
                if (lssWriteUtc > componentStartUtc && lssWriteUtc > lastSidecarSaveUtc)
                    SaveSidecar();
            }
        }
        catch
        {
            // A failed shutdown-time save must never block LiveSplit's exit.
        }

        saveWatcher.Dispose();
        pollTimer.Dispose();
        state.OnStart -= OnStart;
        state.OnSplit -= OnSplit;
        state.OnUndoSplit -= OnUndoSplit;
        state.OnSkipSplit -= OnSkipSplit;
        state.OnReset -= OnReset;
        state.ComparisonRenamed -= OnComparisonRenamed;
    }
}
