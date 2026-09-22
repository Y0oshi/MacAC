using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Placement: finding a valid resting spot for a body that is not moving.</summary>
public sealed partial class Changeover
{

    public bool SeekStanceSpot(KineticEngine engine)
    {
        SweepPath path = SweepPath;

        path.AssignVerifySpot(path.CurPos, path.CurCellId);
        ContactLedger.SlidingNormalValid = false;
        ContactLedger.ContactPlaneValid = false;
        ContactLedger.ContactPlaneIsWater = false;

        var changeoverPhase = JudgeStanceShift(
            AttemptSlot(3, engine));
        if (changeoverPhase == ShiftVerdict.OK)
            return true;

        if (!path.PlacementAllowsSliding)
            return false;

        float adjustGap = 4f;
        float adjustRadius = adjustGap;
        float orbRadius = path.LocalSphere[0].Radius;
        bool fakeOrb = false;

        if (orbRadius < 0.125f)
        {
            fakeOrb = true;
            adjustRadius = 2f;
        }
        else if (orbRadius < 0.48f)
        {
            orbRadius = 0.48f;
        }

        double hopTallyPrecise = 4d / (double)orbRadius;
        if (fakeOrb)
            hopTallyPrecise *= 0.5d;
        if (hopTallyPrecise <= 1d)
            return false;

        int hopTally = (int)Math.Ceiling(hopTallyPrecise);
        float gapPerHop = (float)((double)adjustRadius / hopTally);
        float radiansPerHop = (float)(
            ((double)gapPerHop / orbRadius)
            * 3.14159989f);
        float sumGap = 0f;
        float sumRadians = 0f;

        for (int loop = 0; loop < hopTally; ++loop)
        {
            sumGap += gapPerHop;
            sumRadians += radiansPerHop;

            int specimenTally = (int)Math.Ceiling((double)sumRadians) * 2;
            float bearingHop = (float)(360d / specimenTally);

            for (int specimen = 0; specimen < specimenTally; ++specimen)
            {
                path.AssignVerifySpot(path.CurPos, path.CurCellId);

                float bearing = bearingHop * specimen;
                Vector3 shift = FetchStanceCompassShift(
                    bearing,
                    sumGap);
                path.GlobalOffset = AdjustOffset(shift);

                if (path.GlobalOffset.LengthSquared()
                    < KineticConstants.EpsilonSq)
                    continue;

                path.AppendShiftToVerifySpot(path.GlobalOffset);
                ContactLedger.SlidingNormalValid = false;
                ContactLedger.ContactPlaneValid = false;
                ContactLedger.ContactPlaneIsWater = false;

                changeoverPhase = JudgeStanceShift(
                    AttemptSlot(3, engine));
                if (changeoverPhase == ShiftVerdict.OK)
                    return true;
            }
        }

        return false;
    }
    internal bool SeekValidLocus(KineticEngine engine)
    {
        return SweepPath.InsertType == SlotKind.Transition
            ? SeekTransitionalLocus(engine)
            : SeekStanceLocus(engine);
    }

    internal bool SeekStanceLocus(KineticEngine engine)
    {
        SweepPath path = SweepPath;
        path.AssignVerifySpot(path.CurPos, path.CurCellId);
        path.InsertType = SlotKind.InitialPlacement;

        var starting = JudgeStance(
            engine,
            LeadStanceSlot(engine),
            reattemptStance: true);
        if (starting != ShiftVerdict.OK)
            return false;

        path.InsertType = SlotKind.Placement;
        if (!SeekStanceSpot(engine))
            return false;

        if (MoverFacts.StepDown)
        {
            const float stancePassableAllowance = 0.0871556997f;
            float hopDownHeight = MoverFacts.StepDownHeight;
            path.WalkableAllowance = stancePassableAllowance;
            path.PersistVerifySpot();
            SlotKind storedSlot = path.InsertType;
            path.InsertType = SlotKind.Transition;

            (float sensorHeight, int sensorTally) = FetchHopDownSensorPlan(
                path.NumSphere,
                path.GlobalSphere[0].Radius,
                hopDownHeight);
            bool stepped = StepDownBy(
                sensorHeight,
                stancePassableAllowance,
                engine);
            if (!stepped && sensorTally > 1)
            {
                stepped = StepDownBy(
                    sensorHeight,
                    stancePassableAllowance,
                    engine);
            }
            if (!stepped)
            {
                path.ReinstateVerifySpot();
                ContactLedger.ContactPlaneValid = false;
                ContactLedger.ContactPlaneIsWater = false;
            }
            path.InsertType = storedSlot;
            path.WipePassable();
        }

        return JudgeStance(
                engine,
                ShiftVerdict.OK,
                reattemptStance: true)
            == ShiftVerdict.OK;
    }

    internal static Vector3 FetchStanceCompassShift(
        float bearingDeg,
        float gap)
    {
        const double DegToRadians = 0.017453292519943295d;
        double radians = (double)bearingDeg * DegToRadians;
        return new Vector3(
            (float)Math.Sin(radians) * gap,
            (float)Math.Cos(radians) * gap,
            0f);
    }

    internal ShiftVerdict VetStanceChangeoverForTest(
        ShiftVerdict changeoverPhase) =>
        JudgeStanceShift(changeoverPhase);

    internal ShiftVerdict VetStanceForTest(
        KineticEngine engine,
        ShiftVerdict changeoverPhase,
        bool reattemptStance) =>
        JudgeStance(engine, changeoverPhase, reattemptStance);

    internal ShiftVerdict TransitionalSlotForTest(
        int countAttempts,
        KineticEngine engine)
        => AttemptSlot(countAttempts, engine);

    internal bool RimSlideFollowingHopDownFailedForTest(
        KineticEngine engine,
        float hopDownHeight,
        float zValue,
        out ShiftVerdict outcome)
        => RimSlideBackup(engine, hopDownHeight, zValue, out outcome);

    internal ShiftVerdict CliffSlideForTest(Plane linkPlane)
        => ShiftOffCliff(linkPlane);

    internal static (float ProbeHeight, int ProbeCount) FetchHopDownSensorPlan(
        int countOrbs,
        float orbRadius,
        float askedHeight)
    {
        float diameter = orbRadius * 2f;
        float sensorHeight = askedHeight;

        if (countOrbs < 2 && diameter <= sensorHeight)
            sensorHeight = orbRadius * 0.5f;

        if (diameter > sensorHeight)
            return (sensorHeight, 1);

        return (sensorHeight * 0.5f, 2);
    }

    private ShiftVerdict JudgeStanceShift(
        ShiftVerdict changeoverPhase)
    {
        SweepPath path = SweepPath;
        if (path.CheckCellId is 0)
            return ShiftVerdict.Collided;

        if (changeoverPhase == ShiftVerdict.OK)
        {
            path.CurPos = path.CheckPos;
            path.CurCellId = path.CheckCellId;
            path.CurOrientation = path.CheckOrientation;
            for (int idx = 0; idx < path.NumSphere; ++idx)
            {
                path.GlobalCurrCenter[idx].Center =
                    Vector3.Transform(path.LocalSphere[idx].Center, path.CurOrientation) + path.CurPos;
                path.GlobalCurrCenter[idx].Radius = path.LocalSphere[idx].Radius;
            }
        }
        else if (changeoverPhase > ShiftVerdict.OK
                 && changeoverPhase <= ShiftVerdict.Slid
                 && path.PlacementAllowsSliding)
        {
            ContactLedger.RestartForReuse();
        }

        return changeoverPhase;
    }

    private ShiftVerdict JudgeStance(
        KineticEngine engine,
        ShiftVerdict changeoverPhase,
        bool reattemptStance)
    {
        SweepPath path = SweepPath;
        if (path.CheckCellId is 0u)
            return ShiftVerdict.Collided;

        if (changeoverPhase == ShiftVerdict.OK)
        {
            path.CurPos = path.CheckPos;
            path.CurCellId = path.CheckCellId;
            path.CurOrientation = path.CheckOrientation;
            for (int idx = 0; idx < path.NumSphere; ++idx)
            {
                path.GlobalCurrCenter[idx].Center =
                    Vector3.Transform(
                        path.LocalSphere[idx].Center,
                        path.CurOrientation)
                    + path.CurPos;
                path.GlobalCurrCenter[idx].Radius = path.LocalSphere[idx].Radius;
            }
        }
        else if ((changeoverPhase is ShiftVerdict.Adjusted
                  or ShiftVerdict.Slid)
                 && reattemptStance)
        {
            return JudgeStance(
                engine,
                StanceSlotAll(engine),
                reattemptStance: false);
        }
        return changeoverPhase;
    }

    private ShiftVerdict AttemptSlot(int countAttempts, KineticEngine engine)
    {
        if (SweepPath.CheckCellId is 0) return ShiftVerdict.OK;
        if (countAttempts <= 0) return ShiftVerdict.Invalid;

        SweepPath path = SweepPath;
        ContactLedger ledger = ContactLedger;
        MoverFacts facts = MoverFacts;

        var passagePhase = ShiftVerdict.OK;

        for (int attempt = 0; attempt < countAttempts; ++attempt)
        {
            passagePhase = SlotInto(
                engine,
                path.CheckCellId,
                countAttempts);

            if (passagePhase == ShiftVerdict.Collided)
            {
                path.NegPolyHit = false;
                return ShiftVerdict.Collided;
            }

            if (passagePhase == ShiftVerdict.Slid)
            {
                ledger.ContactPlaneValid = false;
                ledger.ContactPlaneIsWater = false;
                path.NegPolyHit = false;
                continue;
            }

            if (passagePhase == ShiftVerdict.Adjusted)
            {
                path.NegPolyHit = false;
                continue;
            }

            if (passagePhase != ShiftVerdict.OK)
                continue;

            ShiftVerdict anotherPhase = VerifyAnotherChambersThenProceed(
                engine, path.GlobalSphere[0].Center, path.GlobalSphere[0].Radius);

            if (anotherPhase != ShiftVerdict.OK)
                path.NegPolyHit = false;

            if (anotherPhase == ShiftVerdict.Collided)
                return ShiftVerdict.Collided;

            if (anotherPhase != ShiftVerdict.OK)
            {
                passagePhase = anotherPhase;
                continue;   // ADJUSTED / SLID → retry the attempt
            }

            if (path.Collide)
            {
                path.Collide = false;

                bool restart = false;
                if (ledger.ContactPlaneValid && DoVerifyPassable(KineticConstants.LandingZ, engine))
                {
                    SlotKind storedSlot = path.InsertType;
                    path.InsertType = SlotKind.Placement;

                    var placePhase = AttemptSlot(countAttempts, engine);

                    path.InsertType = storedSlot;

                    if (placePhase != ShiftVerdict.OK)
                    {
                        // Placement rejected - fall through to restore
                        placePhase = ShiftVerdict.OK;
                        restart = true;
                    }
                    else if (!restart)
                    {
                        path.WipePassable();
                        return placePhase;
                    }
                }
                else
                    restart = true;

                path.WipePassable();

                if (restart)
                {
                    path.ReinstateVerifySpot();
                    ledger.ContactPlaneValid = false;
                    ledger.ContactPlaneIsWater = false;

                    bool diagSteep = KineticTelemetry.DumpSteepRoofEnabled;
                    if (diagSteep)
                    {
                        Console.WriteLine(
                            $"[steep-roof] PHASE3-RESET lastKnownValid={ledger.LastKnownContactPlaneValid} " +
                            $"checkPos=({path.CheckPos.X:F2},{path.CheckPos.Y:F2},{path.CheckPos.Z:F2}) " +
                            $"curPos=({path.CurPos.X:F2},{path.CurPos.Y:F2},{path.CurPos.Z:F2}) " +
                            $"stepUpNormal=({path.StepUpNormal.X:F2},{path.StepUpNormal.Y:F2},{path.StepUpNormal.Z:F2})");
                    }

                    if (ledger.LastKnownContactPlaneValid)
                    {
                        AttemptSlotBranch(ledger, facts, diagSteep);
                    }
                    else
                    {
                        ledger.AssignImpactNorm(path.StepUpNormal);
                    }

                    return ShiftVerdict.Collided;
                }
            }

            if (path.NegPolyHit && !path.StepDown && !path.StepUp)
            {
                path.NegPolyHit = false;

                if (KineticTelemetry.ProbeBuildingEnabled || KineticTelemetry.ProbeIndoorBspEnabled)
                {
                    Console.WriteLine(FormattableString.Invariant(
                        $"[neg-poly-dispatch] stepUp={path.NegStepUp} n=({path.NegCollisionNormal.X:F3},{path.NegCollisionNormal.Y:F3},{path.NegCollisionNormal.Z:F3})"));
                }

                if (path.NegStepUp)
                {
                    if (DoStepUp(path.NegCollisionNormal, engine))
                    {
                    }
                    else
                    {
                        ShiftVerdict hopUpSlideRes = path.StepUpSlide(this);
                        if (hopUpSlideRes == ShiftVerdict.Slid)
                        {
                            passagePhase = hopUpSlideRes;
                            ledger.ContactPlaneValid = false;
                            ledger.ContactPlaneIsWater = false;
                            continue;
                        }
                        if (hopUpSlideRes != ShiftVerdict.OK)
                            return hopUpSlideRes;
                    }
                }
                else
                {
                    ShiftVerdict slideRes = ShiftOrbInternal(
                        path.NegCollisionNormal, path.GlobalCurrCenter[0].Center);
                    if (slideRes == ShiftVerdict.Collided)
                        return ShiftVerdict.Collided;   // degenerate slide → hard stop
                    passagePhase = slideRes;
                    continue;
                }
            }

            if (ledger.ContactPlaneValid)
                return ShiftVerdict.OK;

            if (facts.Contact && !path.StepDown && path.CheckCellId is not 0 && facts.StepDown)
            {
                float zValue = facts.FetchPassableZ();
                float hopDownHeight = facts.StepDownHeight;
                path.WalkableAllowance = zValue;
                path.PersistVerifySpot();

                (float sensorHeight, int sensorTally) = FetchHopDownSensorPlan(
                    path.NumSphere,
                    path.GlobalSphere[0].Radius,
                    hopDownHeight);
                hopDownHeight = sensorHeight;

                bool steppedDown = false;
                for (int sensor = 0; sensor < sensorTally; ++sensor)
                {
                    if (StepDownBy(hopDownHeight, zValue, engine))
                    {
                        steppedDown = true;
                        break;
                    }
                }

                if (steppedDown)
                {
                    path.WipePassable();
                    return ShiftVerdict.OK;
                }

                bool halt = RimSlideBackup(
                    engine,
                    hopDownHeight,
                    zValue,
                    out ShiftVerdict rimPhase);
                if (halt)
                    return rimPhase;

                if (rimPhase == ShiftVerdict.Slid)
                {
                    passagePhase = rimPhase;
                    ledger.ContactPlaneValid = false;
                    ledger.ContactPlaneIsWater = false;
                    path.NegPolyHit = false;
                    continue;
                }

                if (rimPhase == ShiftVerdict.Adjusted)
                {
                    passagePhase = rimPhase;
                    path.NegPolyHit = false;
                    continue;
                }

                passagePhase = rimPhase;
                continue;
            }

            return ShiftVerdict.OK;
        }

        return passagePhase;
    }

    private void AttemptSlotBranch(ContactLedger ledger, MoverFacts facts, bool diagSteep)
    {
        ledger.LastKnownContactPlaneValid = false;
        facts.StopVelocity();
        if (diagSteep)
            Console.WriteLine($"[steep-roof] PHASE3-RESET-KILLV ← StopVelocity called");
    }

    private ShiftVerdict SlotInto(
        KineticEngine engine,
        uint chamberIdent,
        int countAttempts)
    {
        if (chamberIdent is 0)
            return ShiftVerdict.Collided;

        var phase = ShiftVerdict.OK;
        for (int attempt = 0; attempt < countAttempts; ++attempt)
        {
            phase = SweepPrimaryChamber(engine, chamberIdent, attempt);
            if (phase is ShiftVerdict.OK or ShiftVerdict.Collided)
                return phase;

            if (phase == ShiftVerdict.Slid)
            {
                ContactLedger.ContactPlaneValid = false;
                ContactLedger.ContactPlaneIsWater = false;
            }
        }

        return phase;
    }

    private ShiftVerdict LeadStanceSlot(KineticEngine engine)
    {
        SweepPath path = SweepPath;
        if (path.CheckCellId is 0u)
            return ShiftVerdict.Collided;

        var outcome = SlotInto(
            engine,
            path.CheckCellId,
            countAttempts: 3);
        if (outcome == ShiftVerdict.OK)
        {
            outcome = VerifyAnotherChambersThenProceed(
                engine,
                path.GlobalSphere[0].Center,
                path.GlobalSphere[0].Radius);
        }
        return outcome;
    }

    private ShiftVerdict StanceSlotAll(KineticEngine engine)
    {
        SweepPath path = SweepPath;
        if (path.CheckCellId is 0u)
            return ShiftVerdict.Collided;

        var outcome = SlotInto(
            engine,
            path.CheckCellId,
            countAttempts: 3);
        return outcome == ShiftVerdict.OK
            ? VerifyAnotherChambersThenProceed(
                engine,
                path.GlobalSphere[0].Center,
                path.GlobalSphere[0].Radius)
            : outcome;
    }
}
