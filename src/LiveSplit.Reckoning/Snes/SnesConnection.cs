using System;
using System.Diagnostics;
using System.Drawing;
using LiveSplit.Reckoning.Watchers;
using SNES;

namespace LiveSplit.Reckoning.Snes;

internal sealed class SnesConnection : ISnesMemory
{
    // Reacquire at most once a second: process scans are not free, and the
    // 15 ms poll tick must stay cheap when no emulator is running.
    private const int AcquireIntervalMs = 1000;

    private readonly Emu emu = new();
    private Process process;
    private bool ready;
    private int lastGeneration;
    // Environment.TickCount64 is not available on net481 (it's a .NET Core
    // API); TickCount (int) wraps every ~24.9 days, so the throttle
    // comparison below uses unchecked subtraction to stay wrap-safe.
    private int lastAcquireTick;

    public EmuStatus Status { get; private set; }

    public bool IsAttached => ready && process != null && !HasExitedSafe(process);

    public int Generation => emu.Generation;

    public Color DotColor
    {
        get
        {
            var s = Status;
            return s == null
                ? StatusDot.ColorFor("Detached", false)
                : StatusDot.ColorFor(s.StateName, s.IsCoolingDown);
        }
    }

    public void Tick()
    {
        if (process != null && HasExitedSafe(process))
        {
            process.Dispose();
            process = null;
            ready = false;
        }

        if (process == null)
        {
            int now = Environment.TickCount;
            if (unchecked(now - lastAcquireTick) >= AcquireIntervalMs)
            {
                lastAcquireTick = now;
                process = EmulatorProcessFinder.Find();
                if (process != null)
                {
                    // A process that dies between find and attach must not
                    // throw out of Tick.
                    try { emu.Attach(process); }
                    catch { process = null; }
                    ready = false;
                }
            }
        }

        if (process != null)
        {
            bool wasReady = ready;
            if (ready && emu.Generation != lastGeneration) ready = false;   // rebind: re-baseline
            try { emu.Ready(); } catch { ready = false; }                   // the throw IS "not ready"
            if (!ready && !wasReady && process != null)
            {
                // Skipped for one tick right after a drop so IsAttached reads false
                // exactly once and the detector flushes its edge state.
                try { emu.GetOffset(); ready = true; lastGeneration = emu.Generation; } catch { }
            }
        }

        Status = emu.Status();
    }

    public bool ReadWramByte(int wramOffset, out byte value)
    {
        value = 0;
        if (!IsAttached) return false;
        try { value = emu.Read1(wramOffset); return true; }
        catch { return false; }
    }

    // process.HasExited can throw Win32Exception on an access-denied process
    // (e.g. running elevated); treat that as "exited" rather than let it
    // propagate out of the poll loop.
    private static bool HasExitedSafe(Process p)
    {
        try { return p.HasExited; }
        catch { return true; }
    }
}
