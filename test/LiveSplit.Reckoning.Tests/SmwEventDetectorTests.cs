using LiveSplit.Reckoning.Watchers;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class SmwEventDetectorTests
{
    private readonly FakeSnesMemory mem = new();
    private readonly SmwEventDetector det = new();

    private DetectorTick Poll() => det.Poll(mem);

    private void EnterLevel(byte level = 5, byte room = 2)
    {
        mem.SetByte(SmwAddresses.LevelStart, 1);
        mem.SetByte(SmwAddresses.LevelNum, level);
        mem.SetByte(SmwAddresses.RoomNum, room);
        mem.SetByte(SmwAddresses.CpEntrance, room);
        Poll();   // baseline tick (also captures firstRoom via levelNum change)
    }

    public SmwEventDetectorTests()
    {
        Poll();       // very first poll: baseline only, all zeros
        EnterLevel();
    }

    [Fact]
    public void FirstPollEmitsNothing()
    {
        var fresh = new SmwEventDetector();
        mem.SetByte(SmwAddresses.PlayerAnimation, 9);   // already dead at attach
        Assert.Equal(DetectorTick.None, fresh.Poll(mem));
    }

    [Fact]
    public void AnimationShiftToNineIsDeath()
    {
        mem.SetByte(SmwAddresses.PlayerAnimation, 9);
        Assert.True(Poll().Death);
        // staying at 9 is not another death
        Assert.False(Poll().Death);
    }

    [Fact]
    public void PrepareLevelAfterDeathIsRespawn()
    {
        mem.SetByte(SmwAddresses.PlayerAnimation, 9);
        Poll();
        mem.SetByte(SmwAddresses.GameMode, 18);
        var tick = Poll();
        Assert.True(tick.Respawn);
        // died latch cleared: a second prepare-level without a death is not a respawn
        mem.SetByte(SmwAddresses.GameMode, 20);
        Poll();
        mem.SetByte(SmwAddresses.GameMode, 18);
        Assert.False(Poll().Respawn);
    }

    [Fact]
    public void PrepareLevelWithoutDeathIsNotRespawn()
    {
        mem.SetByte(SmwAddresses.GameMode, 18);
        Assert.False(Poll().Respawn);
    }

    [Fact]
    public void MidwayStepFiresCheckpoint()
    {
        mem.SetByte(SmwAddresses.Midway, 1);
        Assert.True(Poll().Checkpoint);
    }

    [Fact]
    public void MidwayJumpToOneFromGarbageDoesNotFire()
    {
        mem.SetByte(SmwAddresses.Midway, 3);
        Poll();
        mem.SetByte(SmwAddresses.Midway, 1);
        Assert.False(Poll().Checkpoint);   // StepTo requires exactly prev+1
    }

    [Fact]
    public void CpEntranceChangeInLevelFiresCheckpoint()
    {
        mem.SetByte(SmwAddresses.CpEntrance, 7);
        Assert.True(Poll().Checkpoint);
    }

    [Fact]
    public void CpEntranceChangeToFirstRoomIsSuppressed()
    {
        // Retry hacks re-arm the entrance byte to the level's own entry room on
        // load; that change must not read as a checkpoint.
        mem.SetByte(SmwAddresses.CpEntrance, 9);
        Poll();
        mem.SetByte(SmwAddresses.CpEntrance, 2);   // firstRoom captured in EnterLevel
        Assert.False(Poll().Checkpoint);
    }

    [Fact]
    public void CpEntranceGuardDisarmsAfterRealCheckpoint()
    {
        mem.SetByte(SmwAddresses.CpEntrance, 7);
        Poll();                                    // real CP -> firstRoom = 0
        mem.SetByte(SmwAddresses.CpEntrance, 2);   // back to the old entry room
        Assert.True(Poll().Checkpoint);            // guard no longer suppresses
    }

    [Fact]
    public void LevelEntryRearmDoesNotFireCheckpoint()
    {
        // Entering a new level changes levelNum/roomNum/cpEntrance on one tick;
        // that re-arm must not read as a checkpoint touch.
        mem.SetByte(SmwAddresses.LevelNum, 6);
        mem.SetByte(SmwAddresses.RoomNum, 11);
        mem.SetByte(SmwAddresses.CpEntrance, 11);
        Assert.False(Poll().Checkpoint);
    }

    [Fact]
    public void CpEntranceChangeOutsideLevelIsIgnored()
    {
        mem.SetByte(SmwAddresses.LevelStart, 0);
        Poll();
        mem.SetByte(SmwAddresses.CpEntrance, 7);
        Assert.False(Poll().Checkpoint);
    }

    [Fact]
    public void CheckpointSuppressedOnFinishFlagTick()
    {
        mem.SetByte(SmwAddresses.Midway, 1);
        mem.SetByte(SmwAddresses.Io, 4);   // goal fired same tick
        Assert.False(Poll().Checkpoint);
    }

    [Fact]
    public void IoZeroDoesNotResetFinishBaseline()
    {
        mem.SetByte(SmwAddresses.Io, 4);
        Poll();
        mem.SetByte(SmwAddresses.Io, 0);   // P-switch music transient
        Poll();
        mem.SetByte(SmwAddresses.Io, 4);   // back to the same value: no NEW finish
        mem.SetByte(SmwAddresses.Midway, 1);
        Assert.True(Poll().Checkpoint);    // not suppressed — 4 was already the baseline
    }

    [Fact]
    public void DetachDropsEdgesAndLatches()
    {
        mem.SetByte(SmwAddresses.PlayerAnimation, 9);
        Poll();                            // died latch set
        mem.Attached = false;
        Assert.Equal(DetectorTick.None, Poll());
        mem.Attached = true;
        mem.SetByte(SmwAddresses.GameMode, 18);
        Poll();                            // baseline-only tick after reattach
        Assert.False(Poll().Respawn);      // latch was dropped on detach
    }
}
