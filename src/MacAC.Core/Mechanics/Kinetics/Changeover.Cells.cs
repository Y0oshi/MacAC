using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Environment: the primary cell sweep, neighbouring cells, and walkable validation.</summary>
public sealed partial class Changeover
{
    private const float InsidePassableSensorGap = 0.5f;

    internal bool TrySeekInsidePassablePlane(
        CellKinetics chamberKinetics,
        Vector3 ownFootMiddle,
        float orbRadius,
        out System.Numerics.Plane realmPlane,
        out Vector3[] realmVerts,
        out uint strikePolyIdent)
    {
        realmPlane = default;
        realmVerts = System.Array.Empty<Vector3>();
        strikePolyIdent = 0;

        bool usePlanar = chamberKinetics.PackedKineticBsp is { TrunkOrdinal: >= 0 };
        if (!usePlanar && chamberKinetics.BSP?.Root is null)
            return false;

        float storedPassableAllowance = this.SweepPath.WalkableAllowance;
        this.SweepPath.WalkableAllowance = KineticConstants.FloorZ;

        SettledPolygon? strikePoly = null;
        int planarStrikeOrdinal = -1;
        ushort strikeIdent = 0;
        Vector3 adjustedMiddle;
        bool located;

        try
        {
            located = usePlanar ? PackedBspQuery.SeekPassableOrb(
                    chamberKinetics.PackedKineticBsp!,
                    this,
                    ownFootMiddle,
                    orbRadius,
                    InsidePassableSensorGap,
                    Vector3.UnitZ,
                    out planarStrikeOrdinal,
                    out strikeIdent,
                    out adjustedMiddle) : CellBspProbe.SeekPassableOrb(
                    chamberKinetics.BSP!.Root!,
                    chamberKinetics.Resolved,
                    this,
                    ownFootMiddle,
                    orbRadius,
                    InsidePassableSensorGap,
                    Vector3.UnitZ,
                    out strikePoly,
                    out strikeIdent,
                    out adjustedMiddle);
        }
        finally
        {
            this.SweepPath.WalkableAllowance = storedPassableAllowance;
        }

        if (!located)
            return false;

        Plane ownPlane;
        if (usePlanar)
        {
            var chart =
                chamberKinetics.PackedKineticBsp!.PolygChart;
            if ((uint)planarStrikeOrdinal >= (uint)chart.Polygons.Length)
                return false;
            var polyg = chart.Polygons[planarStrikeOrdinal];
            ownPlane = polyg.Plane;

            var realmNorm = Vector3.TransformNormal(
                ownPlane.Normal,
                chamberKinetics.WorldTransform);
            realmNorm = Vector3.Normalize(realmNorm);
            Vector3 realmV0 = Vector3.Transform(
                chart.Vertices[polyg.VertexRange.Start],
                chamberKinetics.WorldTransform);
            float realmD = -Vector3.Dot(realmNorm, realmV0);
            realmPlane = new System.Numerics.Plane(realmNorm, realmD);

            realmVerts = new Vector3[polyg.VertexRange.Count];
            for (int idx = 0; idx < realmVerts.Length; ++idx)
            {
                realmVerts[idx] = Vector3.Transform(
                    chart.Vertices[polyg.VertexRange.Start + idx],
                    chamberKinetics.WorldTransform);
            }
        }
        else
        {
            if (strikePoly is null)
                return false;
            ownPlane = strikePoly.Plane;
            Vector3[] ownVerts = strikePoly.Vertices;

            var realmNorm = Vector3.TransformNormal(
                ownPlane.Normal,
                chamberKinetics.WorldTransform);
            realmNorm = Vector3.Normalize(realmNorm);
            Vector3 realmV0 = Vector3.Transform(
                ownVerts[0],
                chamberKinetics.WorldTransform);
            float realmD = -Vector3.Dot(realmNorm, realmV0);
            realmPlane = new System.Numerics.Plane(realmNorm, realmD);

            realmVerts = new Vector3[ownVerts.Length];
            for (int idx = 0; idx < ownVerts.Length; ++idx)
            {
                realmVerts[idx] = Vector3.Transform(
                    ownVerts[idx],
                    chamberKinetics.WorldTransform);
            }
        }

        strikePolyIdent = strikeIdent;
        return true;
    }

    internal ShiftVerdict VerifyAnotherChambers(
        KineticEngine engine,
        Vector3 footMiddle,
        float orbRadius,
        System.Collections.Generic.IReadOnlyCollection<uint> chamberSet)
    {
        if (engine.DataCache is null) return ShiftVerdict.OK;
        SweepPath path = SweepPath;

        List<uint> sequenced = path.SequencedChamberTemp.Rent();
        try
        {
            if (chamberSet is ChamberArray chamberArr)
            {
                for (int idx = 0; idx < chamberArr.Count; ++idx)
                    sequenced.Add(chamberArr.SequencedIdents[idx]);
            }
            else
            {
                foreach (uint ident in chamberSet)
                    sequenced.Add(ident);
            }
            sequenced.Sort();

            foreach (uint chamberIdent in sequenced)
            {
                if (chamberIdent == path.CheckCellId) continue;

                footMiddle = path.GlobalSphere[0].Center;

                if ((chamberIdent & 0xFFFFu) < 0x0100u)
                {
                    if (engine.DataCache.ChamberGraph.ObtainShown(chamberIdent) is null)
                        continue;

                    TerrainFootingSample? landPassable = engine.ProbeLandPassableInChamber(
                        chamberIdent, footMiddle.X, footMiddle.Y);
                    if (landPassable is { } land)
                    {
                        ShiftVerdict landPhase = VetFooting(
                            footMiddle,
                            orbRadius,
                            land.Plane,
                            land.IsWater,
                            land.WaterDepth,
                            chamberIdent: land.CellId,
                            passableVerts: land.Vertices);

                        if (KineticTelemetry.ProbeIndoorBspEnabled)
                        {
                            Plane plane = land.Plane;
                            Console.WriteLine(FormattableString.Invariant(
                                $"[other-cells] primary=0x{path.CheckCellId:X8} iter=0x{chamberIdent:X8} terrain wpos=({footMiddle.X:F3},{footMiddle.Y:F3},{footMiddle.Z:F3}) r={orbRadius:F3} result={landPhase} n=({plane.Normal.X:F3},{plane.Normal.Y:F3},{plane.Normal.Z:F3}) d={plane.D:F3}"));
                        }

                        if (KineticTelemetry.ProbePushBackEnabled)
                        {
                            KineticTelemetry.TracePushBackChamberPassage(
                                primaryChamberIdent: path.CheckCellId,
                                anotherChamberIdent: chamberIdent,
                                bspOutcome: (int)landPhase,
                                halted: false);
                        }

                        if (ImposeAnotherChamberOutcome(landPhase, out var landHalted))
                            return landHalted;
                    }

                    ShiftVerdict bldgAnotherPhase = SweepStructures(engine, chamberIdent);
                    if (ImposeAnotherChamberOutcome(bldgAnotherPhase, out var bldgHalted))
                        return bldgHalted;

                    ShiftVerdict objRefAnotherPhase = SweepObjectsIn(engine, chamberIdent);
                    if (ImposeAnotherChamberOutcome(objRefAnotherPhase, out var objRefHalted))
                        return objRefHalted;

                    continue;
                }

                CellKinetics? chamber = engine.DataCache.FetchChamberStruct(chamberIdent);
                if (chamber is null ||
                    !ContactSweep.HasPhysics(engine.DataCache, chamber))

                    continue;

                Vector3 ownMiddle = Vector3.Transform(footMiddle, chamber.InverseWorldTransform);
                Vector3 ownCurrMiddle = Vector3.Transform(path.GlobalCurrCenter[0].Center, chamber.InverseWorldTransform);

                bool hasOwnSphere1 = path.NumSphere > 1;
                Vector3 ownSphere1Middle = hasOwnSphere1
                    ? Vector3.Transform(path.GlobalSphere[1].Center, chamber.InverseWorldTransform)
                    : Vector3.Zero;
                float ownSphere1Radius = hasOwnSphere1
                    ? path.GlobalSphere[1].Radius
                    : 0f;

                System.Numerics.Quaternion chamberSpin;
                Vector3 chamberOrigin;
                if (!System.Numerics.Matrix4x4.Decompose(chamber.WorldTransform, out _,
                        out chamberSpin, out chamberOrigin))
                {
                    Console.WriteLine(FormattableString.Invariant(
                        $"[other-cells] WARN cell 0x{chamberIdent:X8} WorldTransform didn't decompose - falling back to identity rotation"));
                    chamberSpin = System.Numerics.Quaternion.Identity;
                    chamberOrigin = chamber.WorldTransform.Translation;
                }

                if (KineticTelemetry.ProbeIndoorBspEnabled)
                    KineticTelemetry.PreviousBspStrikePoly = null;

                ShiftVerdict outcome = ContactSweep.SeekImpacts(
                    engine.DataCache!, chamber, this,
                    ownMiddle,
                    orbRadius,
                    hasOwnSphere1,
                    ownSphere1Middle,
                    ownSphere1Radius,
                    ownCurrMiddle,
                    Vector3.UnitZ, 1.0f, chamberSpin, engine,
                    realmOrigin: chamberOrigin);

                if (KineticTelemetry.ProbeIndoorBspEnabled)
                {
                    SettledPolygon? strike = KineticTelemetry.PreviousBspStrikePoly;
                    string polyDsc = strike is null
                        ? "poly=n/a"
                        : FormattableString.Invariant(
                            $"poly=0x{strike.Id:X4} n=({strike.Plane.Normal.X:F3},{strike.Plane.Normal.Y:F3},{strike.Plane.Normal.Z:F3}) d={strike.Plane.D:F3} sides={strike.SidesType}");
                    var sphere =
                        ContactSweep.RootBoundingSphere(
                            engine.DataCache,
                            chamber);
                    string bsDsc = FormattableString.Invariant(
                        $"bs=({sphere.Origin.X:F3},{sphere.Origin.Y:F3},{sphere.Origin.Z:F3}) br={sphere.Radius:F3}");
                    Console.WriteLine(FormattableString.Invariant(
                        $"[other-cells] primary=0x{path.CheckCellId:X8} iter=0x{chamberIdent:X8} wpos=({footMiddle.X:F3},{footMiddle.Y:F3},{footMiddle.Z:F3}) lpos=({ownMiddle.X:F3},{ownMiddle.Y:F3},{ownMiddle.Z:F3}) lprev=({ownCurrMiddle.X:F3},{ownCurrMiddle.Y:F3},{ownCurrMiddle.Z:F3}) r={orbRadius:F3} {bsDsc} result={outcome} cn=({ContactLedger.CollisionNormal.X:F2},{ContactLedger.CollisionNormal.Y:F2},{ContactLedger.CollisionNormal.Z:F2}) sn=({ContactLedger.SlidingNormal.X:F2},{ContactLedger.SlidingNormal.Y:F2},{ContactLedger.SlidingNormal.Z:F2}) negHit={path.NegPolyHit} negN=({path.NegCollisionNormal.X:F2},{path.NegCollisionNormal.Y:F2},{path.NegCollisionNormal.Z:F2}) {polyDsc}"));
                }

                if (KineticTelemetry.ProbeStepWalkEnabled
                    && path.StepDown
                    && outcome == ShiftVerdict.OK)
                {
                    InspectClosestPassable("other-cell", chamberIdent, ownMiddle, orbRadius, chamber.Resolved);
                }

                if (KineticTelemetry.ProbePushBackEnabled)
                {
                    KineticTelemetry.TracePushBackChamberPassage(
                        primaryChamberIdent: path.CheckCellId,
                        anotherChamberIdent: chamberIdent,
                        bspOutcome: (int)outcome,
                        halted: false);
                }

                if (ImposeAnotherChamberOutcome(outcome, out var halted))
                    return halted;

                ShiftVerdict objRefInsidePhase = SweepObjectsIn(engine, chamberIdent);
                if (ImposeAnotherChamberOutcome(objRefInsidePhase, out var objRefInsideHalted))
                    return objRefInsideHalted;
            }

            return ShiftVerdict.OK;
        }
        finally
        {
            path.SequencedChamberTemp.Yield(sequenced);
        }
    }

    internal ShiftVerdict SeekEnvironImpacts(
        KineticEngine engine,
        uint primaryChamberIdent)
    {
        SweepPath path = SweepPath;
        ContactLedger ledger = ContactLedger;

        path.ObstructionEthereal = false;

        Vector3 footMiddle = path.GlobalSphere[0].Center;
        float orbRadius = path.GlobalSphere[0].Radius;

        uint chamberLo = primaryChamberIdent & 0xFFFFu;
        if (chamberLo >= 0x0100 && engine.DataCache is not null)
        {
            CellKinetics? chamberKinetics = engine.DataCache.FetchChamberStruct(primaryChamberIdent);

            ShiftVerdict restrictionPhase = MoverFacts.VerifyListingRestrictions(
                chamberKinetics?.RestrictionObj ?? 0,
                engine.Objects);
            if (restrictionPhase != ShiftVerdict.OK)
            {
                if ((MoverFacts.State & MoverState.Contact) == 0)
                    ledger.CollidedWithEnvironment = true;
                return restrictionPhase;
            }

            if (chamberKinetics is not null &&
                ContactSweep.HasPhysics(engine.DataCache!, chamberKinetics))
            {
                Vector3 ownMiddle = Vector3.Transform(footMiddle, chamberKinetics.InverseWorldTransform);
                Vector3 ownCurrMiddle = Vector3.Transform(path.GlobalCurrCenter[0].Center, chamberKinetics.InverseWorldTransform);

                bool hasOwnSphere1 = path.NumSphere > 1;
                Vector3 ownSphere1Middle = hasOwnSphere1
                    ? Vector3.Transform(
                        path.GlobalSphere[1].Center,
                        chamberKinetics.InverseWorldTransform)
                    : Vector3.Zero;
                float ownSphere1Radius = hasOwnSphere1
                    ? path.GlobalSphere[1].Radius
                    : 0f;

                if (KineticTelemetry.ProbeIndoorBspEnabled)
                    KineticTelemetry.PreviousBspStrikePoly = null;

                Quaternion chamberSpin;
                Vector3 chamberOrigin;
                if (!Matrix4x4.Decompose(chamberKinetics.WorldTransform, out _, out chamberSpin, out chamberOrigin))
                {
                    Console.WriteLine(FormattableString.Invariant(
                        $"[indoor-bsp] WARN cellPhysics.WorldTransform didn't decompose cleanly for cell 0x{primaryChamberIdent:X8} - falling back to identity rotation"));
                    chamberSpin = Quaternion.Identity;
                    chamberOrigin = chamberKinetics.WorldTransform.Translation;
                }

                ShiftVerdict chamberPhase = ContactSweep.SeekImpacts(
                    engine.DataCache!,
                    chamberKinetics,
                    this,
                    ownMiddle,
                    orbRadius,
                    hasOwnSphere1,
                    ownSphere1Middle,
                    ownSphere1Radius,
                    ownCurrMiddle,
                    Vector3.UnitZ,  // local space Z is up
                    1.0f,
                    chamberSpin,
                    engine,         // engine needed for Path 5 step-up
                    realmOrigin: chamberOrigin);

                if (KineticTelemetry.ProbeIndoorBspEnabled)
                {
                    SeekEnvironImpactsTrace(primaryChamberIdent, footMiddle, ownMiddle, ownCurrMiddle, orbRadius, chamberPhase);
                }

                if (chamberPhase != ShiftVerdict.OK)
                {
                    if ((MoverFacts.State & MoverState.Contact) == 0)
                        ledger.CollidedWithEnvironment = true;
                    return chamberPhase;
                }

                return ShiftVerdict.OK;
            }
        }

        TerrainFootingSample? landPassable = engine.ProbeLandPassableInChamber(
            primaryChamberIdent,
            footMiddle.X,
            footMiddle.Y);
        if (landPassable is not null)
        {
            ShiftVerdict landPhase = VetFooting(footMiddle, orbRadius, landPassable.Value.Plane,
                                    landPassable.Value.IsWater,
                                    landPassable.Value.WaterDepth,
                                    chamberIdent: landPassable.Value.CellId,
                                    passableVerts: landPassable.Value.Vertices);
            if (landPhase != ShiftVerdict.OK)
                return landPhase;
        }
        // else: no terrain loaded here - allow pass-through

        return ShiftVerdict.OK;
    }

    private void SeekEnvironImpactsTrace(uint primaryChamberIdent, Vector3 footMiddle, Vector3 ownMiddle, Vector3 ownCurrMiddle, float orbRadius, ShiftVerdict chamberPhase)
    {
        SettledPolygon? strike = KineticTelemetry.PreviousBspStrikePoly;
        string polyDsc = strike is null
                                ? "poly=n/a"
                                : FormattableString.Invariant(
                                    $"n=({strike.Plane.Normal.X:F3},{strike.Plane.Normal.Y:F3},{strike.Plane.Normal.Z:F3}) sides={strike.SidesType}");
        Console.WriteLine(FormattableString.Invariant(
                                $"[indoor-bsp] cell=0x{primaryChamberIdent:X8} wpos=({footMiddle.X:F3},{footMiddle.Y:F3},{footMiddle.Z:F3}) lpos=({ownMiddle.X:F3},{ownMiddle.Y:F3},{ownMiddle.Z:F3}) lprev=({ownCurrMiddle.X:F3},{ownCurrMiddle.Y:F3},{ownCurrMiddle.Z:F3}) r={orbRadius:F3} result={chamberPhase} ")
                                + polyDsc);
    }

    private ShiftVerdict SweepPrimaryChamber(
        KineticEngine engine,
        uint chamberIdent,
        int interiorAttempt)
    {
        var surroundings = WatchStage(
            engine,
            ShiftCellContactPhase.Environment,
            chamberIdent,
            SeekEnvironImpacts(engine, chamberIdent));
        if (surroundings != ShiftVerdict.OK)
        {
            KineticTelemetry.RecordPassageSlotAttempt(
                MoverFacts.SelfEntityId, interiorAttempt, "environment",
                surroundings, null, null, surroundings,
                ContactLedger.CollisionNormal, ContactLedger.LastCollidedObjectGuid);
            return surroundings;
        }

        var structure = WatchStage(
            engine,
            ShiftCellContactPhase.Building,
            chamberIdent,
            SweepStructures(engine, chamberIdent));
        if (structure != ShiftVerdict.OK)
        {
            KineticTelemetry.RecordPassageSlotAttempt(
                MoverFacts.SelfEntityId, interiorAttempt, "building",
                surroundings, structure, null, structure,
                ContactLedger.CollisionNormal, ContactLedger.LastCollidedObjectGuid);
            return structure;
        }

        var objects = WatchStage(
            engine,
            ShiftCellContactPhase.Objects,
            chamberIdent,
            SweepObjectsIn(engine, chamberIdent));
        KineticTelemetry.RecordPassageSlotAttempt(
            MoverFacts.SelfEntityId, interiorAttempt, "objects",
            surroundings, structure, objects, objects,
            ContactLedger.CollisionNormal, ContactLedger.LastCollidedObjectGuid);
        return objects;
    }

    private ShiftVerdict WatchStage(
        KineticEngine engine,
        ShiftCellContactPhase stage,
        uint chamberIdent,
        ShiftVerdict actual)
    {
        return engine.ChangeoverChamberImpactTestTap is { } tap
                ? tap(this, stage, chamberIdent, actual)
                : actual;
    }

    private bool RimSlideBackup(
        KineticEngine engine,
        float hopDownHeight,
        float zValue,
        out ShiftVerdict outcome)
    {
        SweepPath path = SweepPath;
        ContactLedger ledger = ContactLedger;
        MoverFacts facts = MoverFacts;

        if (!facts.OnWalkable || !facts.EdgeSlide)
        {
            path.WipePassable();
            path.ReinstateVerifySpot();
            ledger.ContactPlaneValid = false;
            ledger.ContactPlaneIsWater = false;
            outcome = ShiftVerdict.OK;
            return true;
        }

        if (ledger.ContactPlaneValid && ledger.ContactPlane.Normal.Z < zValue)
        {
            Plane cliffPlane = ledger.ContactPlane;
            path.WipePassable();
            path.ReinstateVerifySpot();
            ledger.ContactPlaneValid = false;
            ledger.ContactPlaneIsWater = false;
            outcome = ShiftOffCliff(cliffPlane);
            return false;
        }

        if (path.HasPassablePolyg)
        {
            path.ReinstateVerifySpot();
            ledger.ContactPlaneValid = false;
            ledger.ContactPlaneIsWater = false;
            outcome = path.PrecipiceSlide(this);
            return outcome == ShiftVerdict.Collided;
        }

        if (ledger.ContactPlaneValid)
        {
            path.WipePassable();
            path.ReinstateVerifySpot();
            ledger.ContactPlaneValid = false;
            ledger.ContactPlaneIsWater = false;
            outcome = ShiftVerdict.OK;
            return true;
        }

        Vector3 backToLatest = path.GlobalCurrCenter[0].Center - path.GlobalSphere[0].Center;
        path.AppendShiftToVerifySpot(backToLatest);

        _ = StepDownBy(hopDownHeight, zValue, engine);

        ledger.ContactPlaneValid = false;
        ledger.ContactPlaneIsWater = false;
        path.ReinstateVerifySpot();

        if (path.HasPassablePolyg)
        {
            outcome = path.PrecipiceSlide(this);
            return outcome == ShiftVerdict.Collided;
        }

        path.WipePassable();
        outcome = ShiftVerdict.Collided;
        return true;
    }

    private ShiftVerdict ShiftOffCliff(Plane linkPlane)
    {
        SweepPath path = SweepPath;
        ContactLedger ledger = ContactLedger;

        Vector3 referenceNorm = ledger.LastKnownContactPlane.Normal;

        Vector3 linkNorm = Vector3.Cross(linkPlane.Normal, referenceNorm);
        linkNorm.Z = 0f;

        Vector3 collideNorm = new(-linkNorm.Y, linkNorm.X, 0f);
        if (collideNorm.LengthSquared() < KineticConstants.EpsilonSq)

            return ShiftVerdict.OK;

        collideNorm = Vector3.Normalize(collideNorm);

        Vector3 shift = path.GlobalSphere[0].Center - path.GlobalCurrCenter[0].Center;
        float angle = Vector3.Dot(collideNorm, shift);

        if (angle <= 0f)
        {
            path.AppendShiftToVerifySpot(collideNorm * angle);
            ledger.AssignImpactNorm(collideNorm);
        }
        else
        {
            path.AppendShiftToVerifySpot(collideNorm * -angle);
            ledger.AssignImpactNorm(-collideNorm);
        }

        return ShiftVerdict.Adjusted;
    }

    private void InspectChamberSetSummary(
        KineticEngine engine,
        uint containingChamberIdent,
        System.Collections.Generic.IReadOnlyCollection<uint> chamberSet,
        Vector3 footMiddle,
        float orbRadius)
    {
        if (!KineticTelemetry.ProbeStepWalkEnabled || engine.DataCache is null)
            return;

        uint primary = SweepPath.CheckCellId;
        if ((primary & 0xFFFF0000u) != 0xA9B40000u)
            return;

        uint lo = primary & 0xFFFFu;
        if (lo < 0x0140u || lo > 0x0148u)
            return;

        List<uint> sequenced = new System.Collections.Generic.List<uint>(chamberSet);
        sequenced.Sort();
        string chambers = string.Join(",", sequenced.ConvertAll(ident => $"0x{ident:X8}"));

        Console.WriteLine(FormattableString.Invariant(
            $"[cell-set-summary] primary=0x{primary:X8} containing=0x{containingChamberIdent:X8} has146={chamberSet.Contains(0xA9B40146u)} has147={chamberSet.Contains(0xA9B40147u)} foot=({footMiddle.X:F4},{footMiddle.Y:F4},{footMiddle.Z:F4}) r={orbRadius:F4} cells={chambers}"));

        InspectChamberBsp(engine, 0xA9B40146u, footMiddle, orbRadius);
    }

    private void InspectChamberBsp(
        KineticEngine engine,
        uint chamberIdent,
        Vector3 footMiddle,
        float orbRadius)
    {
        CellKinetics? chamber = engine.DataCache?.FetchChamberStruct(chamberIdent);
        if (chamber?.CellBSP?.Root is null)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"[cell-set-summary] target=0x{chamberIdent:X8} not available"));
            return;
        }

        Vector3 own = Vector3.Transform(footMiddle, chamber.InverseWorldTransform);
        bool ptInside = CellBspProbe.PtInsideChamberBsp(chamber.CellBSP.Root, own);
        bool orbStrike = CellBspProbe.SphereIntersectsCellBsp(chamber.CellBSP.Root, own, orbRadius);
        Console.WriteLine(FormattableString.Invariant(
            $"[cell-set-summary] target=0x{chamberIdent:X8} local=({own.X:F4},{own.Y:F4},{own.Z:F4}) pointInside={ptInside} sphereHit={orbStrike}"));

        if (chamber.Resolved.Count > 0)
            InspectClosestPassable("cellar-ascent-target", chamberIdent, own, orbRadius, chamber.Resolved);
    }

    private void InspectClosestPassable(
        string site,
        uint chamberIdent,
        Vector3 ownMiddle,
        float orbRadius,
        Dictionary<ushort, SettledPolygon> settled)
    {
        SweepPath path = SweepPath;

        SettledPolygon? closest = null;
        ushort closestIdent = 0;
        float closestAbsGap = float.MaxValue;
        float closestSignedGap = 0f;
        bool closestInsideRims = false;
        bool closestOverlapsOrb = false;

        foreach (var kv in settled)
        {
            SettledPolygon poly = kv.Value;
            float normDotUp = Vector3.Dot(Vector3.UnitZ, poly.Plane.Normal);
            if (normDotUp <= path.WalkableAllowance)
                continue;

            float signedGap = Vector3.Dot(poly.Plane.Normal, ownMiddle) + poly.Plane.D;
            float absGap = MathF.Abs(signedGap);
            if (absGap >= closestAbsGap)
                continue;

            closest = poly;
            closestIdent = kv.Key;
            closestAbsGap = absGap;
            closestSignedGap = signedGap;
            closestOverlapsOrb = absGap <= orbRadius - KineticConstants.EPSILON;
            closestInsideRims = !CellBspProbe.SeekCrossedRim(
                poly.Plane, poly.Vertices, ownMiddle, Vector3.UnitZ, out _);
        }

        if (closest is null)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"[walkable-nearest] site={site} cell=0x{chamberIdent:X8} center=({ownMiddle.X:F4},{ownMiddle.Y:F4},{ownMiddle.Z:F4}) r={orbRadius:F4} allowance={path.WalkableAllowance:F4} none"));
            return;
        }

        float hopHunt = path.StepDown ? path.StepDownAmt * path.WalkInterp : 0f;
        Console.WriteLine(FormattableString.Invariant(
            $"[walkable-nearest] site={site} cell=0x{chamberIdent:X8} poly=0x{closestIdent:X4} center=({ownMiddle.X:F4},{ownMiddle.Y:F4},{ownMiddle.Z:F4}) r={orbRadius:F4} dist={closestSignedGap:F4} abs={closestAbsGap:F4} gap={closestAbsGap - orbRadius:F4} insideEdges={closestInsideRims} overlapsSphere={closestOverlapsOrb} n=({closest.Plane.Normal.X:F4},{closest.Plane.Normal.Y:F4},{closest.Plane.Normal.Z:F4}) d={closest.Plane.D:F4} allowance={path.WalkableAllowance:F4} stepSearch={hopHunt:F4}"));

        InspectPassableContender(
            site, chamberIdent, closestIdent, closest, ownMiddle, orbRadius, hopHunt);
    }

    private void InspectPassableContender(
        string site,
        uint chamberIdent,
        ushort polyIdent,
        SettledPolygon poly,
        Vector3 ownMiddle,
        float orbRadius,
        float hopHunt)
    {
        if (!KineticTelemetry.ProbeStepWalkEnabled)
            return;
        if (site != "other-cell" || chamberIdent != 0xA9B40143u || polyIdent != 0x0004)
            return;
        if (ownMiddle.Z < -1.1f || ownMiddle.Z > 0.2f)
            return;

        Vector3 travel = -Vector3.UnitZ * hopHunt;
        float dpSpot = Vector3.Dot(ownMiddle, poly.Plane.Normal) + poly.Plane.D;
        float dpRelocate = Vector3.Dot(travel, poly.Plane.Normal);
        bool parallel = dpRelocate <= KineticConstants.EPSILON
            && dpRelocate >= -KineticConstants.EPSILON;

        float distance = 0f;
        float idxDistance = 0f;
        float lerp = SweepPath.WalkInterp;
        bool adjustWouldEnact = false;

        if (!parallel)
        {
            distance = dpRelocate <= KineticConstants.EPSILON
                ? dpSpot - orbRadius
                : -orbRadius - dpSpot;
            idxDistance = distance / dpRelocate;
            lerp = (1f - idxDistance) * SweepPath.WalkInterp;
            adjustWouldEnact = lerp < SweepPath.WalkInterp && lerp >= -0.5f;
        }

        Vector3 projected = ownMiddle - poly.Plane.Normal * dpSpot;
        Vector3 adjusted = ownMiddle - travel * idxDistance;

        var verts = new System.Text.StringBuilder();
        for (int idx = 0; idx < poly.Vertices.Length; ++idx)
        {
            InspectPassableContenderLoop(idx, verts, poly);
        }

        Console.WriteLine(FormattableString.Invariant(
            $"[cellar-ascent-walkable-detail] cell=0x{chamberIdent:X8} poly=0x{polyIdent:X4} center=({ownMiddle.X:F4},{ownMiddle.Y:F4},{ownMiddle.Z:F4}) projected=({projected.X:F4},{projected.Y:F4},{projected.Z:F4}) adjusted=({adjusted.X:F4},{adjusted.Y:F4},{adjusted.Z:F4}) r={orbRadius:F4} stepSearch={hopHunt:F4} walkInterp={SweepPath.WalkInterp:F4} dpPos={dpSpot:F4} dpMove={dpRelocate:F4} dist={distance:F4} iDist={idxDistance:F4} interp={lerp:F4} parallel={parallel} adjustWouldApply={adjustWouldEnact} verts=[{verts}]"));
    }

    private void InspectPassableContenderLoop(int idx, System.Text.StringBuilder verts, SettledPolygon poly)
    {
        if (idx > 0) verts.Append(',');
        Vector3 v = poly.Vertices[idx];
        verts.Append(FormattableString.Invariant(
                        $"({v.X:F4},{v.Y:F4},{v.Z:F4})"));
    }

    private ShiftVerdict VerifyAnotherChambersThenProceed(
        KineticEngine engine, Vector3 footMiddle, float orbRadius)
    {
        SweepPath path = SweepPath;

        if (engine.DataCache is null)
        {
            uint settledExteriorChamberIdent = engine.LocateChamberIdent(
                path.GlobalSphere[0].Center,
                orbRadius,
                path.CheckCellId);
            if (settledExteriorChamberIdent != path.CheckCellId)
                path.AssignVerifySpot(path.CheckPos, settledExteriorChamberIdent);
            return ShiftVerdict.OK;
        }

        footMiddle = path.GlobalSphere[0].Center;

        path.HitsInteriorCell = false;

        uint containingChamberIdent = CellHop.FindCellSet(
            engine.DataCache,
            path.GlobalSphere,
            path.NumSphere,
            path.CheckCellId,
            path.CellCandidates,
            path.CarriedBlockOrigin,
            out bool containingChamberLocated);
        ChamberArray chamberSet = path.CellCandidates;
        InspectChamberSetSummary(engine, containingChamberIdent, chamberSet, footMiddle, orbRadius);

        // A placement candidate that no resident cell contains is rejected outright, the way a null
        // containing cell fails placement validation; the ordinary movement path keeps its seed-cell
        // fallback.
        if (!containingChamberLocated
            && path.InsertType is SlotKind.InitialPlacement or SlotKind.Placement)

            return ShiftVerdict.Collided;

        if ((path.CheckCellId & 0xFFFFu) >= 0x0100u
            || (containingChamberIdent & 0xFFFFu) >= 0x0100u)
        {
            path.HitsInteriorCell = true;
        }
        else
        {
            for (int idx = 0; idx < chamberSet.Count; ++idx)
            {
                uint ident = chamberSet.SequencedIdents[idx];
                if ((ident & 0xFFFFu) >= 0x0100u) { path.HitsInteriorCell = true; break; }
            }
        }

        ShiftVerdict anotherChambersPhase = VerifyAnotherChambers(engine, footMiddle, orbRadius, chamberSet);
        if (anotherChambersPhase != ShiftVerdict.OK)
            return anotherChambersPhase;

        if (containingChamberIdent != path.CheckCellId)
        {
            if (path.CarriedBlockOrigin is { } carriedOrigin
                && (path.CheckCellId & 0xFFFFu) is >= 1u and <= 0x40u
                && (containingChamberIdent & 0xFFFFu) is >= 1u and <= 0x40u)
            {
                VerifyAnotherChambersThenProceedBranch(path, containingChamberIdent, carriedOrigin);
            }

            path.AssignVerifySpot(path.CheckPos, containingChamberIdent);
        }
        return ShiftVerdict.OK;
    }

    private void VerifyAnotherChambersThenProceedBranch(SweepPath path, uint containingChamberIdent, Vector3 carriedOrigin)
    {
        int formerChunkX = (int)((path.CheckCellId >> 24) & 0xFFu);
        int formerChunkY = (int)((path.CheckCellId >> 16) & 0xFFu);
        int newChunkX = (int)((containingChamberIdent >> 24) & 0xFFu);
        int newChunkY = (int)((containingChamberIdent >> 16) & 0xFFu);
        path.CarriedBlockOrigin = carriedOrigin + new Vector3(
                            (newChunkX - formerChunkX) * MechLandDefs.ChunkLength,
                            (newChunkY - formerChunkY) * MechLandDefs.ChunkLength,
                            0f);
    }

    private ShiftVerdict VetFooting(Vector3 orbMiddle, float orbRadius,
                                              System.Numerics.Plane linkPlane,
                                              bool isWater, float waterZDepth, uint chamberIdent,
                                              TerrainTriVerts? passableVerts = null)
    {
        SweepPath path = SweepPath;
        ContactLedger ledger = ContactLedger;
        MoverFacts facts = MoverFacts;

        // Low point of the sphere
        Vector3 loPt = orbMiddle - new Vector3(0f, 0f, orbRadius);

        float distance = Vector3.Dot(loPt, linkPlane.Normal) + linkPlane.D + waterZDepth;

        if (distance >= -KineticConstants.EPSILON)
        {
            if (distance <= KineticConstants.EPSILON)
            {
                bool passableNorm = linkPlane.Normal.Z >= path.WalkableAllowance;
                if (path.StepDown || !facts.OnWalkable || passableNorm)
                {
                    ledger.AssignLinkPlane(linkPlane, chamberIdent, isWater);
                    RetainPassable(path, linkPlane, passableVerts);
                }

                bool restingGuardPassed = !facts.Contact && !path.StepDown;
                if (restingGuardPassed)
                {
                    ledger.AssignImpactNorm(linkPlane.Normal);
                    ledger.CollidedWithEnvironment = true;
                }

                KineticTelemetry.RecordPassageVetPassable(
                    facts.SelfEntityId, "resting", distance, waterZDepth,
                    facts.Contact, path.StepDown, restingGuardPassed,
                    linkPlane.Normal, ShiftVerdict.OK);
            }
            else
            {
                KineticTelemetry.RecordPassageVetPassable(
                    facts.SelfEntityId, "above", distance, waterZDepth,
                    facts.Contact, path.StepDown, guardPassed: null,
                    linkPlane.Normal, ShiftVerdict.OK);
            }
            return ShiftVerdict.OK;
        }

        if (path.CheckWalkable)
        {
            KineticTelemetry.RecordPassageVetPassable(
                facts.SelfEntityId, "checkwalkable-fail", distance, waterZDepth,
                facts.Contact, path.StepDown, guardPassed: null,
                linkPlane.Normal, ShiftVerdict.Collided);
            return ShiftVerdict.Collided;  // walkable probe fails
        }

        float zDistance = distance / linkPlane.Normal.Z;

        var outcome = ShiftVerdict.OK;
        bool passable = linkPlane.Normal.Z >= path.WalkableAllowance;
        if (path.StepDown || !facts.OnWalkable || passable)
        {
            ledger.AssignLinkPlane(linkPlane, chamberIdent, isWater);
            RetainPassable(path, linkPlane, passableVerts);

            if (path.StepDown)
            {
                float lerp = (1f - (-1f / (path.StepDownAmt * path.WalkInterp)) * zDistance) * path.WalkInterp;
                if (lerp >= path.WalkInterp || lerp < -0.1f)
                {
                    KineticTelemetry.RecordPassageVetPassable(
                        facts.SelfEntityId, "below-push", distance, waterZDepth,
                        facts.Contact, path.StepDown, guardPassed: null,
                        linkPlane.Normal, ShiftVerdict.Collided);
                    return ShiftVerdict.Collided;
                }
                path.WalkInterp = lerp;
            }

            path.AppendShiftToVerifySpot(new Vector3(0f, 0f, -zDistance));
            outcome = ShiftVerdict.Adjusted;
        }

        bool belowPushGuardPassed = !facts.Contact && !path.StepDown;
        if (belowPushGuardPassed)
        {
            ledger.AssignImpactNorm(linkPlane.Normal);
            ledger.CollidedWithEnvironment = true;
        }

        KineticTelemetry.RecordPassageVetPassable(
            facts.SelfEntityId, "below-push", distance, waterZDepth,
            facts.Contact, path.StepDown, belowPushGuardPassed,
            linkPlane.Normal, outcome);

        return outcome;
    }

    private static void RetainPassable(
        SweepPath orbTrail,
        Plane linkPlane,
        TerrainTriVerts? passableVerts)
    {
        if (passableVerts is { } verts
            && linkPlane.Normal.Z >= KineticConstants.FloorZ)

            orbTrail.AssignPassable(linkPlane, in verts, Vector3.UnitZ);
    }
}
