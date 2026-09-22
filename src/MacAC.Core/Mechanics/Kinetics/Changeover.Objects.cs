using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Objects and buildings: sweeping the mover against proxy spheres and cylinders.</summary>
public sealed partial class Changeover
{
    /// <summary>
    /// Sweeps the mover against every obstruction sharing a room with it, stopping at the first one
    /// it cannot pass. Obstructions are described three ways — a BSP tree, a sphere or a cylinder —
    /// and each is swept differently, but what happens to the result afterwards is the same.
    /// </summary>
    private ShiftVerdict SweepObjectsIn(KineticEngine engine, uint chamberIdent)
    {
        if (engine.DataCache is null) return ShiftVerdict.OK;

        var objsInChamber = engine.ShadeObjects.FetchObjectsInChamber(chamberIdent);
        if (objsInChamber.Count is 0)
            return ShiftVerdict.OK;

        SweepPath path = SweepPath;
        MoverFacts facts = MoverFacts;
        ContactLedger ledger = ContactLedger;

        // Landblock offsets feed the [resolve-bldg] probe only.
        Vector3 verifySpot = path.GlobalSphere[0].Center;
        engine.TryFetchLbCtx(verifySpot.X, verifySpot.Y,
            out _, out float realmShiftX, out float realmShiftY);

        using ProxyEntryFrame nearbyObjs = ProxyEntryFrame.Capture(objsInChamber);

        foreach (ProxyEntry objRef in nearbyObjs.Entries)
        {
            if (IgnoresObstruction(objRef, facts))
                continue;

            // Something ethereal is passed through, unless the mover is stepping down onto it.
            bool etherealForTest = (objRef.State & 0x4u) is not 0
                                || (MoverFacts.Ethereal && (objRef.State & 0x1u) is 0);
            if (etherealForTest && path.StepDown)
                continue;
            path.ObstructionEthereal = etherealForTest;

            bool impactWasValidPre = ledger.CollisionNormalValid;
            if (KineticTelemetry.ProbeBuildingEnabled || KineticTelemetry.ProbeIndoorBspEnabled)
                KineticTelemetry.PreviousBspStrikePoly = null;

            if (!TrySweepObstruction(engine, objRef, path, out ShiftVerdict outcome))
                continue;

            // An ethereal obstruction cannot stop the mover; the hit is taken back, and with it the
            // surface normal, so nothing downstream slides along a wall that was not there.
            if (outcome != ShiftVerdict.OK
                && path.ObstructionEthereal
                && !path.StepDown
                && (objRef.State & 0x1u) is 0)
            {
                outcome = ShiftVerdict.OK;
                ledger.CollisionNormalValid = false;
            }

            bool attributed = outcome != ShiftVerdict.OK
                || (!impactWasValidPre && ledger.CollisionNormalValid);
            if (attributed)
            {
                ledger.CollideObjectGuids.Add(objRef.EntityId);
                ledger.LastCollidedObjectGuid = objRef.EntityId;
            }

            if (path.InsertType == SlotKind.Placement && outcome == ShiftVerdict.Collided)
                TracePlacementRefusal(objRef);
            if (attributed)
                TraceResolvedBuilding(engine, objRef, realmShiftX, realmShiftY);

            path.ObstructionEthereal = false;

            if (outcome != ShiftVerdict.OK)
                return outcome;
        }

        return ShiftVerdict.OK;
    }

    // ---- probes -------------------------------------------------------------------------
    // All of this is diagnostic and off unless a probe flag is set.

    private static void TraceSkippedAsBspOnly(string tag, ProxyEntry objRef)
    {
        if (!KineticTelemetry.ProbeBuildingEnabled)
            return;
        Console.WriteLine(FormattableString.Invariant(
            $"[{tag}] obj=0x{objRef.EntityId:X8} state=0x{objRef.State:X8} — HAS_PHYSICS_BSP_PS dispatches BSP-only"));
    }

    private static void TraceBspTest(
        KineticEngine engine, ProxyEntry objRef, SweepPath path, GfxObjKinetics? kinetics)
    {
        if (!KineticTelemetry.ProbeBuildingEnabled)
            return;
        Vector3 dxy = objRef.Position - path.GlobalCurrCenter[0].Center;
        float distanceXY = MathF.Sqrt(dxy.X * dxy.X + dxy.Y * dxy.Y);
        bool stashStrike = kinetics is not null
            && ContactSweep.HasPhysics(engine.DataCache!, kinetics);
        Console.WriteLine(FormattableString.Invariant(
            $"[bsp-test] obj=0x{objRef.EntityId:X8} gfx=0x{objRef.GfxObjId:X8} state=0x{objRef.State:X8} radius={objRef.Radius:F3} pos=({objRef.Position.X:F2},{objRef.Position.Y:F2},{objRef.Position.Z:F2}) distXY={distanceXY:F3} cacheHit={stashStrike}"));
    }

    private static void TraceCylinderTest(ProxyEntry objRef, SweepPath path, ShiftVerdict outcome)
    {
        if (!KineticTelemetry.ProbeBuildingEnabled)
            return;
        Vector3 dxy = objRef.Position - path.GlobalCurrCenter[0].Center;
        float distanceXY = MathF.Sqrt(dxy.X * dxy.X + dxy.Y * dxy.Y);
        Console.WriteLine(FormattableString.Invariant(
            $"[cyl-test] obj=0x{objRef.EntityId:X8} state=0x{objRef.State:X8} radius={objRef.Radius:F3} height={objRef.CylHeight:F3} pos=({objRef.Position.X:F2},{objRef.Position.Y:F2},{objRef.Position.Z:F2}) distXY={distanceXY:F3} result={outcome}"));
    }

    private static void TracePlacementRefusal(ProxyEntry objRef)
    {
        if (!KineticTelemetry.ProbePlacementFailEnabled)
            return;
        var ciFmt = System.Globalization.CultureInfo.InvariantCulture;
        Console.WriteLine(string.Format(ciFmt,
            "[place-fail-obj] entityId=0x{0:X8} gfxObjId=0x{1:X8} " +
            "collisionType={2} position=({3:F4},{4:F4},{5:F4}) " +
            "scale={6:F4} radius={7:F4}",
            objRef.EntityId, objRef.GfxObjId,
            objRef.CollisionType,
            objRef.Position.X, objRef.Position.Y, objRef.Position.Z,
            objRef.Scale, objRef.Radius));
    }

    /// <summary>
    /// Reports what a collided obstruction resolved to, including the polygon that was struck when
    /// the BSP path recorded one. Used to work out which part of a building a mover caught on.
    /// </summary>
    private static void TraceResolvedBuilding(
        KineticEngine engine, ProxyEntry objRef, float realmShiftX, float realmShiftY)
    {
        if (!KineticTelemetry.ProbeBuildingEnabled)
            return;

        uint pieceIndex = objRef.EntityId & 0xFFu;
        uint actorIdentSensor = objRef.EntityId >> 8;
        GfxObjKinetics? stashedPhys = engine.DataCache!.FetchGfxObjRef(objRef.GfxObjId);
        GfxObjVisualExtent? visLimits = engine.DataCache!.FetchVisualLimits(objRef.GfxObjId);
        float bspR = stashedPhys?.BoundingSphere?.Radius ?? 0f;
        float vAabbR = visLimits?.Radius ?? 0f;
        Vector3 entOriginLb = objRef.Position - new Vector3(realmShiftX, realmShiftY, 0f);

        var builder = new System.Text.StringBuilder(256);
        builder.Append(FormattableString.Invariant(
            $"[resolve-bldg] obj=0x{objRef.EntityId:X8} entityId=0x{actorIdentSensor:X8} partIdx={pieceIndex}\n"));
        builder.Append(FormattableString.Invariant(
            $"               gfxObj=0x{objRef.GfxObjId:X8} hasPhys={stashedPhys is not null} bspR={bspR:F2} vAabbR={vAabbR:F2}\n"));
        builder.Append(FormattableString.Invariant(
            $"               entOrigin_lb=({entOriginLb.X:F1},{entOriginLb.Y:F1},{entOriginLb.Z:F1})"));
        AppendStruckPolygon(builder, objRef);
        Console.WriteLine(builder.ToString());
    }

    private static void AppendStruckPolygon(System.Text.StringBuilder builder, ProxyEntry objRef)
    {
        if (objRef.CollisionType is ProxyContactType.Cylinder or ProxyContactType.Sphere)
        {
            builder.Append(FormattableString.Invariant(
                $"\n               hitPoly: n/a ({objRef.CollisionType.ToString().ToLowerInvariant()})"));
            return;
        }

        SettledPolygon? poly = KineticTelemetry.PreviousBspStrikePoly;
        if (poly is null)
        {
            builder.Append("\n               hitPoly: n/a (BSP path — side-channel not written, missing CellBspProbe wire site)");
            return;
        }

        builder.Append(FormattableString.Invariant(
            $"\n               hitPoly: numVerts={poly.NumPoints} plane=({poly.Plane.Normal.X:F3},{poly.Plane.Normal.Y:F3},{poly.Plane.Normal.Z:F3},{poly.Plane.D:F3})"));
        int vUpper = Math.Min(poly.Vertices.Length, 4);
        for (int vi = 0; vi < vUpper; ++vi)
        {
            Vector3 vOwn = poly.Vertices[vi];
            Vector3 vRealm = objRef.Position + Vector3.Transform(vOwn * objRef.Scale, objRef.Rotation);
            builder.Append(FormattableString.Invariant(
                $"\n                        v{vi}_local=({vOwn.X,5:F2},{vOwn.Y,5:F2},{vOwn.Z,5:F2})  v{vi}_world=({vRealm.X,6:F2},{vRealm.Y,6:F2},{vRealm.Z,6:F2})"));
        }
        if (poly.Vertices.Length > 4)
        {
            builder.Append(FormattableString.Invariant(
                $"\n                        ... ({poly.Vertices.Length - 4} more verts elided)"));
        }
    }

    /// <summary>The mover never collides with itself, its own missile, or an exempt pairing.</summary>
    private bool IgnoresObstruction(ProxyEntry objRef, MoverFacts facts) =>
        (facts.SelfEntityId is not 0 && objRef.EntityId == facts.SelfEntityId)
        || facts.MissileIgnore(objRef.EntityId, objRef.State, objRef.Flags)
        || ContactExemption.ShouldSkip(objRef.State, objRef.Flags, MoverFacts.State);

    /// <summary>
    /// Sweeps against one obstruction. False means this obstruction is not swept at all — it has no
    /// collision geometry, or it is flagged as BSP-only and its sphere or cylinder is a stand-in
    /// that must not be tested.
    /// </summary>
    private bool TrySweepObstruction(
        KineticEngine engine,
        ProxyEntry objRef,
        SweepPath path,
        out ShiftVerdict outcome)
    {
        outcome = ShiftVerdict.OK;
        switch (objRef.CollisionType)
        {
            case ProxyContactType.BSP:
            {
                GfxObjKinetics? kinetics = engine.DataCache!.FetchGfxObjRef(objRef.GfxObjId);
                TraceBspTest(engine, objRef, path, kinetics);
                if (kinetics is null || !ContactSweep.HasPhysics(engine.DataCache!, kinetics))
                {
                    path.ObstructionEthereal = false;
                    return false;
                }

                outcome = SweepAgainstBsp(engine, objRef, path, kinetics);
                return true;
            }

            case ProxyContactType.Sphere:
            {
                if (BspSoleRelay(objRef.State))
                {
                    TraceSkippedAsBspOnly("sph-skip-bsp", objRef);
                    return false;
                }

                bool isBeast = (objRef.State & 0x40u) is not 0
                    || (objRef.Flags & ActorImpactFlagSet.IsCreature) != 0;
                outcome = CollideOrb(objRef, path, engine, isBeast);
                return true;
            }

            default:
            {
                if (BspSoleRelay(objRef.State))
                {
                    TraceSkippedAsBspOnly("cyl-skip-bsp", objRef);
                    return false;
                }

                outcome = CollideCylinder(objRef, path, engine);
                TraceCylinderTest(objRef, path, outcome);
                return true;
            }
        }
    }

    /// <summary>
    /// Sweeps against a BSP obstruction. The tree is authored in the object's own frame, so rather
    /// than transforming the tree the sweep is brought into that frame: undo the rotation, undo the
    /// scale, and move the origin onto the object.
    /// </summary>
    private ShiftVerdict SweepAgainstBsp(
        KineticEngine engine,
        ProxyEntry objRef,
        SweepPath path,
        GfxObjKinetics kinetics)
    {
        Quaternion invRot = Quaternion.Inverse(objRef.Rotation);
        float invScaling = objRef.Scale > 0 ? 1.0f / objRef.Scale : 1.0f;

        Vector3 ownSphere0Middle =
            Vector3.Transform(path.GlobalSphere[0].Center - objRef.Position, invRot) * invScaling;
        float ownSphere0Radius = path.GlobalSphere[0].Radius * invScaling;
        Vector3 ownCurrMiddle =
            Vector3.Transform(path.GlobalCurrCenter[0].Center - objRef.Position, invRot) * invScaling;

        bool hasOwnSphere1 = path.NumSphere > 1;
        Vector3 ownSphere1Middle = hasOwnSphere1
            ? Vector3.Transform(path.GlobalSphere[1].Center - objRef.Position, invRot) * invScaling
            : Vector3.Zero;
        float ownSphere1Radius = hasOwnSphere1
            ? path.GlobalSphere[1].Radius * invScaling
            : 0f;

        return ContactSweep.SeekImpacts(
            engine.DataCache!,
            kinetics,
            this,
            ownSphere0Middle,
            ownSphere0Radius,
            hasOwnSphere1,
            ownSphere1Middle,
            ownSphere1Radius,
            ownCurrMiddle,
            // Up, rotated into the object's frame.
            Vector3.Transform(Vector3.UnitZ, invRot),
            objRef.Scale,
            objRef.Rotation,
            engine,
            realmOrigin: objRef.Position);
    }



    private ShiftVerdict SweepStructures(KineticEngine engine, uint chamberIdent)
    {
        if ((chamberIdent & 0xFFFFu) >= 0x0100u) return ShiftVerdict.OK;
        if (engine.DataCache is null) return ShiftVerdict.OK;

        BuildingKinetics? structure = engine.DataCache.GetBuilding(chamberIdent);
        if (structure is null || structure.ModelId is 0u) return ShiftVerdict.OK;

        GfxObjKinetics? kinetics = engine.DataCache.FetchGfxObjRef(structure.ModelId);
        if (kinetics is null ||
            !ContactSweep.HasPhysics(engine.DataCache, kinetics))

            return ShiftVerdict.OK;

        SweepPath path = SweepPath;
        ContactLedger ledger = ContactLedger;
        MoverFacts facts = MoverFacts;

        if (!Matrix4x4.Decompose(structure.WorldTransform, out _,
                out Quaternion bldSpin, out Vector3 bldOrigin))
        {
            bldSpin = Quaternion.Identity;
            bldOrigin = structure.WorldTransform.Translation;
        }

        Quaternion invRot = Quaternion.Inverse(bldSpin);
        Vector3 ownSphere0Middle =
            Vector3.Transform(path.GlobalSphere[0].Center - bldOrigin, invRot);
        float ownSphere0Radius = path.GlobalSphere[0].Radius;
        bool hasOwnSphere1 = path.NumSphere > 1;
        Vector3 ownSphere1Middle = hasOwnSphere1
            ? Vector3.Transform(path.GlobalSphere[1].Center - bldOrigin, invRot)
            : Vector3.Zero;
        float ownSphere1Radius = hasOwnSphere1
            ? path.GlobalSphere[1].Radius
            : 0f;
        Vector3 ownCurrMiddle = Vector3.Transform(
            path.GlobalCurrCenter[0].Center - bldOrigin, invRot);
        Vector3 ownSpaceZ = Vector3.Transform(Vector3.UnitZ, invRot);

        path.BldgCheck = true;
        ShiftVerdict outcome;
        try
        {
            outcome = ContactSweep.SeekImpacts(
                engine.DataCache!,
                kinetics,
                this,
                ownSphere0Middle,
                ownSphere0Radius,
                hasOwnSphere1,
                ownSphere1Middle,
                ownSphere1Radius,
                ownCurrMiddle,
                ownSpaceZ,
                1.0f,            // buildings are unscaled
                bldSpin,
                engine,
                realmOrigin: bldOrigin);
        }
        finally
        {
            path.BldgCheck = false;
        }

        if (KineticTelemetry.ProbeBuildingEnabled)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"[bldg-channel] cell=0x{chamberIdent:X8} model=0x{structure.ModelId:X8} wpos=({path.GlobalSphere[0].Center.X:F3},{path.GlobalSphere[0].Center.Y:F3},{path.GlobalSphere[0].Center.Z:F3}) bldOrigin=({bldOrigin.X:F3},{bldOrigin.Y:F3},{bldOrigin.Z:F3}) hitsInterior={path.HitsInteriorCell} result={outcome}"));
        }

        if (outcome != ShiftVerdict.OK && !facts.Contact)
            ledger.CollidedWithEnvironment = true;

        return outcome;
    }

    private ShiftVerdict CollideOrb(ProxyEntry objRef, SweepPath path, KineticEngine engine, bool isBeast)
    {
        if (path.ObstructionEthereal)
            return ShiftVerdict.OK;

        ContactLedger ledger = ContactLedger;
        MoverFacts facts = MoverFacts;
        Orb sphere = path.GlobalSphere[0];
        Vector3 disp0 = sphere.Center - objRef.Position;
        float radsum = sphere.Radius + objRef.Radius - KineticConstants.EPSILON;

        bool hasFront = path.NumSphere > 1;
        Vector3 disp1 = default;
        if (hasFront)
            disp1 = path.GlobalSphere[1].Center - objRef.Position;

        if (path.InsertType == SlotKind.Placement)
        {
            if (OrbsTouch(disp0, radsum))
                return ShiftVerdict.Collided;
            return hasFront && OrbsTouch(disp1, radsum) ? ShiftVerdict.Collided : ShiftVerdict.OK;
        }

        if (path.StepDown)
        {
            return isBeast ? ShiftVerdict.OK : OrbHopDownOnto(objRef, path, disp0, radsum);
        }

        if (path.CheckWalkable)
        {
            if (OrbsTouch(disp0, radsum))
                return ShiftVerdict.Collided;
            return hasFront && OrbsTouch(disp1, radsum) ? ShiftVerdict.Collided : ShiftVerdict.OK;
        }

        if (!path.Collide)
        {
            if ((facts.State & (MoverState.Contact | MoverState.OnWalkable)) != 0)
            {
                // Grounded: foot hit → step over / slide; head hit → slide.
                if (OrbsTouch(disp0, radsum))
                    return OrbHopUp(objRef, path, engine, disp0, radsum);
                if (hasFront && OrbsTouch(disp1, radsum))
                    return OrbSlide(objRef, path, 1);
            }
            else if ((facts.State & MoverState.PathClipped) != 0)
            {
                if (OrbsTouch(disp0, radsum))
                    return OrbClip(objRef, path, sphere, radsum, 0);
            }
            else
            {
                // Airborne: foot hit → land on the sphere top; head hit → point hit
                if (OrbsTouch(disp0, radsum))
                    return OrbLand(objRef, path);
                if (hasFront && OrbsTouch(disp1, radsum))
                    return OrbClip(objRef, path, path.GlobalSphere[1], radsum, 1);
            }
            return ShiftVerdict.OK;
        }

        if (isBeast)
            return ShiftVerdict.OK;   // §8.1

        bool hit0 = OrbsTouch(disp0, radsum);
        if (!hit0 && !(hasFront && OrbsTouch(disp1, radsum)))
            return ShiftVerdict.OK;

        Vector3 travel = path.GlobalCurrCenter[0].Center - sphere.Center;
        float radsumEps = radsum + KineticConstants.EPSILON;
        float lengthSq = travel.LengthSquared();
        if (MathF.Abs(lengthSq) < KineticConstants.EPSILON)
            return ShiftVerdict.Collided;
        float diff = -Vector3.Dot(travel, disp0);
        float disc = diff * diff - (disp0.LengthSquared() - radsumEps * radsumEps) * lengthSq;
        if (disc < 0f)
            return ShiftVerdict.Collided;
        float tt = MathF.Sqrt(disc) + diff;
        if (tt > 1f) tt = diff * 2f - tt;
        float moment = tt / lengthSq;
        float timecheck = (1f - moment) * path.WalkInterp;
        if (timecheck >= path.WalkInterp || timecheck < -0.1f)
            return ShiftVerdict.Collided;
        travel *= moment;
        Vector3 dispNum = (disp0 + travel) / radsumEps;
        if (dispNum.Z <= path.WalkableAllowance)   // !is_walkable_allowable - sphere top too steep to rest on
            return ShiftVerdict.OK;
        Vector3 restPt = sphere.Center - dispNum * sphere.Radius;
        Plane linkPlane = new Plane(dispNum, -Vector3.Dot(dispNum, restPt));
        ledger.AssignLinkPlane(linkPlane, path.CheckCellId, isWater: true);
        path.WalkInterp = timecheck;
        path.AppendShiftToVerifySpot(travel);
        return ShiftVerdict.Adjusted;
    }

    private static bool OrbsTouch(Vector3 disp, float radsum)
        => disp.LengthSquared() <= radsum * radsum;

    private ShiftVerdict OrbHopUp(ProxyEntry objRef, SweepPath path,
        KineticEngine engine, Vector3 disp0, float radsum)
    {
        float radsumEps = radsum + KineticConstants.EPSILON;
        if (MoverFacts.StepUpHeight < radsumEps - disp0.Z)
            return OrbSlide(objRef, path, 0);

        Vector3 num = path.GlobalCurrCenter[0].Center - objRef.Position;

        if (engine is not null && DoStepUp(num, engine))
            return ShiftVerdict.OK;

        return path.StepUpSlide(this);
    }

    private ShiftVerdict OrbSlide(ProxyEntry objRef, SweepPath path, int orbCount)
    {
        Vector3 num = path.GlobalCurrCenter[orbCount].Center - objRef.Position;
        if (StandardizeUnlessTiny(ref num))
            return ShiftVerdict.Collided;
        return ShiftOrb(num, path.GlobalCurrCenter[orbCount].Center, orbCount);
    }

    private ShiftVerdict OrbLand(ProxyEntry objRef, SweepPath path)
    {
        Vector3 num = path.GlobalCurrCenter[0].Center - objRef.Position;
        if (StandardizeUnlessTiny(ref num))
            return ShiftVerdict.Collided;
        path.AssignCollide(num);
        path.WalkableAllowance = KineticConstants.LandingZ;
        return ShiftVerdict.Adjusted;
    }

    private ShiftVerdict OrbClip(ProxyEntry objRef, SweepPath path,
        Orb verifyOrb, float radsum, int orbCount)
    {
        Vector3 gMiddle = path.GlobalCurrCenter[orbCount].Center;
        Vector3 globalShift = gMiddle - objRef.Position;

        if ((MoverFacts.State & MoverState.PerfectClip) == 0)
        {
            if (!StandardizeUnlessTiny(ref globalShift))
                ContactLedger.AssignImpactNorm(globalShift);
            return ShiftVerdict.Collided;
        }

        KineticTelemetry.CaptureOrbPerfectClipRearReach(
            (MoverFacts.State & MoverState.IsViewer) != 0);

        // PerfectClip exact time-of-impact reposition. Block offset = 0.
        Vector3 verifyShift = verifyOrb.Center - gMiddle;
        double toi = OrbMomentOfImpact(verifyShift, globalShift, radsum + KineticConstants.EPSILON);
        if (toi < KineticConstants.EPSILON || toi > 1.0)
            return ShiftVerdict.Collided;
        Vector3 impactShift = verifyShift * (float)toi - verifyShift;
        Vector3 formerDisp = impactShift + verifyOrb.Center - objRef.Position;
        ContactLedger.AssignImpactNorm(formerDisp / radsum);
        path.AppendShiftToVerifySpot(formerDisp);
        return ShiftVerdict.Adjusted;
    }

    private ShiftVerdict OrbHopDownOnto(ProxyEntry objRef, SweepPath path,
        Vector3 disp0, float radsum)
    {
        bool strike = OrbsTouch(disp0, radsum);
        if (!strike && path.NumSphere > 1)
        {
            Vector3 disp1 = path.GlobalSphere[1].Center - objRef.Position;
            strike = OrbsTouch(disp1, radsum);
        }
        if (!strike)
            return ShiftVerdict.OK;

        float hopDown = path.StepDownAmt * path.WalkInterp;
        if (MathF.Abs(hopDown) < KineticConstants.EPSILON)
            return ShiftVerdict.Collided;

        float radsumEps = radsum + KineticConstants.EPSILON;
        float underTrunk = radsumEps * radsumEps - (disp0.X * disp0.X + disp0.Y * disp0.Y);
        if (underTrunk < 0f)
            return ShiftVerdict.Collided;   // defensive: XY already outside radsum
        float val = MathF.Sqrt(underTrunk);
        float scaledHop = (val - disp0.Z) / hopDown;
        float timecheck = (1f - scaledHop) * path.WalkInterp;
        if (timecheck >= path.WalkInterp || timecheck < -0.1f)
            return ShiftVerdict.Collided;

        float lerp = hopDown * scaledHop;
        Vector3 dispNum = new Vector3(disp0.X, disp0.Y, disp0.Z + lerp) / radsumEps;
        if (dispNum.Z <= path.WalkableAllowance)
            return ShiftVerdict.OK;

        Vector3 restPt = objRef.Position + dispNum * objRef.Radius;
        Plane restPlane = new Plane(dispNum, -Vector3.Dot(dispNum, restPt));
        ContactLedger.AssignLinkPlane(restPlane, path.CheckCellId, isWater: true);
        path.WalkInterp = timecheck;
        path.AppendShiftToVerifySpot(new Vector3(0f, 0f, lerp));
        return ShiftVerdict.Adjusted;
    }

    private static double OrbMomentOfImpact(Vector3 travel, Vector3 orbSpot, float radTotal)
    {
        float distanceSq = travel.LengthSquared();
        if (distanceSq < KineticConstants.EPSILON) return -1;
        float nonCollide = orbSpot.LengthSquared() - radTotal * radTotal;
        if (nonCollide < KineticConstants.EPSILON) return -1;
        float similar = -Vector3.Dot(orbSpot, travel);
        double nonCollideB = (double)similar * similar - (double)nonCollide * distanceSq;
        if (nonCollideB < 0) return -1;
        double cDistance = Math.Sqrt(nonCollideB);
        if (similar - cDistance < 0)
            return -1 * (cDistance + similar) / distanceSq;
        return -1 * (similar - cDistance) / distanceSq;
    }

    private ShiftVerdict CollideCylinder(ProxyEntry objRef, SweepPath path, KineticEngine engine)
    {
        // Degenerate dat heights: registration sites apply the same fallback;
        // kept for entries registered before it (pre-dates this port).
        float cylHeight = objRef.CylHeight > 0f ? objRef.CylHeight : objRef.Radius * 4f;

        Orb sphere = path.GlobalSphere[0];
        Vector3 disp0 = sphere.Center - objRef.Position;
        float radsum = objRef.Radius - KineticConstants.EPSILON + sphere.Radius;

        bool hasFront = path.NumSphere > 1;
        Vector3 disp1 = default;
        float frontRadius = 0f;
        if (hasFront)
        {
            disp1 = path.GlobalSphere[1].Center - objRef.Position;
            frontRadius = path.GlobalSphere[1].Radius;
        }

        if (path.InsertType == SlotKind.Placement || path.ObstructionEthereal)
        {
            if (CylinderTouches(disp0, radsum, cylHeight, sphere.Radius))
                return ShiftVerdict.Collided;
            if (hasFront && CylinderTouches(disp1, radsum, cylHeight, frontRadius))
                return ShiftVerdict.Collided;
            return ShiftVerdict.OK;
        }

        if (path.StepDown)
            return CylinderHopDownOnto(objRef, path, cylHeight, disp0, radsum);

        if (path.CheckWalkable)
        {
            if (CylinderTouches(disp0, radsum, cylHeight, sphere.Radius))
                return ShiftVerdict.Collided;
            if (hasFront && CylinderTouches(disp1, radsum, cylHeight, frontRadius))
                return ShiftVerdict.Collided;
            return ShiftVerdict.OK;
        }

        MoverFacts facts = MoverFacts;

        if (!path.Collide)
        {
            if ((facts.State & (MoverState.Contact | MoverState.OnWalkable)) != 0)
            {
                // Grounded mover: foot hit → step over / onto; head hit → slide.
                if (CylinderTouches(disp0, radsum, cylHeight, sphere.Radius))
                    return CylinderHopUp(objRef, path, engine, cylHeight, disp0, radsum);
                if (hasFront && CylinderTouches(disp1, radsum, cylHeight, frontRadius))
                    return CylinderSlide(objRef, path, cylHeight, disp1, radsum, 1);
            }
            else if ((facts.State & MoverState.PathClipped) != 0)
            {
                if (CylinderTouches(disp0, radsum, cylHeight, sphere.Radius))
                    return CylinderClip(objRef, path, cylHeight, sphere, disp0, radsum, 0);
            }
            else
            {
                // Airborne: foot hit → land on the top; head hit → point hit.
                if (CylinderTouches(disp0, radsum, cylHeight, sphere.Radius))
                    return CylinderLand(objRef, path, cylHeight, disp0, radsum);
                if (hasFront && CylinderTouches(disp1, radsum, cylHeight, frontRadius))
                    return CylinderClip(objRef, path, cylHeight, path.GlobalSphere[1], disp1, radsum, 1);
            }
            return ShiftVerdict.OK;
        }

        if (CylinderTouches(disp0, radsum, cylHeight, sphere.Radius)
            || (hasFront && CylinderTouches(disp1, radsum, cylHeight, frontRadius)))
        {
            Vector3 travel = path.GlobalCurrCenter[0].Center - sphere.Center;
            if (MathF.Abs(travel.Z) < KineticConstants.EPSILON)
                return ShiftVerdict.Collided;

            float timecheck = (cylHeight + sphere.Radius - disp0.Z) / travel.Z;
            Vector3 shift = travel * timecheck;

            Vector3 total = shift + disp0;
            if (radsum * radsum < total.X * total.X + total.Y * total.Y)
                return ShiftVerdict.OK;   // rewound point is off the cap - not a top landing

            float t = (1f - timecheck) * path.WalkInterp;
            if (t >= path.WalkInterp || t < -0.1f)
                return ShiftVerdict.Collided;

            Vector3 pt = sphere.Center + shift;
            pt.Z -= sphere.Radius;
            Plane linkPlane = new Plane(Vector3.UnitZ, -pt.Z);
            ContactLedger.AssignLinkPlane(linkPlane, path.CheckCellId, isWater: true);
            path.WalkInterp = t;
            path.AppendShiftToVerifySpot(shift);
            return ShiftVerdict.Adjusted;
        }

        return ShiftVerdict.OK;
    }

    private static bool CylinderTouches(Vector3 disp, float radsum, float cylHeight, float orbRadius)
    {
        if (disp.X * disp.X + disp.Y * disp.Y <= radsum * radsum)
        {
            float halfH = cylHeight * 0.5f;
            if (orbRadius - KineticConstants.EPSILON + halfH >= MathF.Abs(halfH - disp.Z))
                return true;
        }
        return false;
    }

    private bool CylinderNorm(ProxyEntry objRef, SweepPath path, float cylHeight,
        Vector3 dispVerify, float radsum, float orbRadius, int orbCount, out Vector3 norm)
    {
        Vector3 dispCurr = path.GlobalCurrCenter[orbCount].Center - objRef.Position;
        if (radsum * radsum < dispCurr.X * dispCurr.X + dispCurr.Y * dispCurr.Y)
        {
            norm = new Vector3(dispCurr.X, dispCurr.Y, 0f);
            float halfH = cylHeight * 0.5f;
            bool zBandOverlapAtCurr =
                orbRadius - KineticConstants.EPSILON + halfH >= MathF.Abs(halfH - dispCurr.Z);
            bool noZTravel = MathF.Abs(dispCurr.Z - dispVerify.Z) <= KineticConstants.EPSILON;
            return zBandOverlapAtCurr || noZTravel;
        }
        norm = new Vector3(0f, 0f, dispVerify.Z - dispCurr.Z <= 0f ? 1f : -1f);
        return true;
    }

    private static bool StandardizeUnlessTiny(ref Vector3 v)
    {
        float mag = v.Length();
        if (mag < KineticConstants.EPSILON)
            return true;
        v /= mag;
        return false;
    }

    private ShiftVerdict CylinderHopUp(ProxyEntry objRef, SweepPath path, KineticEngine engine,
        float cylHeight, Vector3 disp0, float radsum)
    {
        Orb sphere = path.GlobalSphere[0];

        if (MoverFacts.StepUpHeight < sphere.Radius + cylHeight - disp0.Z)
            return CylinderSlide(objRef, path, cylHeight, disp0, radsum, 0);

        CylinderNorm(objRef, path, cylHeight, disp0, radsum, sphere.Radius, 0, out var num);
        if (StandardizeUnlessTiny(ref num))
            return ShiftVerdict.Collided;

        Vector3 numRealm = Vector3.Transform(num, objRef.Rotation);

        if (engine is not null && DoStepUp(numRealm, engine))
            return ShiftVerdict.OK;

        return path.StepUpSlide(this);
    }

    private ShiftVerdict CylinderHopDownOnto(ProxyEntry objRef, SweepPath path,
        float cylHeight, Vector3 disp0, float radsum)
    {
        Orb sphere = path.GlobalSphere[0];

        bool strike = CylinderTouches(disp0, radsum, cylHeight, sphere.Radius);
        if (!strike && path.NumSphere > 1)
        {
            Vector3 disp1 = path.GlobalSphere[1].Center - objRef.Position;
            strike = CylinderTouches(disp1, radsum, cylHeight, path.GlobalSphere[1].Radius);
        }
        if (!strike)
            return ShiftVerdict.OK;

        float hopScaling = path.StepDownAmt * path.WalkInterp;
        if (MathF.Abs(hopScaling) < KineticConstants.EPSILON)
            return ShiftVerdict.Collided;

        float diffZ = cylHeight + sphere.Radius - disp0.Z;
        float lerp = (1f - diffZ / hopScaling) * path.WalkInterp;
        if (lerp >= path.WalkInterp || lerp < -0.1f)
            return ShiftVerdict.Collided;

        float topZ = sphere.Center.Z + diffZ - sphere.Radius;
        Plane linkPlane = new Plane(Vector3.UnitZ, -topZ);
        ContactLedger.AssignLinkPlane(linkPlane, path.CheckCellId, isWater: true);
        path.WalkInterp = lerp;
        path.AppendShiftToVerifySpot(new Vector3(0f, 0f, diffZ));
        return ShiftVerdict.Adjusted;
    }

    private ShiftVerdict CylinderSlide(ProxyEntry objRef, SweepPath path,
        float cylHeight, Vector3 disp, float radsum, int orbCount)
    {
        CylinderNorm(objRef, path, cylHeight, disp, radsum,
            path.GlobalSphere[orbCount].Radius, orbCount, out var num);
        if (StandardizeUnlessTiny(ref num))
            return ShiftVerdict.Collided;

        return ShiftOrb(num, path.GlobalCurrCenter[orbCount].Center, orbCount);
    }

    private ShiftVerdict CylinderLand(ProxyEntry objRef, SweepPath path,
        float cylHeight, Vector3 disp0, float radsum)
    {
        CylinderNorm(objRef, path, cylHeight, disp0, radsum,
            path.GlobalSphere[0].Radius, 0, out var num);
        if (StandardizeUnlessTiny(ref num))
            return ShiftVerdict.Collided;

        path.AssignCollide(num);
        path.WalkableAllowance = KineticConstants.LandingZ;
        return ShiftVerdict.Adjusted;
    }

    private ShiftVerdict CylinderClip(ProxyEntry objRef, SweepPath path,
        float cylHeight, Orb verifyOrb, Vector3 disp, float radsum, int orbCount)
    {
        bool definite = CylinderNorm(objRef, path, cylHeight, disp, radsum,
            verifyOrb.Radius, orbCount, out var num);
        if (StandardizeUnlessTiny(ref num))
            return ShiftVerdict.Collided;

        if ((MoverFacts.State & MoverState.PerfectClip) == 0)
        {
            ContactLedger.AssignImpactNorm(num);
            return ShiftVerdict.Collided;
        }

        KineticTelemetry.CaptureCylPerfectClipRearReach(
            (MoverFacts.State & MoverState.IsViewer) != 0);

        Vector3 globMiddle = path.GlobalCurrCenter[0].Center;
        Vector3 travel = verifyOrb.Center - globMiddle;
        Vector3 formerDisp = globMiddle - objRef.Position;
        float radsumEps = radsum + KineticConstants.EPSILON;

        float xyRelocateLengthSq = travel.X * travel.X + travel.Y * travel.Y;
        float dot2d = travel.X * formerDisp.X + travel.Y * formerDisp.Y;
        float xyDiff = -dot2d;
        float formerDispXYSq = formerDisp.X * formerDisp.X + formerDisp.Y * formerDisp.Y;
        float diffSq = xyDiff * xyDiff - (formerDispXYSq - radsumEps * radsumEps) * xyRelocateLengthSq;

        float moment;
        Vector3 scaledTravel;

        if (!definite)
        {
            if (MathF.Abs(travel.Z) < KineticConstants.EPSILON)
                return ShiftVerdict.Collided;
            if (travel.Z > 0f)
            {
                num = new Vector3(0f, 0f, -1f);
                moment = (travel.Z + verifyOrb.Radius) / travel.Z * -1f;
            }
            else
            {
                num = new Vector3(0f, 0f, 1f);
                moment = (verifyOrb.Radius + cylHeight - travel.Z) / travel.Z;
            }
            scaledTravel = travel * moment;

            Vector3 landed = scaledTravel + formerDisp;
            if (landed.X * landed.X + landed.Y * landed.Y >= radsumEps * radsumEps)
            {
                if (MathF.Abs(xyRelocateLengthSq) < KineticConstants.EPSILON)
                    return ShiftVerdict.Collided;
                if (diffSq >= 0f && xyRelocateLengthSq > KineticConstants.EPSILON)
                {
                    float diff = MathF.Sqrt(diffSq);
                    moment = xyDiff - diff < 0f
                        ? (diff - dot2d) / xyRelocateLengthSq
                        : (xyDiff - diff) / xyRelocateLengthSq;
                    scaledTravel = travel * moment;
                }
                num = (scaledTravel + globMiddle - objRef.Position) / radsumEps;
                num.Z = 0f;
            }

            if (moment < 0f || moment > 1f)
                return ShiftVerdict.Collided;

            Vector3 shiftOut = globMiddle - scaledTravel - verifyOrb.Center;
            path.AppendShiftToVerifySpot(shiftOut);
            ContactLedger.AssignImpactNorm(num);
            return ShiftVerdict.Adjusted;
        }

        if (num.Z != 0f)
        {
            if (MathF.Abs(travel.Z) < KineticConstants.EPSILON)
                return ShiftVerdict.Collided;

            moment = travel.Z > 0f
                ? -((formerDisp.Z + verifyOrb.Radius) / travel.Z)
                : (verifyOrb.Radius + cylHeight - formerDisp.Z) / travel.Z;
            scaledTravel = travel * moment;

            if (moment < 0f || moment > 1f)
                return ShiftVerdict.Collided;

            Vector3 shiftOut = globMiddle + scaledTravel - verifyOrb.Center;
            path.AppendShiftToVerifySpot(shiftOut);
            ContactLedger.AssignImpactNorm(num);
            return ShiftVerdict.Adjusted;
        }

        if (diffSq < 0f || xyRelocateLengthSq < KineticConstants.EPSILON)
            return ShiftVerdict.Collided;

        {
            float diff = MathF.Sqrt(diffSq);
            moment = xyDiff - diff < 0f
                ? (diff - dot2d) / xyRelocateLengthSq
                : (xyDiff - diff) / xyRelocateLengthSq;
            scaledTravel = travel * moment;

            if (moment < 0f || moment > 1f)
                return ShiftVerdict.Collided;

            num = (scaledTravel + globMiddle - objRef.Position) / radsumEps;
            num.Z = 0f;

            Vector3 shiftOut = globMiddle + scaledTravel - verifyOrb.Center;
            path.AppendShiftToVerifySpot(shiftOut);
            ContactLedger.AssignImpactNorm(num);
            return ShiftVerdict.Adjusted;
        }
    }
}
