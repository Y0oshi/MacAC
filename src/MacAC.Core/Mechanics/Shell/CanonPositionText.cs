using System.Globalization;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Mechanics.Shell;

public static class CanonPositionText
{
    /// <summary>The "/loc" style dump: cell, origin, then the W-first quaternion.</summary>
    public static string Format(Locus locus)
    {
        System.Numerics.Vector3 o = locus.Frame.Origin;
        var r = locus.Frame.Orientation;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"0x{locus.ObjCellId:X8} [{o.X:F6} {o.Y:F6} {o.Z:F6}] {r.W:F6} {r.X:F6} {r.Y:F6} {r.Z:F6}");
    }

    public static string? ComposeExteriorChamber(uint chamberIdent)
    {
        return RadarCoords.TryFromChamber(chamberIdent, out RadarCoords coords) ? coords.CombinedPhrase : null;
    }
}
