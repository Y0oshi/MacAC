using System.Numerics;
using MacAC.Dat;
using Plane = System.Numerics.Plane;

namespace MacAC.Mechanics.Kinetics;

public static partial class CellBspProbe
{

    public static bool SearchPassableOrb(
        PhysicsBspNode? trunk,
        Dictionary<ushort, SettledPolygon> settled,
        Changeover changeover,
        Orb orb,
        float sensorGap,
        Vector3 up,
        out SettledPolygon? strikePoly,
        out ushort strikePolyIdent,
        out Vector3 adjustedMiddle)
    {
        return SeekPassable(
                trunk,
                settled,
                changeover,
                new Ball(orb.Center, orb.Radius),
                sensorGap,
                up,
                out strikePoly,
                out strikePolyIdent,
                out adjustedMiddle);
    }

    public static ShiftVerdict FindCollisions(
        PhysicsBspNode? trunk,
        Dictionary<ushort, SettledPolygon> settled,
        Changeover changeover,
        Orb ownOrb,
        Orb? ownSphere1,
        Vector3 ownCurrMiddle,
        Vector3 ownSpaceZ,
        float scaling,
        Quaternion ownToRealm = default,
        KineticEngine? engine = null,
        Vector3 realmOrigin = default)
    {
        Ball sphere0 = new Ball(ownOrb.Center, ownOrb.Radius);
        bool hasSphere1 = ownSphere1 is not null;
        Ball sphere1 = hasSphere1
            ? new Ball(ownSphere1!.Center, ownSphere1.Radius)
            : default;

        return Sweep(
            trunk,
            settled,
            changeover,
            sphere0,
            hasSphere1,
            sphere1,
            ownCurrMiddle,
            ownSpaceZ,
            scaling,
            ownToRealm,
            engine,
            realmOrigin);
    }

    public static ShiftVerdict FindCollisions(
        PhysicsBspNode? trunk,
        Dictionary<ushort, Facet> polygs,
        MeshVertices verts,
        Changeover changeover,
        Orb ownOrb,
        Orb? ownSphere1,
        Vector3 ownCurrMiddle,
        Vector3 ownSpaceZ,
        float scaling)
    {
        var settled = KineticAssetCache.LocatePolygs(polygs, verts);
        return FindCollisions(trunk, settled, changeover,
                               ownOrb, ownSphere1, ownCurrMiddle, ownSpaceZ, scaling);
    }

    public static bool SphereIntersectsPoly(
        PhysicsBspNode? joint,
        Dictionary<ushort, Facet> polygs,
        MeshVertices verts,
        Vector3 orbMiddle,
        float orbRadius,
        out ushort strikePolyIdent,
        out Vector3 strikeNorm)
    {
        strikePolyIdent = 0;
        strikeNorm = Vector3.Zero;
        if (joint is null) return false;

        var settled = KineticAssetCache.LocatePolygs(polygs, verts);
        return TraverseStaticStrike(joint, settled, orbMiddle, orbRadius,
                                                  ref strikePolyIdent, ref strikeNorm);
    }

    public static bool OrbIntersectsPolyWithTime(
        PhysicsBspNode? joint,
        Dictionary<ushort, Facet> polygs,
        MeshVertices verts,
        Vector3 orbMiddle,
        float orbRadius,
        Vector3 travel,
        out ushort strikePolyIdent,
        out Vector3 strikeNorm,
        out float strikeMoment)
    {
        strikePolyIdent = 0;
        strikeNorm = Vector3.Zero;
        strikeMoment = float.MaxValue;
        if (joint is null) return false;

        var settled = KineticAssetCache.LocatePolygs(polygs, verts);

        TraverseSweptStrike(joint, settled,
            orbMiddle, orbRadius, travel,
            ref strikePolyIdent, ref strikeNorm, ref strikeMoment);

        return strikeMoment < float.MaxValue;
    }

    internal static bool SeekPassableOrb(
        PhysicsBspNode? trunk,
        Dictionary<ushort, SettledPolygon> settled,
        Changeover changeover,
        Vector3 orbMiddle,
        float orbRadius,
        float sensorGap,
        Vector3 up,
        out SettledPolygon? strikePoly,
        out ushort strikePolyIdent,
        out Vector3 adjustedMiddle)
    {
        return SeekPassable(
                trunk,
                settled,
                changeover,
                new Ball(orbMiddle, orbRadius),
                sensorGap,
                up,
                out strikePoly,
                out strikePolyIdent,
                out adjustedMiddle);
    }

    internal static ShiftVerdict SeekImpacts(
        PhysicsBspNode? trunk,
        Dictionary<ushort, SettledPolygon> settled,
        Changeover changeover,
        Vector3 ownOrbMiddle,
        float ownOrbRadius,
        bool hasOwnSphere1,
        Vector3 ownSphere1Middle,
        float ownSphere1Radius,
        Vector3 ownCurrMiddle,
        Vector3 ownSpaceZ,
        float scaling,
        Quaternion ownToRealm = default,
        KineticEngine? engine = null,
        Vector3 realmOrigin = default)
    {
        return Sweep(
                trunk,
                settled,
                changeover,
                new Ball(ownOrbMiddle, ownOrbRadius),
                hasOwnSphere1,
                hasOwnSphere1
                    ? new Ball(ownSphere1Middle, ownSphere1Radius)
                    : default,
                ownCurrMiddle,
                ownSpaceZ,
                scaling,
                ownToRealm,
                engine,
                realmOrigin);
    }

    internal static bool OrbIntersectsPoly(
        PhysicsBspNode? joint,
        Dictionary<ushort, SettledPolygon> settled,
        Vector3 orbMiddle,
        float orbRadius,
        out ushort strikePolyIdent,
        out Vector3 strikeNorm)
    {
        strikePolyIdent = 0;
        strikeNorm = Vector3.Zero;
        if (joint is null) return false;

        return TraverseStaticStrike(
            joint,
            settled,
            orbMiddle,
            orbRadius,
            ref strikePolyIdent,
            ref strikeNorm);
    }

    internal static bool OrbIntersectsPolyWithMoment(
        PhysicsBspNode? joint,
        Dictionary<ushort, SettledPolygon> settled,
        Vector3 orbMiddle,
        float orbRadius,
        Vector3 travel,
        out ushort strikePolyIdent,
        out Vector3 strikeNorm,
        out float strikeMoment)
    {
        strikePolyIdent = 0;
        strikeNorm = Vector3.Zero;
        strikeMoment = float.MaxValue;
        if (joint is null) return false;

        TraverseSweptStrike(
            joint,
            settled,
            orbMiddle,
            orbRadius,
            travel,
            ref strikePolyIdent,
            ref strikeNorm,
            ref strikeMoment);

        return strikeMoment < float.MaxValue;
    }
    private static bool BisectToWipe(
        PhysicsBspNode trunk,
        Dictionary<ushort, SettledPolygon> settled,
        ref Ball verifySpot,
        Vector3 curSpot,
        SettledPolygon strikePoly,
        Vector3 linkPt)
    {
        Vector3 travel = verifySpot.Center - curSpot;

        double wipeMoment = 0.0;
        double strikeMoment = 1.0;
        int idx = 0;

        const int UpperIter = 15;

        while (true)
        {
            float touchMoment = MomentToPlane(strikePoly, verifySpot, curSpot, travel);

            if (touchMoment == 1f) break;

            verifySpot.Center = curSpot + travel * touchMoment;

            SettledPolygon? hp2 = null;
            Vector3 cp2 = Vector3.Zero;
            if (!TraverseFacingStrike(trunk, settled, verifySpot, travel,
                                               ref hp2, ref cp2))
            {
                wipeMoment = touchMoment;
                break;
            }

            if (hp2 is not null) strikePoly = hp2;

            ++idx;
            strikeMoment = touchMoment;
            if (idx >= UpperIter) return false;
        }

        while (idx < UpperIter)
        {
            double avg = (wipeMoment + strikeMoment) * 0.5;
            verifySpot.Center = curSpot + travel * (float)avg;

            SettledPolygon? hp2 = null;
            Vector3 cp2 = Vector3.Zero;
            if (!TraverseFacingStrike(trunk, settled, verifySpot, travel,
                                               ref hp2, ref cp2))
                wipeMoment = avg;
            else
                strikeMoment = avg;

            if (strikeMoment - wipeMoment < 0.02) break;
            ++idx;
        }

        verifySpot.Center = curSpot + travel * (float)wipeMoment;
        return true;
    }

    private static ShiftVerdict PassableVerdict(
        PhysicsBspNode trunk,
        Dictionary<ushort, SettledPolygon> settled,
        SweepPath trail,
        Ball verifySpot,
        Vector3 up)
    {
        Ball validSpot = verifySpot;
        return TraverseSupported(trunk, settled, trail, validSpot, up)
            ? ShiftVerdict.Collided
            : ShiftVerdict.OK;
    }

    private static ShiftVerdict SweepLoop(
        PhysicsBspNode trunk,
        Dictionary<ushort, SettledPolygon> settled,
        Changeover changeover,
        Ball verifySpot,
        Vector3 up,
        float scaling,
        Quaternion ownToRealm = default,
        Vector3 realmOrigin = default)
    {
        if (ownToRealm == default) ownToRealm = Quaternion.Identity;

        SweepPath trail = changeover.SweepPath;
        ContactLedger impacts = changeover.ContactLedger;

        float hopDownQuantity = -(trail.StepDownAmt * trail.WalkInterp);
        Vector3 travel = up * hopDownQuantity * (1f / scaling);

        var validSpot = verifySpot;
        bool altered = false;
        SettledPolygon? polyStrike = null;
        ushort _polyIdent = 0;   // step-down doesn't need the id, but the signature requires it

        TraversePassable(trunk, settled, trail, ref validSpot, travel, up,
                              ref polyStrike, ref _polyIdent, ref altered);

        if (altered && polyStrike is not null)
        {
            Vector3 adjusted = validSpot.Center - verifySpot.Center;
            Vector3 shift = Vector3.Transform(adjusted, ownToRealm) * scaling;
            trail.AppendShiftToVerifySpot(shift);

            Vector3 realmNorm = RealmNorm(polyStrike.Plane.Normal, ownToRealm);
            Plane realmPlane = RealmPlane(
                realmNorm,
                polyStrike.Vertices,
                ownToRealm,
                scaling,
                realmOrigin);
            impacts.AssignLinkPlane(realmPlane, trail.CheckCellId, false);

            trail.AssignPassableTransformed(
                realmPlane,
                polyStrike.Vertices,
                ownToRealm,
                scaling,
                realmOrigin,
                Vector3.UnitZ);

            if (KineticTelemetry.ProbeBuildingEnabled || KineticTelemetry.ProbeIndoorBspEnabled)
                KineticTelemetry.PreviousBspStrikePoly = polyStrike;

            return ShiftVerdict.Adjusted;
        }

        return ShiftVerdict.OK;
    }

    private static bool SeekPassable(
        PhysicsBspNode? trunk,
        Dictionary<ushort, SettledPolygon> settled,
        Changeover changeover,
        Ball validSpot,
        float sensorGap,
        Vector3 up,
        out SettledPolygon? strikePoly,
        out ushort strikePolyIdent,
        out Vector3 adjustedMiddle)
    {
        adjustedMiddle = validSpot.Center;
        strikePoly = null;
        strikePolyIdent = 0;

        if (trunk is null) return false;

        Vector3 travel = -up * sensorGap;
        bool altered = false;
        ushort polyIdent = 0;
        SettledPolygon? poly = null;

        TraversePassable(trunk, settled, changeover.SweepPath, ref validSpot,
                              travel, up, ref poly, ref polyIdent, ref altered);

        if (altered && poly is not null)
        {
            strikePoly = poly;
            strikePolyIdent = polyIdent;
            adjustedMiddle = validSpot.Center;
            return true;
        }

        return false;
    }

    private static ShiftVerdict SweepLoop2(
        Changeover changeover,
        Vector3 impactNorm,
        KineticEngine engine)
    {
        bool stepped = changeover.DoStepUp(impactNorm, engine!);

        if (KineticTelemetry.ProbeIndoorBspEnabled)
        {
            SweepPath path = changeover.SweepPath;
            Console.WriteLine(FormattableString.Invariant(
                $"[stepsphereup] cell=0x{path.CheckCellId:X8} stepUpFlag={path.StepUp} stepDownFlag={path.StepDown} n=({impactNorm.X:F2},{impactNorm.Y:F2},{impactNorm.Z:F2}) stepped={stepped} pos=({path.CheckPos.X:F3},{path.CheckPos.Y:F3},{path.CheckPos.Z:F3})"));
        }

        if (stepped)
            return ShiftVerdict.OK;

        ShiftVerdict slideRes = changeover.SweepPath.StepUpSlide(changeover);
        if (KineticTelemetry.ProbeIndoorBspEnabled)
            Console.WriteLine(FormattableString.Invariant(
                $"[stepsphereup] cell=0x{changeover.SweepPath.CheckCellId:X8} → StepUpSlide={slideRes}"));
        return slideRes;
    }

    private static ShiftVerdict Shift(
        Changeover changeover,
        Vector3 realmNorm)
    {
        return changeover.ShiftOrbInternal(
                realmNorm, changeover.SweepPath.GlobalCurrCenter[0].Center);
    }

    private static ShiftVerdict ClipAgainst(
        PhysicsBspNode trunk,
        Dictionary<ushort, SettledPolygon> settled,
        Changeover changeover,
        Ball verifySpot,
        Vector3 curSpot,
        SettledPolygon strikePoly,
        Vector3 linkPt,
        float scaling,
        Quaternion ownToRealm = default)
    {
        if (ownToRealm == default) ownToRealm = Quaternion.Identity;

        MoverFacts objRef = changeover.MoverFacts;
        SweepPath trail = changeover.SweepPath;
        ContactLedger impacts = changeover.ContactLedger;

        Vector3 impactNorm = Vector3.Transform(strikePoly.Plane.Normal, ownToRealm);

        if ((objRef.State & MoverState.PerfectClip) == 0)
        {
            impacts.AssignImpactNorm(impactNorm);
            if (KineticTelemetry.ProbeBuildingEnabled || KineticTelemetry.ProbeIndoorBspEnabled)
                KineticTelemetry.PreviousBspStrikePoly = strikePoly;
            return ShiftVerdict.Collided;
        }

        var validSpot = verifySpot;

        if (!BisectToWipe(trunk, settled, ref validSpot, curSpot, strikePoly, linkPt))
        {
            if (KineticTelemetry.ProbeBuildingEnabled || KineticTelemetry.ProbeIndoorBspEnabled)
                KineticTelemetry.PreviousBspStrikePoly = strikePoly;
            return ShiftVerdict.Collided;
        }

        impacts.AssignImpactNorm(impactNorm);
        if (KineticTelemetry.ProbeBuildingEnabled || KineticTelemetry.ProbeIndoorBspEnabled)
            KineticTelemetry.PreviousBspStrikePoly = strikePoly;

        Vector3 adjusted = validSpot.Center - verifySpot.Center;
        Vector3 shift = Vector3.Transform(adjusted, ownToRealm) * scaling;
        trail.AppendShiftToVerifySpot(shift);

        return ShiftVerdict.Adjusted;
    }

    private static ShiftVerdict NoteNearbyMiss(
        SweepPath trail,
        SettledPolygon strikePoly,
        bool hopUp,
        Quaternion ownToRealm = default)
    {
        if (ownToRealm == default) ownToRealm = Quaternion.Identity;
        trail.NegPolyHit = true;
        trail.NegStepUp = hopUp;
        trail.NegCollisionNormal = Vector3.Transform(strikePoly.Plane.Normal, ownToRealm);

        if (KineticTelemetry.ProbeIndoorBspEnabled)
        {
            Vector3 nl = strikePoly.Plane.Normal;
            Console.WriteLine(FormattableString.Invariant(
                $"[neg-poly] cell=0x{trail.CheckCellId:X8} stepUp={hopUp} stepDownFlag={trail.StepDown} poly=0x{strikePoly.Id:X4} nLocal=({nl.X:F3},{nl.Y:F3},{nl.Z:F3}) sides={strikePoly.SidesType} checkPos=({trail.CheckPos.X:F3},{trail.CheckPos.Y:F3},{trail.CheckPos.Z:F3})"));
        }
        return ShiftVerdict.OK;
    }

    private static ShiftVerdict Slot(
        PhysicsBspNode trunk,
        Dictionary<ushort, SettledPolygon> settled,
        Changeover changeover,
        bool wipeChamber)
    {
        SweepPath trail = changeover.SweepPath;

        var ball = new Ball(
            trail.LocalSphere[0].Center,
            trail.LocalSphere[0].Radius);

        float rad = ball.Radius;

        bool hasS1 = trail.NumSphere > 1;
        Ball s1 = default;
        if (hasS1)
            s1 = new Ball(
                trail.LocalSphere[1].Center,
                trail.LocalSphere[1].Radius);

        SettledPolygon? strikePoly = null;

        const int UpperIter = 20;
        for (int idx = 0; idx < UpperIter; ++idx)
        {
            bool middleSolid = false;
            strikePoly = null;

            if (TraverseSolidPolyg(trunk, settled, ball, rad,
                                                   ref middleSolid, ref strikePoly, wipeChamber))
            {
                if (strikePoly is not null)
                {
                    NudgeOffPolyg(
                        strikePoly,
                        ref ball,
                        ref s1,
                        hasS1,
                        rad,
                        middleSolid,
                        wipeChamber);
                    continue;
                }
            }
            else
            {
                if (hasS1)
                {
                    middleSolid = false;
                    strikePoly = null;

                    if (TraverseSolidPolyg(trunk, settled, s1, rad,
                                                           ref middleSolid, ref strikePoly, wipeChamber))
                    {
                        if (strikePoly is not null)
                        {
                            NudgeOffPolyg(
                                strikePoly,
                                ref s1,
                                ref ball,
                                true,
                                rad,
                                middleSolid,
                                wipeChamber);
                            continue;
                        }
                    }
                    else
                    {
                        return SlotVerdict(ball, trail, idx);
                    }
                }
                else
                {
                    return SlotVerdict(ball, trail, idx);
                }
            }

            rad *= 2f;
        }
        return ShiftVerdict.Collided;
    }

    private static ShiftVerdict SlotVerdict(
        Ball ball,
        SweepPath trail,
        int iteration)
    {
        if (iteration is 0) return ShiftVerdict.OK;

        Vector3 adjust = ball.Center - trail.LocalSphere[0].Center;
        trail.AppendShiftToVerifySpot(adjust);
        return ShiftVerdict.Adjusted;
    }

    private static ShiftVerdict Sweep(
        PhysicsBspNode? trunk,
        Dictionary<ushort, SettledPolygon> settled,
        Changeover changeover,
        Ball sphere0,
        bool hasSphere1,
        Ball sphere1,
        Vector3 ownCurrMiddle,
        Vector3 ownSpaceZ,
        float scaling,
        Quaternion ownToRealm,
        KineticEngine? engine,
        Vector3 realmOrigin)
    {
        if (trunk is null) return ShiftVerdict.OK;
        if (ownToRealm == default) ownToRealm = Quaternion.Identity;

        SweepPath trail = changeover.SweepPath;
        ContactLedger impacts = changeover.ContactLedger;
        MoverFacts objRef = changeover.MoverFacts;

        Vector3 travel = sphere0.Center - ownCurrMiddle;

        if (KineticTelemetry.ProbeIndoorBspEnabled)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"[path-dispatch] insertType={trail.InsertType} obstructionEthereal={trail.ObstructionEthereal} checkWalkable={trail.CheckWalkable} stepDown={trail.StepDown} collide={trail.Collide} contact={((objRef.State & MoverState.Contact) != 0)} hasSphere1={hasSphere1}"));
        }

        if (KineticTelemetry.ProbePushBackEnabled)
        {
            KineticTelemetry.TracePushBackRelay(
                orbMiddle: sphere0.Center,
                travel: travel,
                collide: trail.Collide,
                slotKind: (int)trail.InsertType,
                objRefPhase: unchecked((int)objRef.State),
                strollLerpListing: trail.WalkInterp,
                returnPhase: -1);
        }

        Vector3 L2W(Vector3 v) => Vector3.Transform(v, ownToRealm);

        if (trail.InsertType == SlotKind.Placement || trail.ObstructionEthereal)
        {
            bool wipeChamber = !(trail.BldgCheck && trail.HitsInteriorCell);

            if (KineticTelemetry.ProbePlacementFailEnabled)
            {
                KineticTelemetry.PreviousStanceFailPolyIdent = 0;
                KineticTelemetry.PreviousStanceFailSolidLeaf = false;
            }

            if (TraverseSolid(trunk, settled, sphere0, wipeChamber))
            {
                if (KineticTelemetry.ProbePlacementFailEnabled)
                    KineticTelemetry.TraceStanceFail(
                        "Path1.sphere0", sphere0.Center, sphere0.Radius, 0,
                        trail.CheckCellId, realmOrigin, objRef.Ethereal);
                return ShiftVerdict.Collided;
            }

            if (KineticTelemetry.ProbePlacementFailEnabled)
            {
                KineticTelemetry.PreviousStanceFailPolyIdent = 0;
                KineticTelemetry.PreviousStanceFailSolidLeaf = false;
            }

            if (hasSphere1 &&
                TraverseSolid(trunk, settled, sphere1, wipeChamber))
            {
                if (KineticTelemetry.ProbePlacementFailEnabled)
                    KineticTelemetry.TraceStanceFail(
                        "Path1.sphere1", sphere1.Center, sphere1.Radius, 1,
                        trail.CheckCellId, realmOrigin, objRef.Ethereal);
                return ShiftVerdict.Collided;
            }

            return ShiftVerdict.OK;
        }

        if (trail.CheckWalkable)
        {
            return PassableVerdict(trunk, settled, trail, sphere0, ownSpaceZ);
        }

        if (trail.StepDown)
        {
            return SweepLoop(trunk, settled, changeover, sphere0, ownSpaceZ, scaling, ownToRealm, realmOrigin);
        }

        if (trail.Collide)
        {
            var validSpot = sphere0;
            SettledPolygon? strikePoly = null;
            ushort _strikePolyIdent = 0;   // Path 4 doesn't need the id
            bool altered = false;

            TraversePassable(trunk, settled, trail, ref validSpot, travel, ownSpaceZ,
                                  ref strikePoly, ref _strikePolyIdent, ref altered);

            if (altered && strikePoly is not null)
            {
                Vector3 ownShift = validSpot.Center - sphere0.Center;
                Vector3 realmShift = L2W(ownShift) * scaling;
                trail.AppendShiftToVerifySpot(realmShift);

                Vector3 realmNorm = RealmNorm(strikePoly.Plane.Normal, ownToRealm);
                Plane realmPlane = RealmPlane(
                    realmNorm,
                    strikePoly.Vertices,
                    ownToRealm,
                    scaling,
                    realmOrigin);
                impacts.AssignLinkPlane(realmPlane, trail.CheckCellId, false);
                trail.AssignPassableTransformed(
                    realmPlane,
                    strikePoly.Vertices,
                    ownToRealm,
                    scaling,
                    realmOrigin,
                    Vector3.UnitZ);

                if (KineticTelemetry.ProbeBuildingEnabled || KineticTelemetry.ProbeIndoorBspEnabled)
                    KineticTelemetry.PreviousBspStrikePoly = strikePoly;

                return ShiftVerdict.Adjusted;
            }
            return ShiftVerdict.OK;
        }

        if ((objRef.State & MoverState.Contact) != 0)
        {
            SettledPolygon? strikePoly0 = null;
            Vector3 contact0 = Vector3.Zero;

            bool hit0 = TraverseFacingStrike(trunk, settled, sphere0, travel,
                                                      ref strikePoly0, ref contact0);

            if (KineticTelemetry.ProbeIndoorBspEnabled)
            {
                Console.WriteLine(FormattableString.Invariant(
                    $"[path5-diag] hit0={hit0} hitPoly0={(strikePoly0 is not null)} hasSphere1={hasSphere1}"));
            }

            if (hit0)
            {
                // Full hit - step_sphere_up
                if (KineticTelemetry.ProbeBuildingEnabled || KineticTelemetry.ProbeIndoorBspEnabled)
                    KineticTelemetry.PreviousBspStrikePoly = strikePoly0;

                Vector3 realmNorm = L2W(strikePoly0!.Plane.Normal);
                if (engine is not null && !trail.StepUp && !trail.StepDown)
                    return SweepLoop2(changeover, realmNorm, engine);

                return Shift(changeover, realmNorm);
            }

            if (hasSphere1)
            {
                SettledPolygon? strikePoly1 = null;
                Vector3 contact1 = Vector3.Zero;
                bool hit1 = TraverseFacingStrike(trunk, settled, sphere1, travel,
                                                         ref strikePoly1, ref contact1);

                if (KineticTelemetry.ProbeIndoorBspEnabled)
                {
                    Console.WriteLine(FormattableString.Invariant(
                        $"[path5-diag] hit1={hit1} hitPoly1={(strikePoly1 is not null)}"));
                }

                if (hit1)
                {
                    if (KineticTelemetry.ProbeBuildingEnabled || KineticTelemetry.ProbeIndoorBspEnabled)
                        KineticTelemetry.PreviousBspStrikePoly = strikePoly1;

                    Vector3 realmNorm = L2W(strikePoly1!.Plane.Normal);
                    return Shift(changeover, realmNorm);
                }

                if (strikePoly1 is not null)
                {
                    if (KineticTelemetry.ProbeBuildingEnabled || KineticTelemetry.ProbeIndoorBspEnabled)
                        KineticTelemetry.PreviousBspStrikePoly = strikePoly1;
                    NoteNearbyMiss(trail, strikePoly1, hopUp: false, ownToRealm);
                    return ShiftVerdict.OK;
                }

                if (strikePoly0 is not null)
                {
                    if (KineticTelemetry.ProbeBuildingEnabled || KineticTelemetry.ProbeIndoorBspEnabled)
                        KineticTelemetry.PreviousBspStrikePoly = strikePoly0;
                    NoteNearbyMiss(trail, strikePoly0, hopUp: true, ownToRealm);
                    return ShiftVerdict.OK;
                }
            }

            return ShiftVerdict.OK;
        }

        {
            SettledPolygon? strikePoly0 = null;
            Vector3 contact0 = Vector3.Zero;

            bool hit0 = TraverseFacingStrike(trunk, settled, sphere0, travel,
                                                      ref strikePoly0, ref contact0);

            if (hit0 || strikePoly0 is not null)
            {
                if ((objRef.State & MoverState.PathClipped) != 0)
                {
                    return ClipAgainst(trunk, settled, changeover,
                                          sphere0, ownCurrMiddle,
                                          strikePoly0!, contact0, scaling, ownToRealm);
                }

                Vector3 realmNormal0 = L2W(strikePoly0!.Plane.Normal);
                trail.AssignCollide(realmNormal0);
                trail.WalkableAllowance = KineticConstants.LandingZ;
                if (KineticTelemetry.ProbeBuildingEnabled || KineticTelemetry.ProbeIndoorBspEnabled)
                    KineticTelemetry.PreviousBspStrikePoly = strikePoly0;
                return ShiftVerdict.Adjusted;
            }

            if (hasSphere1)
            {
                SettledPolygon? strikePoly1 = null;
                Vector3 contact1 = Vector3.Zero;

                bool hit1 = TraverseFacingStrike(trunk, settled, sphere1, travel,
                                                          ref strikePoly1, ref contact1);

                if (hit1 || strikePoly1 is not null)
                {
                    Vector3 realmNormal1 = L2W(strikePoly1!.Plane.Normal);

                    impacts.AssignImpactNorm(realmNormal1);
                    if (KineticTelemetry.ProbeBuildingEnabled || KineticTelemetry.ProbeIndoorBspEnabled)
                        KineticTelemetry.PreviousBspStrikePoly = strikePoly1;
                    return ShiftVerdict.Collided;
                }
            }
        }

        return ShiftVerdict.OK;
    }
}
