using System;
using System.Windows.Forms;
using System.Xml;
using LiveSplit.Reckoning.UI;
using LiveSplit.UI;

namespace LiveSplit.UI.Components;

public class ReckoningComponentSettings : UserControl
{
    private readonly CheckBox sunkBox;
    private readonly CheckBox dotBox;
    private readonly ComboBox accuracyBox;

    public bool ShowSunkRow { get; set; } = true;
    public bool ShowStatusDot { get; set; } = true;
    internal RowAccuracy Accuracy { get; set; } = RowAccuracy.Tenths;

    public ReckoningComponentSettings()
    {
        sunkBox = new CheckBox { Text = "Show Sunk row", AutoSize = true, Left = 8, Top = 8 };
        dotBox = new CheckBox { Text = "Show connection status dot", AutoSize = true, Left = 8, Top = 34 };
        accuracyBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 8, Top = 60, Width = 140 };
        accuracyBox.Items.AddRange(new object[] { "Seconds", "Tenths", "Hundredths" });
        Controls.Add(sunkBox);
        Controls.Add(dotBox);
        Controls.Add(accuracyBox);
        Load += (_, _) => ModelToView();
        sunkBox.CheckedChanged += (_, _) => ShowSunkRow = sunkBox.Checked;
        dotBox.CheckedChanged += (_, _) => ShowStatusDot = dotBox.Checked;
        accuracyBox.SelectedIndexChanged += (_, _) => Accuracy = (RowAccuracy)accuracyBox.SelectedIndex;
    }

    private void ModelToView()
    {
        sunkBox.Checked = ShowSunkRow;
        dotBox.Checked = ShowStatusDot;
        accuracyBox.SelectedIndex = (int)Accuracy;
    }

    public XmlNode GetSettings(XmlDocument document)
    {
        var parent = document.CreateElement("Settings");
        SettingsHelper.CreateSetting(document, parent, "Version", "1");
        SettingsHelper.CreateSetting(document, parent, "ShowSunkRow", ShowSunkRow);
        SettingsHelper.CreateSetting(document, parent, "ShowStatusDot", ShowStatusDot);
        SettingsHelper.CreateSetting(document, parent, "Accuracy", Accuracy.ToString());
        return parent;
    }

    public void SetSettings(XmlNode settings)
    {
        ShowSunkRow = SettingsHelper.ParseBool(settings["ShowSunkRow"], true);
        ShowStatusDot = SettingsHelper.ParseBool(settings["ShowStatusDot"], true);
        Accuracy = Enum.TryParse(SettingsHelper.ParseString(settings["Accuracy"], "Tenths"), out RowAccuracy acc)
            ? acc : RowAccuracy.Tenths;
        if (IsHandleCreated) ModelToView();
    }

    public int GetSettingsHashCode()
    {
        // 397: standard odd prime for hash folding (matches SMWCounters).
        int hash = ShowSunkRow.GetHashCode();
        hash = hash * 397 ^ ShowStatusDot.GetHashCode();
        hash = hash * 397 ^ Accuracy.GetHashCode();
        return hash;
    }
}
