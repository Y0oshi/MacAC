using System.Numerics;
using System.Runtime.InteropServices;

namespace MacAC.Client.Graphics;

public sealed class ClipCycle : IDisposable
{

    public const int UpperPlanes = 8;

    public const int ChamberClipStrideOctets = 16 + UpperPlanes * 16; // 144

    public const int ChamberClipPlanesShift = 16;

    public const int LandUboOctets = 16 + UpperPlanes * 16; // 144

    public const uint LandClipUboMapping = 2;

    private byte[] _zoneOctets;

    internal int DynamicBufSetTally => 0;

    private ClipCycle(byte[] zoneOctets, int socketTally)
    {
        _zoneOctets = zoneOctets;
        SocketTally = socketTally;
    }

    public static ClipCycle NoClip()
    {
        byte[] octets = new byte[ChamberClipStrideOctets];
        return new ClipCycle(octets, socketTally: 1);
    }

    public int SocketTally { get; private set; }

    public void Reset()
    {
        if (_zoneOctets.Length < ChamberClipStrideOctets)
            SecureZoneCap(ChamberClipStrideOctets);
        Array.Clear(_zoneOctets, 0, ChamberClipStrideOctets);
        SocketTally = 1;
    }

    public void BeginFrame(int cycleSocket) => ArgumentOutOfRangeException.ThrowIfNegative(cycleSocket);

    public int AppendSlot(ClipFacetGroup set)
    {
        int tally = Math.Min(set.Count, UpperPlanes);
        if (tally is 0)
            return AppendSlot([]);

        Span<Vector4> planes = stackalloc Vector4[tally];
        for (int idx = 0; idx < tally; ++idx)
            planes[idx] = set.Planes[idx];
        return AppendSlot(planes);
    }

    public int AppendSlot(ReadOnlySpan<Vector4> planes)
    {
        int tally = Math.Min(planes.Length, UpperPlanes);

        int socket = SocketTally;
        int byteShift = socket * ChamberClipStrideOctets;
        SecureZoneCap(byteShift + ChamberClipStrideOctets);

        EmitUInt(_zoneOctets, byteShift, (uint)tally);

        for (int idx = 0; idx < tally; ++idx)
        {
            int po = byteShift + ChamberClipPlanesShift + idx * 16;
            EmitVec4(_zoneOctets, po, planes[idx]);
        }

        ++SocketTally;
        return socket;
    }

    public void Dispose()
    {
    }

    internal ReadOnlySpan<Vector4> FetchSocketPlanes(uint slot)
    {
        if (slot >= (uint)SocketTally)
            throw new ArgumentOutOfRangeException(nameof(slot));

        int byteShift = checked((int)slot * ChamberClipStrideOctets);
        int tally = checked((int)ScanUInt(_zoneOctets, byteShift));
        return (uint)tally > UpperPlanes
            ? throw new InvalidOperationException(
                $"Clip slot {slot} contains not valid plane count {tally}.")
            : (ReadOnlySpan<Vector4>)MemoryMarshal.Cast<byte, Vector4>(
            _zoneOctets.AsSpan(
                byteShift + ChamberClipPlanesShift,
                tally * sizeof(float) * 4));
    }

    private void SecureZoneCap(int neededOctets)
    {
        if (_zoneOctets.Length >= neededOctets) return;
        int newLength = Math.Max(neededOctets, _zoneOctets.Length * 2);
        Array.Resize(ref _zoneOctets, newLength);
    }

    private static void EmitUInt(byte[] dst, int shift, uint val)
    {
        dst[shift + 0] = (byte)(val & 0xFF);
        dst[shift + 1] = (byte)((val >> 8) & 0xFF);
        dst[shift + 2] = (byte)((val >> 16) & 0xFF);
        dst[shift + 3] = (byte)((val >> 24) & 0xFF);
    }

    private static uint ScanUInt(byte[] src, int shift)
    {
        return (uint)(src[shift + 0]
            | (src[shift + 1] << 8)
            | (src[shift + 2] << 16)
            | (src[shift + 3] << 24));
    }

    private static void EmitInt(byte[] dst, int shift, int val)
        => EmitUInt(dst, shift, unchecked((uint)val));

    private static void EmitVec4(byte[] dst, int shift, Vector4 v)
    {
        EmitFloat(dst, shift + 0, v.X);
        EmitFloat(dst, shift + 4, v.Y);
        EmitFloat(dst, shift + 8, v.Z);
        EmitFloat(dst, shift + 12, v.W);
    }

    private static void EmitFloat(byte[] dst, int shift, float val)
    {
        uint bitset = BitConverter.SingleToUInt32Bits(val);
        EmitUInt(dst, shift, bitset);
    }

    internal ReadOnlySpan<byte> ZoneOctets =>
        _zoneOctets.AsSpan(0, SocketTally * ChamberClipStrideOctets);

    internal ReadOnlySpan<byte> ZoneOctetsForTest => ZoneOctets;
}
