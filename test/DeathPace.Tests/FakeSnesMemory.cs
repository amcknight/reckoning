using System.Collections.Generic;
using DeathPace.Watchers;

namespace DeathPace.Tests;

internal sealed class FakeSnesMemory : ISnesMemory
{
    private readonly Dictionary<int, byte> bytes = new();

    public bool Attached { get; set; } = true;
    public bool IsAttached => Attached;

    public void SetByte(int offset, byte value) => bytes[offset] = value;

    public bool ReadWramByte(int wramOffset, out byte value)
    {
        value = 0;
        if (!Attached) return false;
        bytes.TryGetValue(wramOffset, out value);
        return true;
    }
}
