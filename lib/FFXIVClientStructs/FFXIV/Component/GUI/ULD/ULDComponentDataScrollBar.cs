using System.Runtime.InteropServices;

namespace FFXIVClientStructs.FFXIV.Component.GUI.ULD
{

    [StructLayout(LayoutKind.Explicit, Size = 0x20)]
    public unsafe struct ULDComponentDataScrollBar
    {
        [FieldOffset(0x00)] public ULDComponentDataBase Base;
        [FieldOffset(0x0C)] public fixed uint Nodes[4];
        [FieldOffset(0x1C)] public ushort Margin;
        [FieldOffset(0x1E)] public byte Vertical;
    }
}
