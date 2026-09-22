using System.Globalization;
using System.Numerics;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Mechanics.Shell;

public enum MechRadarBehavior : byte
{
    Undef = 0,
    ShowNever = 1,
    ShowMovement = 2,
    ShowAttacking = 3,
    ShowAlways = 4,
}

public enum RadarBlipGlyph
{
    Undef = 0,
    Circle = 1,
    Box = 2,
    X = 3,
    Plus = 4,
    Triangle = 5,
    InvertedTriangle = 6,
    XBox = 7,

    Default = Plus,
    AllegianceMember = Box,
    FellowshipLeader = Triangle,
    Fellowship = InvertedTriangle,
    Threat = X,
    ThreatAllegiance = XBox,
}

public enum RadarCompassMark
{
    North,
    East,
    South,
    West,
}

public readonly record struct RadarPixelShift(int X, int Y);

public readonly record struct RadarMapping(Vector2 Pixel, float RgbMultiplier);

/// <summary>Geometry of the retail radar: ranges, blip glyphs, and the compass ring.</summary>
public static class CanonRadar
{
    public const float ExteriorSpanMeters = 75f;
    public const float InsideSpanMeters = 25f;
    public const float AltitudeDimThresholdMeters = 5f;
    public const float AltitudeDimMultiplier = 0.65f;
    public const float RefreshIntervalSecs = 0.025f;

    private const float DegToRadians = 0.0174532924f;

    private static readonly RadarPixelShift[] Dot = [new(0, 0)];
    private static readonly RadarPixelShift[] Rims = [new(0, -1), new(0, 1), new(-1, 0), new(1, 0)];
    private static readonly RadarPixelShift[] Corners = [new(1, 1), new(-1, -1), new(-1, 1), new(1, -1)];

    private static readonly RadarPixelShift[] BboxGlyph = [.. Rims, .. Corners];
    private static readonly RadarPixelShift[] XGlyph = [.. Dot, .. Corners];
    private static readonly RadarPixelShift[] PlusGlyph = [.. Dot, .. Rims];
    private static readonly RadarPixelShift[] TriangleGlyph = [new(0, 0), new(-1, 1), new(0, 1), new(1, 1)];
    private static readonly RadarPixelShift[] InvertedTriangleGlyph = [new(0, 0), new(-1, -1), new(0, -1), new(1, -1)];
    private static readonly RadarPixelShift[] XBboxGlyph =
        [.. Rims, .. Corners, new(-2, -2), new(2, -2), new(-2, 2), new(2, 2)];

    private static readonly RadarPixelShift[] AvatarGlyph =
        [.. Dot, .. Rims, new(-2, 0), new(2, 0), new(0, -2), new(0, 2)];

    private static readonly RadarPixelShift[] PickLoop = LoopOfRadiusThree();

    public static ReadOnlySpan<RadarPixelShift> AvatarMarkerPx => AvatarGlyph;

    public static ReadOnlySpan<RadarPixelShift> PickPx => PickLoop;

    public static bool IsShowable(MechRadarBehavior behavior, bool hasKineticsObject)
    {
        return hasKineticsObject
        && behavior is MechRadarBehavior.ShowMovement or MechRadarBehavior.ShowAttacking or MechRadarBehavior.ShowAlways;
    }

    public static RadarBlipGlyph FetchBlipForm(RadarObjectFacets mark, RadarRelationFacets relationship = default)
    {
        if (!mark.IsValid)
            return RadarBlipGlyph.Undef;
        if (relationship.IsFellowshipLeader)
            return RadarBlipGlyph.FellowshipLeader;
        if (relationship.IsFellowshipMember)
            return RadarBlipGlyph.Fellowship;
        if (relationship.IsAllegianceMember)
            return RadarBlipGlyph.AllegianceMember;

        bool mutualThreat =
            (mark.IsPlayerKiller && relationship.PlayerIsPlayerKiller)
            || (mark.IsPkLite && relationship.PlayerIsPkLite);
        return mutualThreat ? RadarBlipGlyph.Threat : RadarBlipGlyph.Default;
    }

    public static ReadOnlySpan<RadarPixelShift> FetchBlipPx(RadarBlipGlyph form)
    {
        return form switch
        {
            RadarBlipGlyph.Circle => Dot,
            RadarBlipGlyph.Box => BboxGlyph,
            RadarBlipGlyph.X => XGlyph,
            RadarBlipGlyph.Plus => PlusGlyph,
            RadarBlipGlyph.Triangle => TriangleGlyph,
            RadarBlipGlyph.InvertedTriangle => InvertedTriangleGlyph,
            RadarBlipGlyph.XBox => XBboxGlyph,
            _ => [],
        };
    }

    public static float FetchSpanMeters(bool isBeyond) =>
        isBeyond ? ExteriorSpanMeters : InsideSpanMeters;

    public static bool TryProject(
        Vector3 avatarSpaceMeters,
        Vector2 middlePx,
        int radarRadiusPx,
        float radarSpanMeters,
        out RadarMapping proj)
    {
        float reach = radarSpanMeters - 1f;
        float planar = avatarSpaceMeters.X * avatarSpaceMeters.X + avatarSpaceMeters.Y * avatarSpaceMeters.Y;
        if (planar >= reach * reach)
        {
            proj = default;
            return false;
        }

        float pxPerMetre = radarRadiusPx / radarSpanMeters;
        Vector2 pixel = new Vector2(
            (int)(middlePx.X + avatarSpaceMeters.X * pxPerMetre),
            (int)(middlePx.Y - avatarSpaceMeters.Y * pxPerMetre));
        proj = new RadarMapping(pixel, FetchAltitudeRgbMultiplier(avatarSpaceMeters.Z));
        return true;
    }

    public static float FetchAltitudeRgbMultiplier(float avatarSpaceZMeters)
    {
        return MathF.Abs(avatarSpaceZMeters) < AltitudeDimThresholdMeters ? 1f : AltitudeDimMultiplier;
    }

    public static Vector2 FetchCompassTicketTopLeft(
        float avatarBearingDeg,
        RadarCompassMark pt,
        Vector2 radarMiddlePx,
        float ticketMagnitudePx,
        Vector2 ticketDimsPx)
    {
        double bearing = avatarBearingDeg * DegToRadians + pt switch
        {
            RadarCompassMark.North => Math.PI,
            RadarCompassMark.East => (double)1.57079637f,
            RadarCompassMark.West => (double)4.71238899f,
            _ => 0d,
        };

        float centreX = (float)(Math.Sin(bearing) * ticketMagnitudePx + radarMiddlePx.X);
        float centreY = (float)(Math.Cos(bearing) * ticketMagnitudePx + radarMiddlePx.Y);
        return new Vector2(
            (int)(centreX - ticketDimsPx.X * 0.5f),
            (int)(centreY - ticketDimsPx.Y * 0.5f));
    }

    private static RadarPixelShift[] LoopOfRadiusThree()
    {
        var loop = new List<RadarPixelShift>(20);
        for (int t = -2; t <= 2; ++t)
        {
            loop.Add(new RadarPixelShift(t, 3));
            loop.Add(new RadarPixelShift(t, -3));
        }
        for (int t = -2; t <= 2; ++t)
        {
            loop.Add(new RadarPixelShift(3, t));
            loop.Add(new RadarPixelShift(-3, t));
        }
        return [.. loop];
    }
}

/// <summary>Map coordinates in the "12.3N, 45.6E" convention.</summary>
public readonly record struct RadarCoords(double X, double Y)
{
    private const int LbOrigin = 1024;
    private const double ChunksPerUnit = 0.1;

    public string XPhrase => Axis(X, "W", "E");

    public string YPhrase => Axis(Y, "S", "N");

    public string CombinedPhrase => $"{YPhrase},{XPhrase}";

    public static bool TryFromChamber(uint chamberIdent, out RadarCoords coordinates)
    {
        if (!MechLandDefs.GidToLcoord(chamberIdent, out int bx, out int by))
        {
            coordinates = default;
            return false;
        }

        coordinates = new RadarCoords(
            (bx - LbOrigin) * ChunksPerUnit + 0.5,
            (by - LbOrigin) * ChunksPerUnit + 0.5);
        return true;
    }

    private static string Axis(double val, string negative, string positive)
    {
        string flank = val < 0d ? negative : val > 0d ? positive : string.Empty;
        return Math.Abs(val).ToString("F1", CultureInfo.InvariantCulture) + flank;
    }
}
