using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using LiveSplit.Model;
using LiveSplit.Reckoning.Engine;
using LiveSplit.Reckoning.Persistence;
using LiveSplit.Reckoning.Snes;
using LiveSplit.Reckoning.UI;
using LiveSplit.Reckoning.Watchers;
using LiveSplit.TimeFormatters;
using LiveSplit.UI;

namespace LiveSplit.UI.Components;

public class ReckoningComponent : IComponent
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

    private BestsStore store = new();
    private ReckoningModel model;
    private string loadedLssPath;
    private int lastGeneration = -1;
    private ComposedPrediction lastComposed;
    private bool lastUnlearned;
    private string previousInformationName;

    public ReckoningComponentSettings Settings { get; } = new();

    public ReckoningComponent(LiveSplitState state)
    {
        this.state = state;
        model = new ReckoningModel(store);
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

    public string ComponentName => ComparisonNaming.GetDisplayedName(Settings.Comparison);
    public float VerticalHeight => internalComponent.VerticalHeight;
    public float MinimumHeight => internalComponent.MinimumHeight;
    // Widened by the dot gutter when the dot is shown, so the layout engine
    // reserves enough horizontal room and the internal component's own
    // content is never squeezed into (or under) the dot.
    public float HorizontalWidth => internalComponent.HorizontalWidth + (Settings.ShowStatusDot ? DotGutterPx : 0f);
    public float MinimumWidth => internalComponent.MinimumWidth + (Settings.ShowStatusDot ? DotGutterPx : 0f);
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

        if (tick.Death) { hit.OnDeath(lastComposed.Sunk); model.OnDeath(); }
        if (tick.Checkpoint) model.OnCheckpoint(elapsed);
        if (tick.Respawn) { hit.OnRespawn(clock.ElapsedMilliseconds); model.OnRespawn(elapsed); }
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
        loadedLssPath = lss;
        store = string.IsNullOrEmpty(lss) ? new BestsStore() : SidecarStore.Load(SidecarStore.PathFor(lss));
        model = new ReckoningModel(store);
    }

    private void SaveSidecar()
    {
        string lss = loadedLssPath;
        if (string.IsNullOrEmpty(lss)) return;
        try
        {
            SidecarStore.Save(SidecarStore.PathFor(lss), store, lss,
                state.Run.GameName, state.Run.CategoryName,
                state.Run.Select(seg => seg.Name).ToList());
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
        if (Elapsed() is TimeSpan t) model.OnSplit(t);
        SaveSidecar();
    }

    private void OnUndoSplit(object sender, EventArgs e)
    {
        hit.Clear();
        model.OnUndoSplit(Elapsed() ?? TimeSpan.Zero);
    }

    private void OnSkipSplit(object sender, EventArgs e) => model.OnSkipSplit(Elapsed() ?? TimeSpan.Zero);

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

        return model.Compute(elapsed, segmentStart, state.Run[index].BestSegmentTime[method]);
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
        lastComposed = default;
        lastUnlearned = false;

        if (internalComponent.InformationName.StartsWith("Current Pace") && state.CurrentPhase == TimerPhase.NotRunning)
        {
            internalComponent.TimeValue = null;
        }
        else if (state.CurrentPhase is TimerPhase.Running or TimerPhase.Paused
                 && state.CurrentSplitIndex >= 0 && state.CurrentSplitIndex < state.Run.Count
                 && state.CurrentTime[method] is TimeSpan elapsed)
        {
            var prediction = model.IsRunning ? ComputePrediction(state, method, elapsed) : null;
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

        hit.Update(lastComposed.Sunk, clock.ElapsedMilliseconds);

        cache.Restart();
        cache["dot"] = Settings.ShowStatusDot ? connection.DotColor.ToArgb() : 0;
        cache["unlearned"] = lastUnlearned;
        cache["hitAmount"] = hit.Visible ? hit.Amount.Ticks : 0L;
        // Bucketed so the fade repaints smoothly without invalidating every tick.
        cache["hitAlpha"] = hit.Alpha(clock.ElapsedMilliseconds) / 16;
        if (cache.HasChanged) invalidator?.Invalidate(0, 0, width, height);

        internalComponent.Update(invalidator, state, width, height, mode);
    }

    private void PrepareDraw(LiveSplitState state, LayoutMode mode)
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

    private void DrawOverlays(Graphics g, LiveSplitState state, float width, float height)
    {
        int alpha = hit.Alpha(clock.ElapsedMilliseconds);
        if (hit.Visible && alpha > 0)
        {
            float valueWidth = g.MeasureString(internalComponent.InformationValue ?? "", state.LayoutSettings.TimesFont).Width;
            hitLabel.Text = TimeText.FormatHit(hit.Amount);
            hitLabel.Font = state.LayoutSettings.TimesFont;
            hitLabel.ForeColor = Color.FromArgb(alpha, HitColor);
            hitLabel.HasShadow = state.LayoutSettings.DropShadows;
            hitLabel.ShadowColor = state.LayoutSettings.ShadowsColor;
            hitLabel.HorizontalAlignment = StringAlignment.Far;
            hitLabel.VerticalAlignment = StringAlignment.Center;
            hitLabel.X = 0;
            hitLabel.Y = 0;
            hitLabel.Width = width - valueWidth - HitGapPx - 12;   // 12: InfoTextComponent's own value-label right inset
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
        PrepareDraw(state, LayoutMode.Vertical);
        if (Settings.ShowStatusDot)
        {
            // Inset the internal component's whole draw into the gutter so
            // its (hard-coded) name-label origin lands to the right of the
            // dot instead of under it; the dot itself is drawn afterward, at
            // full (untranslated) coordinates, in DrawOverlays.
            var savedTransform = g.Save();
            g.TranslateTransform(DotGutterPx, 0);
            internalComponent.DrawVertical(g, state, Math.Max(0f, width - DotGutterPx), clipRegion);
            g.Restore(savedTransform);
        }
        else
        {
            internalComponent.DrawVertical(g, state, width, clipRegion);
        }

        DrawOverlays(g, state, width, VerticalHeight);
    }

    public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion)
    {
        DrawBackground(g, state, HorizontalWidth, height);
        PrepareDraw(state, LayoutMode.Horizontal);
        if (Settings.ShowStatusDot)
        {
            // Same gutter inset as DrawVertical; HorizontalWidth is already
            // widened by DotGutterPx above, so the internal component's own
            // (unchanged) intrinsic width plus this translate exactly fills
            // the advertised row width.
            var savedTransform = g.Save();
            g.TranslateTransform(DotGutterPx, 0);
            internalComponent.DrawHorizontal(g, state, height, clipRegion);
            g.Restore(savedTransform);
        }
        else
        {
            internalComponent.DrawHorizontal(g, state, height, clipRegion);
        }

        DrawOverlays(g, state, HorizontalWidth, height);
    }

    public Control GetSettingsControl(LayoutMode mode) => Settings;
    public XmlNode GetSettings(XmlDocument document) => Settings.GetSettings(document);
    public void SetSettings(XmlNode settings) => Settings.SetSettings(settings);
    public int GetSettingsHashCode() => Settings.GetSettingsHashCode();

    public void Dispose()
    {
        SaveSidecar();   // spec: persist on LiveSplit shutdown too
        pollTimer.Dispose();
        state.OnStart -= OnStart;
        state.OnSplit -= OnSplit;
        state.OnUndoSplit -= OnUndoSplit;
        state.OnSkipSplit -= OnSkipSplit;
        state.OnReset -= OnReset;
        state.ComparisonRenamed -= OnComparisonRenamed;
    }
}
