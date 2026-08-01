namespace DeathPace.Watchers;

/// <summary>The WRAM seam. Offsets are console-space ($7E0000 -> 0,
/// $7F0000 -> 0x10000), matching SNES.Emu.Read1.</summary>
internal interface ISnesMemory
{
    bool IsAttached { get; }
    bool ReadWramByte(int wramOffset, out byte value);
}
