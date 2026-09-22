using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

// The retail find_collisions dispatch over a packed tree, plus the public entry points
internal static partial class PackedBspQuery
{

    public static bool SeekPassableOrb(
        PackedKineticBsp tree,
        Changeover changeover,
        Vector3 orbMiddle,
        float orbRadius,
        float sensorGap,
        Vector3 up,
        out int strikePolygOrdinal,
        out ushort strikePolygIdent,
        out Vector3 adjustedMiddle)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(changeover);

        Ball validLocus =
            new(orbMiddle, orbRadius);
        adjustedMiddle = validLocus.Center;
        strikePolygOrdinal = -1;
        strikePolygIdent = 0;

        if (tree.TrunkOrdinal < 0)
            return false;

        Vector3 travel = -up * sensorGap;
        bool altered = false;
        int polygOrdinal = -1;
        ushort polygIdent = 0;

        TraversePassable(
            tree,
            tree.TrunkOrdinal,
            changeover.SweepPath,
            ref validLocus,
            travel,
            up,
            ref polygOrdinal,
            ref polygIdent,
            ref altered);

        if (altered && polygOrdinal >= 0)
        {
            strikePolygOrdinal = polygOrdinal;
            strikePolygIdent = polygIdent;
            adjustedMiddle = validLocus.Center;
            return true;
        }

        return false;
    }

    public static ShiftVerdict SearchImpacts(
        PackedKineticBsp tree,
        Changeover changeover,
        Orb ownOrb,
        Orb? ownSphere1,
        Vector3 ownLatestMiddle,
        Vector3 ownSpaceZ,
        float scaling,
        Quaternion ownToRealm = default,
        KineticEngine? engine = null,
        Vector3 realmOrigin = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(changeover);

        Ball sphere0 =
            new(ownOrb.Center, ownOrb.Radius);
        bool hasSphere1 = ownSphere1 is not null;
        Ball sphere1 = hasSphere1
            ? new Ball(ownSphere1!.Center, ownSphere1.Radius)
            : default;

        return Sweep(
            tree,
            changeover,
            sphere0,
            hasSphere1,
            sphere1,
            ownLatestMiddle,
            ownSpaceZ,
            scaling,
            ownToRealm,
            engine,
            realmOrigin);
    }

    public static bool SphereIntersectsPoly(
        PackedKineticBsp tree,
        Vector3 orbMiddle,
        float orbRadius,
        out ushort strikePolygIdent,
        out Vector3 strikeNorm)
    {
        ArgumentNullException.ThrowIfNull(tree);
        strikePolygIdent = 0;
        strikeNorm = Vector3.Zero;
        if (tree.TrunkOrdinal < 0)
            return false;

        return TraverseStaticStrike(
            tree,
            tree.TrunkOrdinal,
            orbMiddle,
            orbRadius,
            ref strikePolygIdent,
            ref strikeNorm);
    }

    public static bool OrbIntersectsPolyWithMoment(
        PackedKineticBsp tree,
        Vector3 orbMiddle,
        float orbRadius,
        Vector3 travel,
        out ushort strikePolygIdent,
        out Vector3 strikeNorm,
        out float strikeMoment)
    {
        ArgumentNullException.ThrowIfNull(tree);
        strikePolygIdent = 0;
        strikeNorm = Vector3.Zero;
        strikeMoment = float.MaxValue;
        if (tree.TrunkOrdinal < 0)
            return false;

        TraverseSweptStrike(
            tree,
            tree.TrunkOrdinal,
            orbMiddle,
            orbRadius,
            travel,
            ref strikePolygIdent,
            ref strikeNorm,
            ref strikeMoment);

        return strikeMoment < float.MaxValue;
    }

    internal static ShiftVerdict SeekImpacts(
        PackedKineticBsp tree,
        Changeover changeover,
        Vector3 ownOrbMiddle,
        float ownOrbRadius,
        bool hasOwnSphere1,
        Vector3 ownSphere1Middle,
        float ownSphere1Radius,
        Vector3 ownLatestMiddle,
        Vector3 ownSpaceZ,
        float scaling,
        Quaternion ownToRealm = default,
        KineticEngine? engine = null,
        Vector3 realmOrigin = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(changeover);

        return Sweep(
            tree,
            changeover,
            new Ball(ownOrbMiddle, ownOrbRadius),
            hasOwnSphere1,
            hasOwnSphere1
                ? new Ball(
                    ownSphere1Middle,
                    ownSphere1Radius)
                : default,
            ownLatestMiddle,
            ownSpaceZ,
            scaling,
            ownToRealm,
            engine,
            realmOrigin);
    }
    private static bool BisectToWipe(
        PackedKineticBsp tree,
        ref Ball verifyLocus,
        Vector3 latestLocus,
        int strikePolygOrdinal,
        Vector3 linkPt)
    {
        Vector3 travel = verifyLocus.Center - latestLocus;
        double wipeMoment = 0.0;
        double strikeMoment = 1.0;
        int iteration = 0;
        const int UpperIterations = 15;

        while (true)
        {
            float touchMoment = MomentToPlane(
                tree,
                strikePolygOrdinal,
                verifyLocus,
                latestLocus,
                travel);
            if (touchMoment == 1f)
                break;

            verifyLocus.Center = latestLocus + travel * touchMoment;

            int upcomingStrikePolygOrdinal = -1;
            Vector3 upcomingLinkPt = Vector3.Zero;
            if (!TraverseFacingStrike(
                    tree,
                    tree.TrunkOrdinal,
                    verifyLocus,
                    travel,
                    ref upcomingStrikePolygOrdinal,
                    ref upcomingLinkPt))
            {
                wipeMoment = touchMoment;
                break;
            }

            if (upcomingStrikePolygOrdinal >= 0)
                strikePolygOrdinal = upcomingStrikePolygOrdinal;

            ++iteration;
            strikeMoment = touchMoment;
            if (iteration >= UpperIterations)
                return false;
        }

        while (iteration < UpperIterations)
        {
            double average = (wipeMoment + strikeMoment) * 0.5;
            verifyLocus.Center =
                latestLocus + travel * (float)average;

            int upcomingStrikePolygOrdinal = -1;
            Vector3 upcomingLinkPt = Vector3.Zero;
            if (TraverseFacingStrike(
                    tree,
                    tree.TrunkOrdinal,
                    verifyLocus,
                    travel,
                    ref upcomingStrikePolygOrdinal,
                    ref upcomingLinkPt))
            {
                strikeMoment = average;
            }
            else
            {
                wipeMoment = average;
            }

            if (strikeMoment - wipeMoment < 0.02)
                break;
            ++iteration;
        }

        verifyLocus.Center = latestLocus + travel * (float)wipeMoment;
        return true;
    }

    private static ShiftVerdict PassableVerdict(
        PackedKineticBsp tree,
        SweepPath trail,
        Ball verifyLocus,
        Vector3 up)
    {
        Ball validLocus = verifyLocus;
        return TraverseSupported(
            tree,
            tree.TrunkOrdinal,
            trail,
            validLocus,
            up)
            ? ShiftVerdict.Collided
            : ShiftVerdict.OK;
    }

    private static ShiftVerdict SweepLoop(
        PackedKineticBsp tree,
        Changeover changeover,
        Ball verifyLocus,
        Vector3 up,
        float scaling,
        Quaternion ownToRealm = default,
        Vector3 realmOrigin = default)
    {
        if (ownToRealm == default)
            ownToRealm = Quaternion.Identity;

        SweepPath trail = changeover.SweepPath;
        var impacts = changeover.ContactLedger;
        float hopDownQuantity = -(trail.StepDownAmt * trail.WalkInterp);
        Vector3 travel = up * hopDownQuantity * (1f / scaling);

        Ball validLocus = verifyLocus;
        bool altered = false;
        int strikePolygOrdinal = -1;
        ushort strikePolygIdent = 0;

        TraversePassable(
            tree,
            tree.TrunkOrdinal,
            trail,
            ref validLocus,
            travel,
            up,
            ref strikePolygOrdinal,
            ref strikePolygIdent,
            ref altered);

        if (altered && strikePolygOrdinal >= 0)
        {
            var strikePolyg =
                tree.PolygChart.Polygons[strikePolygOrdinal];
            var verts = CornersOf(tree, strikePolyg);
            Vector3 adjusted = validLocus.Center - verifyLocus.Center;
            Vector3 shift =
                Vector3.Transform(adjusted, ownToRealm) * scaling;
            trail.AppendShiftToVerifySpot(shift);

            Vector3 realmNorm =
                RealmNorm(strikePolyg.Plane.Normal, ownToRealm);
            Plane realmPlane = RealmPlane(
                realmNorm,
                verts,
                ownToRealm,
                scaling,
                realmOrigin);
            impacts.AssignLinkPlane(realmPlane, trail.CheckCellId, false);
            trail.AssignPassableTransformed(
                realmPlane,
                verts,
                ownToRealm,
                scaling,
                realmOrigin,
                Vector3.UnitZ);

            NoteStrikeForSensors(tree, strikePolygOrdinal);
            return ShiftVerdict.Adjusted;
        }

        return ShiftVerdict.OK;
    }

    private static ShiftVerdict SweepLoop2(
        Changeover changeover,
        Vector3 impactNorm,
        KineticEngine engine)
    {
        bool stepped = changeover.DoStepUp(impactNorm, engine);
        if (KineticTelemetry.ProbeIndoorBspEnabled)
        {
            SweepPath trail = changeover.SweepPath;
            Console.WriteLine(FormattableString.Invariant(
                $"[stepsphereup] cell=0x{trail.CheckCellId:X8} stepUpFlag={trail.StepUp} stepDownFlag={trail.StepDown} n=({impactNorm.X:F2},{impactNorm.Y:F2},{impactNorm.Z:F2}) stepped={stepped} pos=({trail.CheckPos.X:F3},{trail.CheckPos.Y:F3},{trail.CheckPos.Z:F3})"));
        }

        if (stepped)
            return ShiftVerdict.OK;

        var slideOutcome =
            changeover.SweepPath.StepUpSlide(changeover);
        if (KineticTelemetry.ProbeIndoorBspEnabled)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"[stepsphereup] cell=0x{changeover.SweepPath.CheckCellId:X8} → StepUpSlide={slideOutcome}"));
        }

        return slideOutcome;
    }

    private static ShiftVerdict Shift(
        Changeover changeover,
        Vector3 realmNorm)
    {
        return changeover.ShiftOrbInternal(
                realmNorm,
                changeover.SweepPath.GlobalCurrCenter[0].Center);
    }

    private static ShiftVerdict ClipAgainst(
        PackedKineticBsp tree,
        Changeover changeover,
        Ball verifyLocus,
        Vector3 latestLocus,
        int strikePolygOrdinal,
        Vector3 linkPt,
        float scaling,
        Quaternion ownToRealm = default)
    {
        if (ownToRealm == default)
            ownToRealm = Quaternion.Identity;

        var strikePolyg =
            tree.PolygChart.Polygons[strikePolygOrdinal];
        MoverFacts objectDetails = changeover.MoverFacts;
        SweepPath trail = changeover.SweepPath;
        var impacts = changeover.ContactLedger;
        Vector3 impactNorm =
            Vector3.Transform(strikePolyg.Plane.Normal, ownToRealm);

        if ((objectDetails.State & MoverState.PerfectClip) == 0)
        {
            impacts.AssignImpactNorm(impactNorm);
            NoteStrikeForSensors(tree, strikePolygOrdinal);
            return ShiftVerdict.Collided;
        }

        Ball validLocus = verifyLocus;
        if (!BisectToWipe(
                tree,
                ref validLocus,
                latestLocus,
                strikePolygOrdinal,
                linkPt))
        {
            NoteStrikeForSensors(tree, strikePolygOrdinal);
            return ShiftVerdict.Collided;
        }

        impacts.AssignImpactNorm(impactNorm);
        NoteStrikeForSensors(tree, strikePolygOrdinal);
        Vector3 adjusted = validLocus.Center - verifyLocus.Center;
        Vector3 shift =
            Vector3.Transform(adjusted, ownToRealm) * scaling;
        trail.AppendShiftToVerifySpot(shift);
        return ShiftVerdict.Adjusted;
    }

    private static ShiftVerdict NoteNearbyMiss(
        PackedKineticBsp tree,
        SweepPath trail,
        int strikePolygOrdinal,
        bool hopUp,
        Quaternion ownToRealm = default)
    {
        if (ownToRealm == default)
            ownToRealm = Quaternion.Identity;

        var strikePolyg =
            tree.PolygChart.Polygons[strikePolygOrdinal];
        trail.NegPolyHit = true;
        trail.NegStepUp = hopUp;
        trail.NegCollisionNormal =
            Vector3.Transform(strikePolyg.Plane.Normal, ownToRealm);

        if (KineticTelemetry.ProbeIndoorBspEnabled)
        {
            Vector3 norm = strikePolyg.Plane.Normal;
            Console.WriteLine(FormattableString.Invariant(
                $"[neg-poly] cell=0x{trail.CheckCellId:X8} stepUp={hopUp} stepDownFlag={trail.StepDown} poly=0x{strikePolyg.Id:X4} nLocal=({norm.X:F3},{norm.Y:F3},{norm.Z:F3}) sides={strikePolyg.SidesType} checkPos=({trail.CheckPos.X:F3},{trail.CheckPos.Y:F3},{trail.CheckPos.Z:F3})"));
        }

        return ShiftVerdict.OK;
    }

    private static ShiftVerdict Slot(
        PackedKineticBsp tree,
        Changeover changeover,
        bool wipeChamber)
    {
        SweepPath trail = changeover.SweepPath;
        Ball sphere0 = new(
            trail.LocalSphere[0].Center,
            trail.LocalSphere[0].Radius);
        float radius = sphere0.Radius;

        bool hasSphere1 = trail.NumSphere > 1;
        Ball sphere1 = default;
        if (hasSphere1)
        {
            sphere1 = new Ball(
                trail.LocalSphere[1].Center,
                trail.LocalSphere[1].Radius);
        }

        const int UpperIterations = 20;
        for (int iteration = 0; iteration < UpperIterations; ++iteration)
        {
            bool middleSolid = false;
            int strikePolygOrdinal = -1;
            if (TraverseSolidPolyg(
                    tree,
                    tree.TrunkOrdinal,
                    sphere0,
                    radius,
                    ref middleSolid,
                    ref strikePolygOrdinal,
                    wipeChamber))
            {
                if (strikePolygOrdinal >= 0)
                {
                    NudgeOffPolyg(
                        tree,
                        strikePolygOrdinal,
                        ref sphere0,
                        ref sphere1,
                        hasSphere1,
                        radius,
                        middleSolid,
                        wipeChamber);
                    continue;
                }
            }
            else
            {
                if (hasSphere1)
                {
                    middleSolid = false;
                    strikePolygOrdinal = -1;
                    if (TraverseSolidPolyg(
                            tree,
                            tree.TrunkOrdinal,
                            sphere1,
                            radius,
                            ref middleSolid,
                            ref strikePolygOrdinal,
                            wipeChamber))
                    {
                        if (strikePolygOrdinal >= 0)
                        {
                            NudgeOffPolyg(
                                tree,
                                strikePolygOrdinal,
                                ref sphere1,
                                ref sphere0,
                                hasValidPosition2: true,
                                radius,
                                middleSolid,
                                wipeChamber);
                            continue;
                        }
                    }
                    else
                    {
                        return SlotVerdict(sphere0, trail, iteration);
                    }
                }
                else
                {
                    return SlotVerdict(sphere0, trail, iteration);
                }
            }

            radius *= 2f;
        }

        return ShiftVerdict.Collided;
    }

    private static ShiftVerdict SlotVerdict(
        Ball sphere0,
        SweepPath trail,
        int iteration)
    {
        if (iteration is 0)
            return ShiftVerdict.OK;

        Vector3 adjustment =
            sphere0.Center - trail.LocalSphere[0].Center;
        trail.AppendShiftToVerifySpot(adjustment);
        return ShiftVerdict.Adjusted;
    }

    private static ShiftVerdict Sweep(
        PackedKineticBsp tree,
        Changeover changeover,
        Ball sphere0,
        bool hasSphere1,
        Ball sphere1,
        Vector3 ownLatestMiddle,
        Vector3 ownSpaceZ,
        float scaling,
        Quaternion ownToRealm,
        KineticEngine? engine,
        Vector3 realmOrigin)
    {
        if (tree.TrunkOrdinal < 0)
            return ShiftVerdict.OK;
        if (ownToRealm == default)
            ownToRealm = Quaternion.Identity;

        SweepPath trail = changeover.SweepPath;
        var impacts = changeover.ContactLedger;
        MoverFacts objectDetails = changeover.MoverFacts;
        Vector3 travel = sphere0.Center - ownLatestMiddle;

        if (KineticTelemetry.ProbePushBackEnabled)
        {
            KineticTelemetry.TracePushBackRelay(
                orbMiddle: sphere0.Center,
                travel,
                collide: trail.Collide,
                slotKind: (int)trail.InsertType,
                objRefPhase: unchecked((int)objectDetails.State),
                strollLerpListing: trail.WalkInterp,
                returnPhase: -1);
        }

        Vector3 OwnToRealm(Vector3 val)
            => Vector3.Transform(val, ownToRealm);

        // Path 1: Placement or obstruction-ethereal
        if (trail.InsertType == SlotKind.Placement ||
            trail.ObstructionEthereal)
        {
            bool wipeChamber =
                !(trail.BldgCheck && trail.HitsInteriorCell);

            if (KineticTelemetry.ProbePlacementFailEnabled)
            {
                KineticTelemetry.PreviousStanceFailPolyIdent = 0;
                KineticTelemetry.PreviousStanceFailSolidLeaf = false;
            }

            if (TraverseSolid(
                    tree,
                    tree.TrunkOrdinal,
                    sphere0,
                    wipeChamber))
            {
                if (KineticTelemetry.ProbePlacementFailEnabled)
                {
                    KineticTelemetry.TraceStanceFail(
                        "Path1.sphere0",
                        sphere0.Center,
                        sphere0.Radius,
                        0,
                        trail.CheckCellId,
                        realmOrigin,
                        objectDetails.Ethereal);
                }

                return ShiftVerdict.Collided;
            }

            if (KineticTelemetry.ProbePlacementFailEnabled)
            {
                KineticTelemetry.PreviousStanceFailPolyIdent = 0;
                KineticTelemetry.PreviousStanceFailSolidLeaf = false;
            }

            if (hasSphere1 &&
                TraverseSolid(
                    tree,
                    tree.TrunkOrdinal,
                    sphere1,
                    wipeChamber))
            {
                if (KineticTelemetry.ProbePlacementFailEnabled)
                {
                    KineticTelemetry.TraceStanceFail(
                        "Path1.sphere1",
                        sphere1.Center,
                        sphere1.Radius,
                        1,
                        trail.CheckCellId,
                        realmOrigin,
                        objectDetails.Ethereal);
                }

                return ShiftVerdict.Collided;
            }

            return ShiftVerdict.OK;
        }

        if (trail.CheckWalkable)
        {
            return PassableVerdict(
                tree,
                trail,
                sphere0,
                ownSpaceZ);
        }

        if (trail.StepDown)
        {
            return SweepLoop(
                tree,
                changeover,
                sphere0,
                ownSpaceZ,
                scaling,
                ownToRealm,
                realmOrigin);
        }

        if (trail.Collide)
        {
            Ball validLocus = sphere0;
            int strikePolygOrdinal = -1;
            ushort strikePolygIdent = 0;
            bool altered = false;

            TraversePassable(
                tree,
                tree.TrunkOrdinal,
                trail,
                ref validLocus,
                travel,
                ownSpaceZ,
                ref strikePolygOrdinal,
                ref strikePolygIdent,
                ref altered);

            if (altered && strikePolygOrdinal >= 0)
            {
                var strikePolyg =
                    tree.PolygChart.Polygons[strikePolygOrdinal];
                var verts =
                    CornersOf(tree, strikePolyg);
                Vector3 ownShift =
                    validLocus.Center - sphere0.Center;
                Vector3 realmShift =
                    OwnToRealm(ownShift) * scaling;
                trail.AppendShiftToVerifySpot(realmShift);

                Vector3 realmNorm =
                    RealmNorm(strikePolyg.Plane.Normal, ownToRealm);
                Plane realmPlane = RealmPlane(
                    realmNorm,
                    verts,
                    ownToRealm,
                    scaling,
                    realmOrigin);
                impacts.AssignLinkPlane(
                    realmPlane,
                    trail.CheckCellId,
                    false);
                trail.AssignPassableTransformed(
                    realmPlane,
                    verts,
                    ownToRealm,
                    scaling,
                    realmOrigin,
                    Vector3.UnitZ);
                NoteStrikeForSensors(tree, strikePolygOrdinal);
                return ShiftVerdict.Adjusted;
            }

            return ShiftVerdict.OK;
        }

        if ((objectDetails.State & MoverState.Contact) != 0)
        {
            int strikePolygIndex0 = -1;
            Vector3 contact0 = Vector3.Zero;
            bool hit0 = TraverseFacingStrike(
                tree,
                tree.TrunkOrdinal,
                sphere0,
                travel,
                ref strikePolygIndex0,
                ref contact0);

            if (hit0)
            {
                NoteStrikeForSensors(tree, strikePolygIndex0);
                Vector3 realmNorm = OwnToRealm(
                    tree.PolygChart.Polygons[strikePolygIndex0].Plane.Normal);
                if (engine is not null && !trail.StepUp && !trail.StepDown)
                    return SweepLoop2(changeover, realmNorm, engine);

                return Shift(changeover, realmNorm);
            }

            if (hasSphere1)
            {
                int strikePolygIndex1 = -1;
                Vector3 contact1 = Vector3.Zero;
                bool hit1 = TraverseFacingStrike(
                    tree,
                    tree.TrunkOrdinal,
                    sphere1,
                    travel,
                    ref strikePolygIndex1,
                    ref contact1);

                if (hit1)
                {
                    NoteStrikeForSensors(tree, strikePolygIndex1);
                    Vector3 realmNorm = OwnToRealm(
                        tree.PolygChart.Polygons[strikePolygIndex1].Plane.Normal);
                    return Shift(changeover, realmNorm);
                }

                if (strikePolygIndex1 >= 0)
                {
                    NoteStrikeForSensors(tree, strikePolygIndex1);
                    NoteNearbyMiss(
                        tree,
                        trail,
                        strikePolygIndex1,
                        hopUp: false,
                        ownToRealm);
                    return ShiftVerdict.OK;
                }

                if (strikePolygIndex0 >= 0)
                {
                    NoteStrikeForSensors(tree, strikePolygIndex0);
                    NoteNearbyMiss(
                        tree,
                        trail,
                        strikePolygIndex0,
                        hopUp: true,
                        ownToRealm);
                    return ShiftVerdict.OK;
                }
            }

            return ShiftVerdict.OK;
        }

        int defaultStrikePolygIndex0 = -1;
        Vector3 defaultContact0 = Vector3.Zero;
        bool defaultHit0 = TraverseFacingStrike(
            tree,
            tree.TrunkOrdinal,
            sphere0,
            travel,
            ref defaultStrikePolygIndex0,
            ref defaultContact0);

        if (defaultHit0 || defaultStrikePolygIndex0 >= 0)
        {
            if ((objectDetails.State & MoverState.PathClipped) != 0)
            {
                return ClipAgainst(
                    tree,
                    changeover,
                    sphere0,
                    ownLatestMiddle,
                    defaultStrikePolygIndex0,
                    defaultContact0,
                    scaling,
                    ownToRealm);
            }

            Vector3 realmNormal0 = OwnToRealm(
                tree.PolygChart.Polygons[defaultStrikePolygIndex0].Plane.Normal);
            trail.AssignCollide(realmNormal0);
            trail.WalkableAllowance = KineticConstants.LandingZ;
            NoteStrikeForSensors(tree, defaultStrikePolygIndex0);
            return ShiftVerdict.Adjusted;
        }

        if (hasSphere1)
        {
            int defaultStrikePolygIndex1 = -1;
            Vector3 defaultContact1 = Vector3.Zero;
            bool defaultHit1 = TraverseFacingStrike(
                tree,
                tree.TrunkOrdinal,
                sphere1,
                travel,
                ref defaultStrikePolygIndex1,
                ref defaultContact1);

            if (defaultHit1 || defaultStrikePolygIndex1 >= 0)
            {
                Vector3 realmNormal1 = OwnToRealm(
                    tree.PolygChart.Polygons[defaultStrikePolygIndex1].Plane.Normal);
                impacts.AssignImpactNorm(realmNormal1);
                NoteStrikeForSensors(tree, defaultStrikePolygIndex1);
                return ShiftVerdict.Collided;
            }
        }

        return ShiftVerdict.OK;
    }
}
