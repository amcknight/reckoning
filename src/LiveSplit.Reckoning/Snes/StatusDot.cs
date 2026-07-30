using System.Drawing;

namespace LiveSplit.Reckoning.Snes;

/// <summary>Pure EmuState-name -> dot color mapping (SMWCounters pattern).
/// String-keyed so this file has no SNES.dll dependency.</summary>
internal static class StatusDot
{
    private static readonly Color Blue = Color.FromArgb(0x39, 0x8F, 0xE5);    // resolved
    private static readonly Color Purple = Color.FromArgb(0x9B, 0x59, 0xB6);  // held (paused)
    private static readonly Color Green = Color.FromArgb(0x2E, 0xCC, 0x40);   // degraded (working)
    private static readonly Color Yellow = Color.FromArgb(0xFF, 0xDC, 0x00);  // searching
    private static readonly Color Orange = Color.FromArgb(0xFF, 0x85, 0x1B);  // attached, no content
    private static readonly Color Gray = Color.FromArgb(0x9A, 0x9A, 0x9A);    // retry cooldown
    private static readonly Color Red = Color.FromArgb(0xE5, 0x3E, 0x3E);     // detached

    public static Color ColorFor(string stateName, bool isCoolingDown) => stateName switch
    {
        "Resolved" => Blue,
        "Held" => Purple,
        "Degraded" => Green,
        "Detached" => Red,
        "Searching" or "Discovering" => isCoolingDown ? Gray : Yellow,
        "NoContent" => isCoolingDown ? Gray : Orange,
        _ => Yellow,
    };
}
