using System.Numerics;
using MacAC.Dat;
using Plane = System.Numerics.Plane;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Recursive tree walks: which polygons a sphere hits, supports, or is buried in.</summary>
public static partial class CellBspProbe
{
    private static bool TraverseFacingStrike(
        PhysicsBspNode? joint,
        Dictionary<ushort, SettledPolygon> settled,
        Ball orb,
        Vector3 travel,
        ref SettledPolygon? strikePoly,
        ref Vector3 linkPt)
    {
        if (joint is null) return false;
        if (!Overlaps(joint, orb)) return false;

        // Leaf: test each polygon
        if (joint.Tag == BspTag.Leaf)
        {
            if (joint.Polygons.Count is 0) return false;

            foreach (ushort polyIdent in joint.Polygons)
            {
                if (!settled.TryGetValue(polyIdent, out var poly)) continue;

                if (SmackFacing(poly, orb, travel, ref linkPt, ref strikePoly))
                    return true;
            }
            return false;
        }

        float distance = Vector3.Dot(joint.Splitter.Normal, orb.Center)
                      + joint.Splitter.D;
        float reach = orb.Radius - Eps;

        if (distance >= reach)
            return TraverseFacingStrike(joint.Front, settled, orb, travel,
                                                 ref strikePoly, ref linkPt);

        if (distance <= -reach)
            return TraverseFacingStrike(joint.Back, settled, orb, travel,
                                                 ref strikePoly, ref linkPt);

        if (joint.Front is not null &&
            TraverseFacingStrike(joint.Front, settled, orb, travel,
                                          ref strikePoly, ref linkPt))
            return true;

        if (joint.Back is not null &&
            TraverseFacingStrike(joint.Back, settled, orb, travel,
                                          ref strikePoly, ref linkPt))
            return true;

        return false;
    }

    private static void TraversePassable(
        PhysicsBspNode? joint,
        Dictionary<ushort, SettledPolygon> settled,
        SweepPath trail,
        ref Ball validSpot,
        Vector3 travel,
        Vector3 up,
        ref SettledPolygon? strikePoly,
        ref ushort strikePolyIdent,
        ref bool altered)
    {
        if (joint is null) return;
        if (!Overlaps(joint, validSpot)) return;

        // Leaf.
        if (joint.Tag == BspTag.Leaf)
        {
            if (joint.Polygons.Count is 0) return;

            foreach (ushort polyIdent in joint.Polygons)
            {
                if (!settled.TryGetValue(polyIdent, out var poly)) continue;

                bool passable = TouchesPassable(poly, trail, validSpot, up);
                bool adjusted = passable
                    && PushBack(poly, trail, ref validSpot, travel);

                if (passable && adjusted)
                {
                    altered = true;
                    strikePoly = poly;
                    strikePolyIdent = polyIdent;
                }
            }
            return;
        }

        float distance = Vector3.Dot(joint.Splitter.Normal, validSpot.Center)
                      + joint.Splitter.D;
        float reach = validSpot.Radius - Eps;

        if (distance >= reach)
        {
            TraversePassable(joint.Front, settled, trail, ref validSpot, travel, up,
                                  ref strikePoly, ref strikePolyIdent, ref altered);
            return;
        }

        if (distance <= -reach)
        {
            TraversePassable(joint.Back, settled, trail, ref validSpot, travel, up,
                                  ref strikePoly, ref strikePolyIdent, ref altered);
            return;
        }

        TraversePassable(joint.Front, settled, trail, ref validSpot, travel, up,
                              ref strikePoly, ref strikePolyIdent, ref altered);
        TraversePassable(joint.Back, settled, trail, ref validSpot, travel, up,
                              ref strikePoly, ref strikePolyIdent, ref altered);
    }

    private static bool TraverseSupported(
        PhysicsBspNode? joint,
        Dictionary<ushort, SettledPolygon> settled,
        SweepPath trail,
        Ball orb,
        Vector3 up)
    {
        if (joint is null) return false;
        if (!Overlaps(joint, orb)) return false;

        // Leaf.
        if (joint.Tag == BspTag.Leaf)
        {
            if (joint.Polygons.Count is 0) return false;

            foreach (ushort polyIdent in joint.Polygons)
            {
                if (!settled.TryGetValue(polyIdent, out var poly)) continue;

                if (TouchesPassable(poly, trail, orb, up) &&
                    Supports(poly, orb, up, small: true))
                    return true;
            }
            return false;
        }

        // Internal
        float distance = Vector3.Dot(joint.Splitter.Normal, orb.Center)
                      + joint.Splitter.D;
        float reach = orb.Radius - Eps;

        if (distance >= reach)
            return TraverseSupported(joint.Front, settled, trail, orb, up);

        if (distance <= -reach)
            return TraverseSupported(joint.Back, settled, trail, orb, up);

        if (TraverseSupported(joint.Front, settled, trail, orb, up)) return true;
        return TraverseSupported(joint.Back, settled, trail, orb, up);
    }

    private static bool TraverseSolid(
        PhysicsBspNode? joint,
        Dictionary<ushort, SettledPolygon> settled,
        Ball orb,
        bool middleVerify)
    {
        if (joint is null) return false;

        // Leaf.
        if (joint.Tag == BspTag.Leaf)
        {
            if (joint.Polygons.Count is 0) return false;
            if (middleVerify && joint.Solid is not 0)
            {
                if (KineticTelemetry.ProbePlacementFailEnabled)
                    KineticTelemetry.PreviousStanceFailSolidLeaf = true;
                return true;
            }
            if (!Overlaps(joint, orb)) return false;

            foreach (ushort polyIdent in joint.Polygons)
            {
                if (!settled.TryGetValue(polyIdent, out var poly)) continue;
                if (Touches(poly, orb))
                {
                    if (KineticTelemetry.ProbeBuildingEnabled || KineticTelemetry.ProbeIndoorBspEnabled)
                        KineticTelemetry.PreviousBspStrikePoly = poly;

                    if (KineticTelemetry.ProbePlacementFailEnabled)
                    {
                        TraverseSolidBranch(poly);
                    }
                    return true;
                }
            }
            return false;
        }

        if (!Overlaps(joint, orb)) return false;

        // Internal
        float distance = Vector3.Dot(joint.Splitter.Normal, orb.Center)
                      + joint.Splitter.D;
        float reach = orb.Radius - Eps;

        if (distance >= reach)
            return TraverseSolid(joint.Front, settled, orb, middleVerify);

        if (distance <= -reach)
            return TraverseSolid(joint.Back, settled, orb, middleVerify);

        if (distance < 0f)
        {
            if (TraverseSolid(joint.Front, settled, orb, false))
                return true;
            return TraverseSolid(joint.Back, settled, orb, middleVerify);
        }
        if (TraverseSolid(joint.Front, settled, orb, middleVerify))
            return true;
        return TraverseSolid(joint.Back, settled, orb, false);
    }

    private static void TraverseSolidBranch(SettledPolygon poly)
    {
        KineticTelemetry.PreviousStanceFailPolyIdent = poly.Id;
        KineticTelemetry.PreviousStanceFailPolyNorm = poly.Plane.Normal;
        KineticTelemetry.PreviousStanceFailPolyD = poly.Plane.D;
    }

    private static bool TraverseSolidPolyg(
        PhysicsBspNode? joint,
        Dictionary<ushort, SettledPolygon> settled,
        Ball orb,
        float radius,
        ref bool middleSolid,
        ref SettledPolygon? strikePoly,
        bool middleVerify)
    {
        if (joint is null) return middleSolid;

        // Leaf.
        if (joint.Tag == BspTag.Leaf)
        {
            if (joint.Polygons.Count is 0) return false;

            if (middleVerify && joint.Solid is not 0)
                middleSolid = true;

            if (!Overlaps(joint, orb))
                return middleSolid;

            foreach (ushort polyIdent in joint.Polygons)
            {
                if (!settled.TryGetValue(polyIdent, out var poly)) continue;
                if (Touches(poly, orb))
                {
                    strikePoly = poly;
                    return true;
                }
            }
            return middleSolid;
        }

        if (!Overlaps(joint, orb)) return middleSolid;

        // Internal
        float distance = Vector3.Dot(joint.Splitter.Normal, orb.Center)
                      + joint.Splitter.D;
        float reach = radius - Eps;

        if (distance >= reach)
            return TraverseSolidPolyg(joint.Front, settled, orb, radius,
                                                      ref middleSolid, ref strikePoly, middleVerify);

        if (distance <= -reach)
            return TraverseSolidPolyg(joint.Back, settled, orb, radius,
                                                      ref middleSolid, ref strikePoly, middleVerify);

        if (distance <= 0f)
        {
            TraverseSolidPolyg(joint.Back, settled, orb, radius,
                                               ref middleSolid, ref strikePoly, middleVerify);
            if (strikePoly is not null) return middleSolid;
            return TraverseSolidPolyg(joint.Front, settled, orb, radius,
                                                      ref middleSolid, ref strikePoly, false);
        }
        TraverseSolidPolyg(joint.Front, settled, orb, radius,
                                           ref middleSolid, ref strikePoly, middleVerify);
        if (strikePoly is not null) return middleSolid;
        return TraverseSolidPolyg(joint.Back, settled, orb, radius,
                                                  ref middleSolid, ref strikePoly, false);
    }

    private static bool TraverseStaticStrike(
        PhysicsBspNode? joint,
        Dictionary<ushort, SettledPolygon> settled,
        Vector3 middle,
        float radius,
        ref ushort strikePolyIdent,
        ref Vector3 strikeNorm)
    {
        if (joint is null) return false;

        // Broad phase
        Orb sphere = joint.Bounds;
        Vector3 d = middle - sphere.Center;
        float r = radius + sphere.Radius;
        if (d.LengthSquared() >= r * r) return false;

        if (joint.Tag == BspTag.Leaf)
        {
            foreach (ushort polyIdent in joint.Polygons)
            {
                if (!settled.TryGetValue(polyIdent, out var poly)) continue;

                Vector3 cp = Vector3.Zero;
                if (PolygStrikesOrbPrecise(poly.Plane, poly.Vertices,
                                              middle, radius, ref cp))
                {
                    strikePolyIdent = polyIdent;
                    strikeNorm = poly.Plane.Normal;
                    return true;
                }
            }
            return false;
        }

        float divideDistance = Vector3.Dot(joint.Splitter.Normal, middle) + joint.Splitter.D;
        float reach = radius - Eps;

        if (divideDistance >= reach)
            return TraverseStaticStrike(joint.Front, settled,
                                                      middle, radius, ref strikePolyIdent, ref strikeNorm);

        if (divideDistance <= -reach)
            return TraverseStaticStrike(joint.Back, settled,
                                                      middle, radius, ref strikePolyIdent, ref strikeNorm);

        if (TraverseStaticStrike(joint.Front, settled,
                middle, radius, ref strikePolyIdent, ref strikeNorm))
            return true;

        return TraverseStaticStrike(joint.Back, settled,
                middle, radius, ref strikePolyIdent, ref strikeNorm);
    }

    private static void TraverseSweptStrike(
        PhysicsBspNode? joint,
        Dictionary<ushort, SettledPolygon> settled,
        Vector3 middle,
        float radius,
        Vector3 travel,
        ref ushort strikePolyIdent,
        ref Vector3 strikeNorm,
        ref float finestMoment)
    {
        if (joint is null) return;

        // Broad phase
        Orb sphere = joint.Bounds;
        Vector3 d = middle - sphere.Center;
        float r = radius + sphere.Radius + travel.Length() + 0.1f;
        if (d.LengthSquared() >= r * r) return;

        if (joint.Tag == BspTag.Leaf)
        {
            foreach (ushort polyIdent in joint.Polygons)
            {
                if (!settled.TryGetValue(polyIdent, out var poly)) continue;

                if (Vector3.Dot(travel, poly.Plane.Normal) >= 0f) continue;

                Vector3 cp = Vector3.Zero;
                if (PolygStrikesOrbPrecise(poly.Plane, poly.Vertices, middle, radius, ref cp))
                {
                    if (0f < finestMoment)
                    {
                        finestMoment = 0f;
                        strikePolyIdent = polyIdent;
                        strikeNorm = poly.Plane.Normal;
                    }
                    continue;
                }

                // Test at end position
                Vector3 finishMiddle = middle + travel;
                if (PolygStrikesOrbPrecise(poly.Plane, poly.Vertices, finishMiddle, radius, ref cp) && 1f < finestMoment)
                {
                    finestMoment = 1f;
                    strikePolyIdent = polyIdent;
                    strikeNorm = poly.Plane.Normal;
                }
            }
            return;
        }

        float divideDistance = Vector3.Dot(joint.Splitter.Normal, middle) + joint.Splitter.D;
        float reach = radius + travel.Length();

        if (divideDistance >= reach)
        {
            TraverseSweptStrike(joint.Front, settled,
                middle, radius, travel, ref strikePolyIdent, ref strikeNorm, ref finestMoment);
            return;
        }

        if (divideDistance <= -reach)
        {
            TraverseSweptStrike(joint.Back, settled,
                middle, radius, travel, ref strikePolyIdent, ref strikeNorm, ref finestMoment);
            return;
        }

        TraverseSweptStrike(joint.Front, settled,
            middle, radius, travel, ref strikePolyIdent, ref strikeNorm, ref finestMoment);
        TraverseSweptStrike(joint.Back, settled,
            middle, radius, travel, ref strikePolyIdent, ref strikeNorm, ref finestMoment);
    }
}
