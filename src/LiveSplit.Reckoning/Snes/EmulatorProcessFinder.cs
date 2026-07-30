using System.Diagnostics;

namespace LiveSplit.Reckoning.Snes;

/// <summary>Ordered process scan mirroring the kaizosplits autosplitter's
/// emulator list (same order, same names).</summary>
internal static class EmulatorProcessFinder
{
    private static readonly string[] Names =
    {
        "snes9x", "snes9x-x64", "bsnes", "retroarch", "higan",
        "snes9x-rr", "mesen", "emuhawk", "ares", "mednafen",
    };

    public static Process Find()
    {
        foreach (var name in Names)
        {
            Process winner = null;
            foreach (var p in Process.GetProcessesByName(name))
            {
                if (winner == null && !p.HasExited) winner = p;
                else p.Dispose();
            }
            if (winner != null) return winner;
        }
        return null;
    }
}
