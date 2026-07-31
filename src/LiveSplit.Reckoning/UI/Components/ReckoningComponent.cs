using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using LiveSplit.Model;
using LiveSplit.Reckoning.Engine;
using LiveSplit.Reckoning.Persistence;
using LiveSplit.Reckoning.Snes;
using LiveSplit.Reckoning.UI;
using LiveSplit.Reckoning.Watchers;
using LiveSplit.UI;

namespace LiveSplit.UI.Components;

public class ReckoningComponent : IComponent
{
    // 15 ms poll (SMWCounters cadence): under one 60 fps frame, so no
    // death/checkpoint edge can slip between polls.
    private const int PollIntervalMs = 15;
    // Half opacity for unlearned values: clearly dimmed, still legible on
    // light and dark layouts (spec: subtle visual flag).
    private const int UnlearnedValueAlpha = 128;
    // Matches LiveSplit's InfoTextComponent default row height.
    private const float RowHeightPx = 25f;
    // 5x5 px matches SMWCounters' proven status-pixel size — small enough to
    // be unobtrusive, large enough to read color at a glance.
    private const float StatusDotSizePx = 5f;
    // Matches SMWCounters' fixed dot position: pinned near the component's
    // left edge, clear of the row padding so text never overlaps it.
    private const float StatusDotLeftPx = 3f;
    // Horizontal layout mode: room for label + "1:02:03.45" value at default
    // fonts; the minimum keeps both legible before ellipsis truncation.
    private const float HorizontalWidthPx = 220f;
    private const float MinimumWidthPx = 120f;
    // 7f per side matches LiveSplit's InfoTextComponent intrinsic padding, so
    // the rows align with stock info components in the same layout.
    private const float SidePaddingPx = 7f;

    private readonly LiveSplitState state;
    private readonly SnesConnection connection = new();
    private readonly SmwEventDetector detector = new();
    private readonly Timer pollTimer;
    private readonly GraphicsCache cache = new();
    private readonly SimpleLabel[] nameLabels = { new(), new() };
    private readonly SimpleLabel[] valueLabels = { new(), new() };

    private BestsStore store = new();
    private ReckoningModel model;
    private string loadedLssPath;
    private int lastGeneration = -1;
    private SituationPrediction lastResult;

    public ReckoningComponentSettings Settings { get; } = new();

    public ReckoningComponent(LiveSplitState state)
    {
        this.state = state;
        model = new ReckoningModel(store);
        state.OnStart += OnStart;
        state.OnSplit += OnSplit;
        state.OnUndoSplit += OnUndoSplit;
        state.OnSkipSplit += OnSkipSplit;
        state.OnReset += OnReset;
        pollTimer = new Timer { Interval = PollIntervalMs };
        pollTimer.Tick += (_, _) => Poll();
        pollTimer.Enabled = true;
    }

    public string ComponentName => "Reckoning";
    // Temporary bridge (Task 5 dropped ShowSunkRow; Task 6 rewrites this
    // against the stock Run Prediction layout): single row for now.
    public float VerticalHeight => false ? RowHeightPx * 2 : RowHeightPx;
    public float MinimumHeight => VerticalHeight;
    public float HorizontalWidth => HorizontalWidthPx;
    public float MinimumWidth => MinimumWidthPx;
    public float PaddingTop => 0f;
    public float PaddingBottom => 0f;
    public float PaddingLeft => SidePaddingPx;
    public float PaddingRight => SidePaddingPx;
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

        if (tick.Death) model.OnDeath();
        if (tick.Checkpoint) model.OnCheckpoint(elapsed);
        if (tick.Respawn) model.OnRespawn(elapsed);
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
        model.OnStart(Elapsed() ?? TimeSpan.Zero);
    }

    private void OnSplit(object sender, EventArgs e)
    {
        if (Elapsed() is TimeSpan t) model.OnSplit(t);
        SaveSidecar();
    }

    private void OnUndoSplit(object sender, EventArgs e) => model.OnUndoSplit(Elapsed() ?? TimeSpan.Zero);
    private void OnSkipSplit(object sender, EventArgs e) => model.OnSkipSplit(Elapsed() ?? TimeSpan.Zero);
    private void OnReset(object sender, TimerPhase phase) => model.OnReset();

    private SituationPrediction ComputeNow()
    {
        // Upper bound mirrors LiveSplit's own CurrentSplit accessor: after the
        // final split, CurrentSplitIndex == Run.Count (phase Ended) while the
        // model still reads as running — indexing Run there would throw on
        // every redraw and stall the layout's update loop.
        if (!model.IsRunning
            || state.CurrentSplitIndex < 0 || state.CurrentSplitIndex >= state.Run.Count
            || Elapsed() is not TimeSpan elapsed)
            return null;

        var method = state.CurrentTimingMethod;
        int index = state.CurrentSplitIndex;

        // Segment start = last non-null earlier split time (skips leave nulls).
        TimeSpan segmentStart = TimeSpan.Zero;
        for (int i = index - 1; i >= 0; i--)
        {
            if (state.Run[i].SplitTime[method] is TimeSpan st) { segmentStart = st; break; }
        }

        TimeSpan? fullBest = state.Run[index].BestSegmentTime[method];

        return model.Compute(elapsed, segmentStart, fullBest);
    }

    public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        ReloadSidecarIfPathChanged();
        lastResult = ComputeNow();

        cache.Restart();
        // Temporary bridge (Task 5 rewrites this against the stock Run
        // Prediction formula): displays only the death-aware finish estimate.
        cache["reckoning"] = lastResult?.Finish?.ToString() ?? "—";
        cache["sunk"] = "";
        cache["unlearned"] = lastResult?.Unlearned ?? false;
        cache["sunkRow"] = false;
        cache["dot"] = Settings.ShowStatusDot ? connection.DotColor.ToArgb() : 0;
        if (cache.HasChanged) invalidator?.Invalidate(0, 0, width, height);
    }

    public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion) =>
        DrawGeneral(g, state, width, VerticalHeight);

    public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion) =>
        DrawGeneral(g, state, HorizontalWidth, height);

    private void DrawGeneral(Graphics g, LiveSplitState state, float width, float height)
    {
        var textColor = state.LayoutSettings.TextColor;
        var valueColor = (lastResult?.Unlearned ?? false)
            ? Color.FromArgb(UnlearnedValueAlpha, textColor)
            : textColor;
        int rows = false ? 2 : 1;
        float rowHeight = height / rows;

        // Temporary bridge (Task 5 rewrites this against the stock Run
        // Prediction formula): displays only the death-aware finish estimate.
        DrawRow(g, state, 0, rowHeight, width, "Reckoning",
            lastResult?.Finish?.ToString() ?? "—", textColor, valueColor);
        if (false)
        {
            DrawRow(g, state, 1, rowHeight, width, "Sunk",
                "", textColor, valueColor);
        }

        if (Settings.ShowStatusDot)
        {
            using var dotBrush = new SolidBrush(connection.DotColor);
            g.FillRectangle(dotBrush, StatusDotLeftPx, (height - StatusDotSizePx) / 2f, StatusDotSizePx, StatusDotSizePx);
        }
    }

    private void DrawRow(Graphics g, LiveSplitState state, int row, float rowHeight, float width,
        string name, string value, Color nameColor, Color valueColor)
    {
        float y = row * rowHeight;
        var font = state.LayoutSettings.TextFont;
        var nameLabel = nameLabels[row];
        nameLabel.Text = name;
        nameLabel.Font = font;
        nameLabel.ForeColor = nameColor;
        nameLabel.HorizontalAlignment = StringAlignment.Near;
        nameLabel.VerticalAlignment = StringAlignment.Center;
        nameLabel.X = PaddingLeft + StatusDotSizePx;
        nameLabel.Y = y;
        nameLabel.Width = width / 2;
        nameLabel.Height = rowHeight;
        nameLabel.Draw(g);

        var valueLabel = valueLabels[row];
        valueLabel.Text = value;
        valueLabel.Font = state.LayoutSettings.TimesFont;
        valueLabel.ForeColor = valueColor;
        valueLabel.HorizontalAlignment = StringAlignment.Far;
        valueLabel.VerticalAlignment = StringAlignment.Center;
        valueLabel.X = width / 2;
        valueLabel.Y = y;
        valueLabel.Width = width / 2 - PaddingRight;
        valueLabel.Height = rowHeight;
        valueLabel.Draw(g);
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
    }
}
