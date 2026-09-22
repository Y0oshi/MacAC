using System.Numerics;
using System.Text;

namespace MacAC.Client.Graphics.Stride;

internal static class StrideTranscriptDump
{
    private static bool Enabled =>
        MacAC.Mechanics.Drawing.DrawTelemetry.DumpWalkTranscriptEnabled;

    internal static void DumpCycleTrunk(
        int cycleNumber,
        uint camChamberIdent,
        Vector3 lbOwnOrigin,
        Vector3 ahead)
    {
        if (!Enabled) return;

        Vector3 realmUp = MathF.Abs(Vector3.Dot(ahead, Vector3.UnitZ)) > 0.999f
            ? Vector3.UnitY
            : Vector3.UnitZ;
        Vector3 right = Vector3.Normalize(Vector3.Cross(ahead, realmUp));
        Vector3 up = Vector3.Cross(right, ahead);
        DumpCycleTrunkRest(ahead, right, up, cycleNumber, camChamberIdent, lbOwnOrigin);
    }

    private static void DumpCycleTrunkRest(Vector3 ahead, Vector3 right, Vector3 up, int cycleNumber, uint camChamberIdent, Vector3 lbOwnOrigin)
    {
        Matrix4x4 basis = new Matrix4x4(
                right.X, right.Y, right.Z, 0f,
                ahead.X, ahead.Y, ahead.Z, 0f,
                up.X, up.Y, up.Z, 0f,
                0f, 0f, 0f, 1f);
        Quaternion q = Quaternion.CreateFromRotationMatrix(basis);
        Console.WriteLine($"F {cycleNumber}");
        Console.WriteLine(
                    $"P {camChamberIdent:x8} "
                    + $"{HexOf(lbOwnOrigin.X)} {HexOf(lbOwnOrigin.Y)} {HexOf(lbOwnOrigin.Z)} "
                    + $"{HexOf(q.W)} {HexOf(q.X)} {HexOf(q.Y)} {HexOf(q.Z)}");
    }

    internal static void DumpScenery()
    {
        if (!Enabled) return;
        Console.WriteLine("LS");
    }

    internal static void DumpStructure(uint locusChamberIdent)
    {
        if (!Enabled) return;
        Console.WriteLine($"BLD {locusChamberIdent:x8}");
    }

    internal static void DumpPaintInside(uint chamberIdent)
    {
        if (!Enabled) return;
        Console.WriteLine($"DI {chamberIdent:x8}");
    }

    internal static void DumpPaintChambers(
        bool exteriorPview, int beyondLensTally, IReadOnlyList<uint> chambers)
    {
        if (!Enabled) return;
        StringBuilder builder = new StringBuilder(48 + chambers.Count * 9);
        builder.Append("DC pv=").Append(exteriorPview ? "00000001" : "00000000");
        builder.Append(" ov=").Append(beyondLensTally);
        DumpPaintChambersRest(builder, chambers);
    }

    private static void DumpPaintChambersRest(StringBuilder builder, IReadOnlyList<uint> chambers)
    {
        builder.Append(" n=").Append(chambers.Count).Append(':');
        for (int idx = 0; idx < chambers.Count; ++idx)
            builder.Append(' ').Append(chambers[idx].ToString("x8"));
        Console.WriteLine(builder.ToString());
    }

    internal static void DumpLandChamber(uint chamberIdent)
    {
        if (!Enabled) return;
        Console.WriteLine($"LC {chamberIdent:x8}");
    }

    internal static void DumpOrderChamber(uint chamberIdent)
    {
        if (!Enabled) return;
        Console.WriteLine($"SC {chamberIdent:x8}");
    }

    internal static void DumpEnvironChamberShell(uint chamberIdent)
    {
        if (!Enabled) return;
        Console.WriteLine($"EC {chamberIdent:x8}");
    }

    internal static void DumpObjectChamberPivot(uint chamberIdent)
    {
        if (!Enabled) return;
        Console.WriteLine($"OC {chamberIdent:x8}");
    }

    internal static uint LodChamberIdent(uint lbIdent, int flankChamberTally, int chamberOrdinal)
    {
        int x = chamberOrdinal / flankChamberTally;
        int y = chamberOrdinal % flankChamberTally;
        return (lbIdent & 0xFFFF0000u) | checked((uint)(x * 8 + y + 1));
    }

    private static string HexOf(float val)
        => unchecked((uint)BitConverter.SingleToInt32Bits(val)).ToString("x8");
}
