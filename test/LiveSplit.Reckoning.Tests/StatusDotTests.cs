using System.Drawing;
using LiveSplit.Reckoning.Snes;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class StatusDotTests
{
    // State names are raw strings on purpose: they pin the SNES.dll wire
    // contract (EmuState names) independently of the assembly.
    [Theory]
    [InlineData("Resolved", false, 0x39, 0x8F, 0xE5)]   // blue
    [InlineData("Held", false, 0x9B, 0x59, 0xB6)]        // purple
    [InlineData("Degraded", false, 0x2E, 0xCC, 0x40)]    // green
    [InlineData("Detached", false, 0xE5, 0x3E, 0x3E)]    // red
    [InlineData("Searching", false, 0xFF, 0xDC, 0x00)]   // yellow
    [InlineData("Discovering", false, 0xFF, 0xDC, 0x00)] // yellow
    [InlineData("Searching", true, 0x9A, 0x9A, 0x9A)]    // gray while cooling down
    [InlineData("NoContent", false, 0xFF, 0x85, 0x1B)]   // orange
    [InlineData("NoContent", true, 0x9A, 0x9A, 0x9A)]    // gray while cooling down
    [InlineData("SomethingNew", false, 0xFF, 0xDC, 0x00)]// unknown state -> yellow
    public void MapsStateToColor(string state, bool coolingDown, int r, int g, int b)
    {
        Assert.Equal(Color.FromArgb(r, g, b), StatusDot.ColorFor(state, coolingDown));
    }
}
