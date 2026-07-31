using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using LiveSplit.Model;
using LiveSplit.Model.Comparisons;
using LiveSplit.TimeFormatters;
using LiveSplit.UI;

namespace LiveSplit.UI.Components;

/// <summary>Settings surface cloned from stock LiveSplit.RunPrediction (MIT) —
/// same fields, XML keys, and defaults — plus Reckoning's ShowStatusDot.</summary>
public class ReckoningComponentSettings : UserControl
{
    // Accuracy combo order mirrors TimeAccuracy's own declared order, so
    // SelectedIndex casts directly to the enum with no lookup table.
    private static readonly string[] AccuracyNames = Enum.GetNames(typeof(TimeAccuracy));

    private readonly ComboBox comparisonBox;
    private readonly CheckBox overrideTextColorBox;
    private readonly Button textColorButton;
    private readonly CheckBox overrideTimeColorBox;
    private readonly Button timeColorButton;
    private readonly Button backgroundColorButton;
    private readonly Button backgroundColor2Button;
    private readonly ComboBox gradientBox;
    private readonly ComboBox accuracyBox;
    private readonly CheckBox display2RowsBox;
    private readonly CheckBox dotBox;

    public string Comparison { get; set; } = "Current Comparison";
    public bool OverrideTextColor { get; set; }
    public Color TextColor { get; set; } = Color.FromArgb(255, 255, 255);
    public bool OverrideTimeColor { get; set; }
    public Color TimeColor { get; set; } = Color.FromArgb(255, 255, 255);
    public Color BackgroundColor { get; set; } = Color.Transparent;
    public Color BackgroundColor2 { get; set; } = Color.Transparent;
    public GradientType BackgroundGradient { get; set; } = GradientType.Plain;
    public TimeAccuracy Accuracy { get; set; } = TimeAccuracy.Seconds;
    public bool Display2Rows { get; set; }
    public bool ShowStatusDot { get; set; } = true;
    public LiveSplitState CurrentState { get; set; }

    public ReckoningComponentSettings()
    {
        var comparisonLabel = new Label { Text = "Comparison:", AutoSize = true, Left = 8, Top = 8 };
        comparisonBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 8, Top = 24, Width = 320 };

        overrideTextColorBox = new CheckBox { Text = "Override text color", AutoSize = true, Left = 8, Top = 56 };
        textColorButton = new Button { FlatStyle = FlatStyle.Popup, Left = 240, Top = 53, Width = 23, Height = 23, Enabled = false };

        overrideTimeColorBox = new CheckBox { Text = "Override time color", AutoSize = true, Left = 8, Top = 84 };
        timeColorButton = new Button { FlatStyle = FlatStyle.Popup, Left = 240, Top = 81, Width = 23, Height = 23, Enabled = false };

        var backgroundLabel = new Label { Text = "Background:", AutoSize = true, Left = 8, Top = 116 };
        backgroundColorButton = new Button { FlatStyle = FlatStyle.Popup, Left = 100, Top = 112, Width = 23, Height = 23 };
        backgroundColor2Button = new Button { FlatStyle = FlatStyle.Popup, Left = 130, Top = 112, Width = 23, Height = 23 };
        gradientBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 165, Top = 112, Width = 163 };
        gradientBox.Items.AddRange(Enum.GetNames(typeof(GradientType)));

        var accuracyLabel = new Label { Text = "Accuracy:", AutoSize = true, Left = 8, Top = 148 };
        accuracyBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 8, Top = 164, Width = 140 };
        accuracyBox.Items.AddRange(AccuracyNames);

        display2RowsBox = new CheckBox { Text = "Display 2 rows", AutoSize = true, Left = 8, Top = 196 };
        dotBox = new CheckBox { Text = "Show connection status dot", AutoSize = true, Left = 8, Top = 222 };

        Controls.Add(comparisonLabel);
        Controls.Add(comparisonBox);
        Controls.Add(overrideTextColorBox);
        Controls.Add(textColorButton);
        Controls.Add(overrideTimeColorBox);
        Controls.Add(timeColorButton);
        Controls.Add(backgroundLabel);
        Controls.Add(backgroundColorButton);
        Controls.Add(backgroundColor2Button);
        Controls.Add(gradientBox);
        Controls.Add(accuracyLabel);
        Controls.Add(accuracyBox);
        Controls.Add(display2RowsBox);
        Controls.Add(dotBox);

        Load += (_, _) =>
        {
            PopulateComparisons();
            ModelToView();
        };

        comparisonBox.SelectedIndexChanged += (_, _) =>
            Comparison = comparisonBox.SelectedItem?.ToString() ?? Comparison;

        overrideTextColorBox.CheckedChanged += (_, _) =>
        {
            OverrideTextColor = overrideTextColorBox.Checked;
            textColorButton.Enabled = OverrideTextColor;
        };
        textColorButton.Click += (_, _) =>
        {
            SettingsHelper.ColorButtonClick(textColorButton, this);
            TextColor = textColorButton.BackColor;
        };

        overrideTimeColorBox.CheckedChanged += (_, _) =>
        {
            OverrideTimeColor = overrideTimeColorBox.Checked;
            timeColorButton.Enabled = OverrideTimeColor;
        };
        timeColorButton.Click += (_, _) =>
        {
            SettingsHelper.ColorButtonClick(timeColorButton, this);
            TimeColor = timeColorButton.BackColor;
        };

        backgroundColorButton.Click += (_, _) =>
        {
            SettingsHelper.ColorButtonClick(backgroundColorButton, this);
            BackgroundColor = backgroundColorButton.BackColor;
        };
        backgroundColor2Button.Click += (_, _) =>
        {
            SettingsHelper.ColorButtonClick(backgroundColor2Button, this);
            BackgroundColor2 = backgroundColor2Button.BackColor;
        };
        gradientBox.SelectedIndexChanged += (_, _) =>
            BackgroundGradient = (GradientType)Enum.Parse(typeof(GradientType), (string)gradientBox.SelectedItem);

        accuracyBox.SelectedIndexChanged += (_, _) => Accuracy = (TimeAccuracy)accuracyBox.SelectedIndex;
        display2RowsBox.CheckedChanged += (_, _) => Display2Rows = display2RowsBox.Checked;
        dotBox.CheckedChanged += (_, _) => ShowStatusDot = dotBox.Checked;

        // Pre-seed the view from the model's defaults so the control looks
        // right even before a Load event fires (e.g. hosted in a designer).
        ModelToView();
    }

    private void PopulateComparisons()
    {
        // Unit tests construct this control with no LiveSplitState.
        if (CurrentState == null) return;

        comparisonBox.Items.Clear();
        comparisonBox.Items.Add("Current Comparison");
        comparisonBox.Items.AddRange(CurrentState.Run.Comparisons
            .Where(x => x != BestSplitTimesComparisonGenerator.ComparisonName && x != NoneComparisonGenerator.ComparisonName)
            .ToArray());
        if (!comparisonBox.Items.Contains(Comparison))
        {
            comparisonBox.Items.Add(Comparison);
        }

        comparisonBox.SelectedItem = Comparison;
    }

    private void ModelToView()
    {
        overrideTextColorBox.Checked = OverrideTextColor;
        textColorButton.BackColor = TextColor;
        textColorButton.Enabled = OverrideTextColor;

        overrideTimeColorBox.Checked = OverrideTimeColor;
        timeColorButton.BackColor = TimeColor;
        timeColorButton.Enabled = OverrideTimeColor;

        backgroundColorButton.BackColor = BackgroundColor;
        backgroundColor2Button.BackColor = BackgroundColor2;
        gradientBox.SelectedItem = BackgroundGradient.ToString();

        accuracyBox.SelectedIndex = (int)Accuracy;
        display2RowsBox.Checked = Display2Rows;
        dotBox.Checked = ShowStatusDot;

        if (CurrentState != null) comparisonBox.SelectedItem = Comparison;
    }

    public XmlNode GetSettings(XmlDocument document)
    {
        var parent = document.CreateElement("Settings");
        SettingsHelper.CreateSetting(document, parent, "Version", "2");
        SettingsHelper.CreateSetting(document, parent, "Comparison", Comparison);
        SettingsHelper.CreateSetting(document, parent, "OverrideTextColor", OverrideTextColor);
        SettingsHelper.CreateSetting(document, parent, "TextColor", TextColor);
        SettingsHelper.CreateSetting(document, parent, "OverrideTimeColor", OverrideTimeColor);
        SettingsHelper.CreateSetting(document, parent, "TimeColor", TimeColor);
        SettingsHelper.CreateSetting(document, parent, "BackgroundColor", BackgroundColor);
        SettingsHelper.CreateSetting(document, parent, "BackgroundColor2", BackgroundColor2);
        SettingsHelper.CreateSetting(document, parent, "BackgroundGradient", BackgroundGradient);
        SettingsHelper.CreateSetting(document, parent, "Accuracy", Accuracy);
        SettingsHelper.CreateSetting(document, parent, "Display2Rows", Display2Rows);
        SettingsHelper.CreateSetting(document, parent, "ShowStatusDot", ShowStatusDot);
        return parent;
    }

    public void SetSettings(XmlNode settings)
    {
        Comparison = SettingsHelper.ParseString(settings["Comparison"], "Current Comparison");
        OverrideTextColor = SettingsHelper.ParseBool(settings["OverrideTextColor"], false);
        TextColor = SettingsHelper.ParseColor(settings["TextColor"], Color.FromArgb(255, 255, 255));
        OverrideTimeColor = SettingsHelper.ParseBool(settings["OverrideTimeColor"], false);
        TimeColor = SettingsHelper.ParseColor(settings["TimeColor"], Color.FromArgb(255, 255, 255));
        BackgroundColor = SettingsHelper.ParseColor(settings["BackgroundColor"], Color.Transparent);
        BackgroundColor2 = SettingsHelper.ParseColor(settings["BackgroundColor2"], Color.Transparent);
        BackgroundGradient = ParseEnumOrDefault(settings["BackgroundGradient"], GradientType.Plain);
        Accuracy = ParseEnumOrDefault(settings["Accuracy"], TimeAccuracy.Seconds);
        Display2Rows = SettingsHelper.ParseBool(settings["Display2Rows"], false);
        ShowStatusDot = SettingsHelper.ParseBool(settings["ShowStatusDot"], true);
        if (IsHandleCreated) ModelToView();
    }

    // Unlike SettingsHelper.ParseEnum (which throws Enum.Parse's exception on
    // an invalid-but-present value), this falls back to the default for both
    // a missing element and a corrupted/hand-edited one — a bad enum string
    // in a saved layout must never crash settings load.
    private static T ParseEnumOrDefault<T>(XmlElement element, T defaultValue) where T : struct
    {
        SettingsHelper.TryParseEnum(element, out T result, defaultValue);
        return result;
    }

    public int GetSettingsHashCode()
    {
        // 397: standard odd prime for hash folding (matches SMWCounters).
        int hash = Comparison.GetHashCode();
        hash = hash * 397 ^ OverrideTextColor.GetHashCode();
        hash = hash * 397 ^ TextColor.GetHashCode();
        hash = hash * 397 ^ OverrideTimeColor.GetHashCode();
        hash = hash * 397 ^ TimeColor.GetHashCode();
        hash = hash * 397 ^ BackgroundColor.GetHashCode();
        hash = hash * 397 ^ BackgroundColor2.GetHashCode();
        hash = hash * 397 ^ BackgroundGradient.GetHashCode();
        hash = hash * 397 ^ Accuracy.GetHashCode();
        hash = hash * 397 ^ Display2Rows.GetHashCode();
        hash = hash * 397 ^ ShowStatusDot.GetHashCode();
        return hash;
    }
}
