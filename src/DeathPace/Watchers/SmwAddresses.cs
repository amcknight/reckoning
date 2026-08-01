namespace LiveSplit.Reckoning.Watchers;

/// <summary>Console-space WRAM offsets, mirrored from kaizosplits
/// Components/SMW/SMW/Memory.cs (the proven address set).</summary>
internal static class SmwAddresses
{
    public const int PlayerAnimation = 0x0071;   // $7E0071: 9 = death animation
    public const int GameMode = 0x0100;          // $7E0100: 18 = prepare level (spawn point)
    public const int RoomNum = 0x010B;           // $7E010B
    public const int LevelNum = 0x13BF;          // $7E13BF
    public const int Midway = 0x13CE;            // $7E13CE: steps 0->1 on midway tape
    public const int LevelStart = 0x1935;        // $7E1935: 1 = in level
    public const int Io = 0x1DFB;                // $7E1DFB: 3=orb 4=goal 7=key 8=fadeout
    public const int CpEntrance = 0x1B403;       // $7FB403: retry-hack respawn entrance

    // Value meanings (named so no magic numbers appear in logic):
    public const byte DeathAnimation = 9;
    public const byte GameModePrepareLevel = 18;
    public const byte InLevel = 1;
    public const byte IoOrb = 3;
    public const byte IoGoal = 4;
    public const byte IoKey = 7;
    public const byte IoFadeout = 8;
}
