namespace LiveSplit.Reckoning.Watchers;

internal readonly record struct DetectorTick(bool Death, bool Checkpoint, bool Respawn)
{
    public static DetectorTick None => default;
}

/// <summary>Turns per-tick WRAM reads into death/checkpoint/respawn events,
/// porting kaizosplits' Watchers.cs semantics (see plan Reference facts).</summary>
internal sealed class SmwEventDetector
{
    private struct Snapshot
    {
        public byte PlayerAnimation, GameMode, RoomNum, LevelNum, Midway, LevelStart, Io, CpEntrance;
    }

    private bool hasPrev;
    private Snapshot prev;
    private bool died;          // set on death animation, cleared on respawn
    private byte firstRoom;     // cpEntrance guard: level entry room, cleared to 0 after a real CP
    private byte lastNonZeroIo; // io finish baseline; io transiently zeroes on P-switch/star music

    public void Reset()
    {
        hasPrev = false;
        died = false;
        firstRoom = 0;
        lastNonZeroIo = 0;
    }

    public DetectorTick Poll(ISnesMemory memory)
    {
        if (!memory.IsAttached || !TryRead(memory, out var cur))
        {
            Reset();   // never edge across a gap in visibility
            return DetectorTick.None;
        }

        if (!hasPrev)
        {
            Baseline(cur);
            return DetectorTick.None;
        }

        bool death = prev.PlayerAnimation != SmwAddresses.DeathAnimation
                  && cur.PlayerAnimation == SmwAddresses.DeathAnimation;
        if (death) died = true;

        bool respawn = false;
        bool toPrepareLevel = prev.GameMode != SmwAddresses.GameModePrepareLevel
                           && cur.GameMode == SmwAddresses.GameModePrepareLevel;
        if (toPrepareLevel && died)
        {
            respawn = true;
            died = false;
        }

        // Finish flags fire against the last non-zero io value.
        bool finishFired = cur.Io is 3 or 4 or 7 or 8 && cur.Io != lastNonZeroIo;

        // Level transition: capture the entry room BEFORE evaluating checkpoint
        // logic — on the entry tick levelNum/roomNum/cpEntrance all change
        // together, and treating the cpEntrance re-arm as a checkpoint would
        // false-fire (kaizosplits Watchers.cs:198-202 ordering).
        bool levelChanged = cur.LevelNum != prev.LevelNum;
        if (levelChanged)
        {
            firstRoom = cur.RoomNum;
        }

        bool inLevel = cur.LevelStart == SmwAddresses.InLevel;
        bool midwayStep = cur.Midway == 1 && prev.Midway + 1 == cur.Midway;   // StepTo: exact 0->1
        bool cpEntranceChanged = cur.CpEntrance != prev.CpEntrance;
        bool isShiftToFirstRoom = prev.CpEntrance != firstRoom && cur.CpEntrance == firstRoom;
        bool cpEntranceChange = inLevel && !levelChanged && cpEntranceChanged && !isShiftToFirstRoom;
        bool checkpoint = (midwayStep || cpEntranceChange) && !finishFired;
        if (checkpoint) firstRoom = 0;   // real CP disarms the entry-room guard

        if (cur.Io != 0) lastNonZeroIo = cur.Io;
        prev = cur;
        return new DetectorTick(death, checkpoint, respawn);
    }

    private void Baseline(Snapshot cur)
    {
        prev = cur;
        hasPrev = true;
        firstRoom = cur.RoomNum;
        if (cur.Io != 0) lastNonZeroIo = cur.Io;
    }

    private static bool TryRead(ISnesMemory m, out Snapshot s)
    {
        s = default;
        return m.ReadWramByte(SmwAddresses.PlayerAnimation, out s.PlayerAnimation)
            && m.ReadWramByte(SmwAddresses.GameMode, out s.GameMode)
            && m.ReadWramByte(SmwAddresses.RoomNum, out s.RoomNum)
            && m.ReadWramByte(SmwAddresses.LevelNum, out s.LevelNum)
            && m.ReadWramByte(SmwAddresses.Midway, out s.Midway)
            && m.ReadWramByte(SmwAddresses.LevelStart, out s.LevelStart)
            && m.ReadWramByte(SmwAddresses.Io, out s.Io)
            && m.ReadWramByte(SmwAddresses.CpEntrance, out s.CpEntrance);
    }
}
