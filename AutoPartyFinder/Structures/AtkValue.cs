using System.Runtime.InteropServices;

namespace AutoPartyFinder.Structures;

[StructLayout(LayoutKind.Explicit, Size = 0x10)]
public struct AtkValue
{
    [FieldOffset(0x0)]
    public byte Type;

    [FieldOffset(0x8)]
    public long Int64;

    public static AtkValue CreateLong(long value)
    {
        return new AtkValue
        {
            Type = 15, // ValueType.Int
            Int64 = value
        };
    }
}