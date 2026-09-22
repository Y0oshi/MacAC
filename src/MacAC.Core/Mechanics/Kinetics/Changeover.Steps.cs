using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

/// <summary>The step and slide primitives every collision path ends in.</summary>
public sealed partial class Changeover
{
    internal ShiftVerdict ShiftOrbInternal(Vector3 impactNorm, Vector3 currSpot)
        => ShiftOrb(impactNorm, currSpot);

    internal Vector3 AdjustOffset(Vector3 shift)
    {
        SweepPath path = SweepPath;
        ContactLedger ledger = ContactLedger;

        Vector3 outcome = shift;
        bool verifySlide = false;
        string branch = "init";

        float slidingAngle = Vector3.Dot(outcome, ledger.SlidingNormal);
        if (ledger.SlidingNormalValid)
        {
            if (slidingAngle < 0f)
                verifySlide = true;
            else
                ledger.SlidingNormalValid = false;
        }

        if (!ledger.ContactPlaneValid)
        {
            if (verifySlide)
            {
                outcome -= ledger.SlidingNormal * slidingAngle;
                branch = "no-cp-slide";
            }
            else
            {
                branch = "no-cp";
            }

            if (KineticTelemetry.ProbeStepWalkEnabled)
                KineticTelemetry.TraceHopStrollAdjust(
                    branch, shift, outcome,
                    linkPlane: null,
                    slidingValid: ledger.SlidingNormalValid,
                    slidingNorm: ledger.SlidingNormal,
                    impactAngle: 0f,
                    strollLerp: path.WalkInterp);

            KineticTelemetry.RecordPassageAdjustShift(
                MoverFacts.SelfEntityId, branch, shift, outcome);

            return outcome;
        }

        float impactAngle = Vector3.Dot(outcome, ledger.ContactPlane.Normal);
        Vector3 slideShift = Vector3.Cross(ledger.ContactPlane.Normal, ledger.SlidingNormal);

        if (verifySlide)
        {
            float slideLength = slideShift.Length();
            if (slideLength < KineticConstants.EPSILON)
            {
                outcome = Vector3.Zero;
                branch = "slide-degenerate";
            }
            else
            {
                slideShift /= slideLength;
                outcome = Vector3.Dot(slideShift, outcome) * slideShift;
                branch = "slide-crease";
            }
        }
        else if (impactAngle <= 0f)
        {
            outcome -= ledger.ContactPlane.Normal * impactAngle;
            branch = "into-plane";
        }
        else
        {
            Vector3 num = ledger.ContactPlane.Normal;
            if (MathF.Abs(num.Z) > KineticConstants.EPSILON)
                outcome.Z = -(outcome.X * num.X + outcome.Y * num.Y) / num.Z;
            branch = "away-plane";
        }

        if (ledger.ContactPlaneCellId is not 0 && !ledger.ContactPlaneIsWater)
        {
            branch = AdjustOffsetBranch(path, ledger, branch);
        }

        if (KineticTelemetry.ProbeStepWalkEnabled)
            KineticTelemetry.TraceHopStrollAdjust(
                branch, shift, outcome,
                linkPlane: ledger.ContactPlane,
                slidingValid: ledger.SlidingNormalValid,
                slidingNorm: ledger.SlidingNormal,
                impactAngle: impactAngle,
                strollLerp: path.WalkInterp);

        KineticTelemetry.RecordPassageAdjustShift(
            MoverFacts.SelfEntityId, branch, shift, outcome);

        return outcome;
    }

    private string AdjustOffsetBranch(SweepPath path, ContactLedger ledger, string branch)
    {
        Vector3 globMiddle = path.GlobalSphere[0].Center;
        float radius = path.GlobalSphere[0].Radius;
        float distance = Vector3.Dot(globMiddle, ledger.ContactPlane.Normal)
                                 + ledger.ContactPlane.D;
        if (distance < radius - KineticConstants.EPSILON)
        {
            float zDistance = (radius - distance) / ledger.ContactPlane.Normal.Z;
            if (radius > MathF.Abs(zDistance))
            {
                path.AppendShiftToVerifySpot(new Vector3(0f, 0f, zDistance));
                branch += "+safety-push";
            }
        }

        return branch;
    }

    internal bool DoHopDownForTest(
        float hopDownHeight,
        float passableZ,
        KineticEngine engine)
        => StepDownBy(hopDownHeight, passableZ, engine);

    internal bool DoStepUp(Vector3 impactNorm, KineticEngine engine)
    {
        SweepPath path = SweepPath;
        ContactLedger ledger = ContactLedger;
        MoverFacts facts = MoverFacts;

        KineticTelemetry.RecordPassageHopUp(
            facts.SelfEntityId, "enter", impactNorm,
            onPassable: (facts.State & MoverState.OnWalkable) != 0,
            hopUpHeight: facts.StepUpHeight,
            spot: path.CurPos,
            succeeded: null,
            landedNorm: null);

        bool storedCpValid = ledger.ContactPlaneValid;
        Plane storedCp = ledger.ContactPlane;
        uint storedCpChamberIdent = ledger.ContactPlaneCellId;
        bool storedCpIsWater = ledger.ContactPlaneIsWater;

        ledger.ContactPlaneValid = false;
        ledger.ContactPlaneIsWater = false;

        path.StepUp = true;
        path.StepUpNormal = impactNorm;

        float hopDownHeight = 0.04f;
        float zLandingVal = KineticConstants.LandingZ;

        if ((facts.State & MoverState.OnWalkable) != 0)
        {
            zLandingVal = facts.FetchPassableZ();
            hopDownHeight = facts.StepUpHeight;
        }

        path.WalkableAllowance = zLandingVal;
        path.PersistVerifySpot();

        bool hopDown = StepDownBy(hopDownHeight, zLandingVal, engine);

        path.StepUp = false;
        path.WipePassable();

        KineticTelemetry.RecordPassageHopUp(
            facts.SelfEntityId, "exit", impactNorm,
            onPassable: (facts.State & MoverState.OnWalkable) != 0,
            hopUpHeight: facts.StepUpHeight,
            spot: path.CheckPos,
            succeeded: hopDown,
            landedNorm: hopDown && ledger.ContactPlaneValid
                ? ledger.ContactPlane.Normal
                : null);

        if (!hopDown)
        {
            path.ReinstateVerifySpot();

            if (storedCpValid)
            {
                DoStepUpBranch(ledger, storedCp, storedCpChamberIdent, storedCpIsWater);
            }
        }

        return hopDown;
    }

    private void DoStepUpBranch(ContactLedger ledger, Plane storedCp, uint storedCpChamberIdent, bool storedCpIsWater)
    {
        ledger.ContactPlane = storedCp;
        ledger.ContactPlaneValid = true;
        ledger.ContactPlaneCellId = storedCpChamberIdent;
        ledger.ContactPlaneIsWater = storedCpIsWater;
    }

    internal bool DoVerifyPassable(float zVerify, KineticEngine engine)
    {
        SweepPath path = SweepPath;
        MoverFacts facts = MoverFacts;

        if ((facts.State & MoverState.OnWalkable) == 0)
            return true;

        if (path.VerifyWalkables())
            return true;

        Vector3 storedVerifySpot = path.CheckPos;
        uint storedVerifyChamberIdent = path.CheckCellId;
        Vector3 storedBackupVerifySpot = path.BackupCheckPos;
        uint storedBackupVerifyChamberIdent = path.BackupCheckCellId;

        float hopHeight = facts.StepDownHeight;
        Orb globOrb = path.GlobalSphere[0];

        if (path.NumSphere < 2 && hopHeight > globOrb.Radius * 2f)
            hopHeight = globOrb.Radius * 0.5f;

        if (hopHeight > globOrb.Radius * 2f)
            hopHeight *= 0.5f;

        path.WalkableAllowance = zVerify;
        path.CheckWalkable = true;
        path.AppendShiftToVerifySpot(new Vector3(0f, 0f, -hopHeight));

        ShiftVerdict passagePhase = AttemptSlot(1, engine);

        path.CheckWalkable = false;
        path.AssignVerifySpot(storedVerifySpot, storedVerifyChamberIdent);
        path.BackupCheckPos = storedBackupVerifySpot;
        path.BackupCheckCellId = storedBackupVerifyChamberIdent;

        return passagePhase != ShiftVerdict.OK;
    }

    private ShiftVerdict ShiftOrb(Vector3 impactNorm, Vector3 currSpot, int orbCount = 0)
    {
        SweepPath path = SweepPath;
        ContactLedger ledger = ContactLedger;

        if (impactNorm.LengthSquared() < KineticConstants.EpsilonSq)
        {
            Vector3 halfShift = (currSpot - path.GlobalSphere[orbCount].Center) * 0.5f;
            path.AppendShiftToVerifySpot(halfShift);
            return ShiftVerdict.Adjusted;
        }

        ledger.AssignImpactNorm(impactNorm);

        Vector3 gDiff = path.GlobalSphere[orbCount].Center - currSpot;

        System.Numerics.Plane linkPlane;
        if (ledger.ContactPlaneValid)
            linkPlane = ledger.ContactPlane;
        else if (ledger.LastKnownContactPlaneValid)
            linkPlane = ledger.LastKnownContactPlane;
        else
        {
            float diff = Vector3.Dot(impactNorm, gDiff);
            Vector3 shift = -impactNorm * diff;
            path.AppendShiftToVerifySpot(shift);
            return ShiftVerdict.Slid;
        }

        Vector3 dir = Vector3.Cross(impactNorm, linkPlane.Normal);
        float directionLengthSq = dir.LengthSquared();

        if (directionLengthSq >= KineticConstants.EPSILON)
        {
            float diff = Vector3.Dot(dir, gDiff);
            float invDirectionLengthSq = 1f / directionLengthSq;
            Vector3 shift = dir * diff * invDirectionLengthSq;

            if (shift.LengthSquared() < KineticConstants.EPSILON)
                return ShiftVerdict.Collided;

            shift -= gDiff;
            path.AppendShiftToVerifySpot(shift);
            return ShiftVerdict.Slid;
        }

        if (Vector3.Dot(impactNorm, linkPlane.Normal) >= 0f)
        {
            float diff = Vector3.Dot(impactNorm, gDiff);
            Vector3 shift = -impactNorm * diff;
            path.AppendShiftToVerifySpot(shift);
            return ShiftVerdict.Slid;
        }

        Vector3 reversed = -gDiff;
        if (reversed.LengthSquared() > KineticConstants.EpsilonSq)
        {
            reversed = Vector3.Normalize(reversed);
            ledger.AssignImpactNorm(reversed);
        }
        return ShiftVerdict.Collided;
    }

    private bool StepDownBy(float hopDownHeight, float passableZ, KineticEngine engine)
    {
        SweepPath path = SweepPath;

        path.NegPolyHit = false;
        path.StepDown = true;
        path.StepDownAmt = hopDownHeight;
        path.WalkInterp = 1.0f;

        bool hopStrollSensor = KineticTelemetry.ProbeStepWalkEnabled;
        if (hopStrollSensor)
        {
            KineticTelemetry.TraceHopStroll(
                "stepdown-enter", -1, 0, path, ContactLedger, MoverFacts,
                Vector3.Zero, Vector3.Zero,
                specifics: $"height={hopDownHeight:F4} walkableZ={passableZ:F4}");
        }

        if (!path.StepUp)
        {
            Vector3 downShift = new Vector3(0f, 0f, -hopDownHeight);
            path.AppendShiftToVerifySpot(downShift);

            if (hopStrollSensor)
            {
                KineticTelemetry.TraceHopStroll(
                    "stepdown-after-offset", -1, 0, path, ContactLedger, MoverFacts,
                    downShift, downShift,
                    specifics: $"height={hopDownHeight:F4} walkableZ={passableZ:F4}");
            }
        }

        ShiftVerdict passagePhase = AttemptSlot(5, engine);

        if (hopStrollSensor)
        {
            KineticTelemetry.TraceHopStroll(
                "stepdown-after-insert", -1, 0, path, ContactLedger, MoverFacts,
                Vector3.Zero, Vector3.Zero,
                passagePhase,
                $"height={hopDownHeight:F4} walkableZ={passableZ:F4}");
        }

        path.StepDown = false;

        if (KineticTelemetry.ProbeIndoorBspEnabled)
        {
            Vector3 cpn = ContactLedger.ContactPlane.Normal;
            bool admit = passagePhase == ShiftVerdict.OK
                          && ContactLedger.ContactPlaneValid
                          && cpn.Z >= passableZ;
            Console.WriteLine(FormattableString.Invariant(
                $"[stepdown-decide] cell=0x{path.CheckCellId:X8} insert={passagePhase} cpValid={ContactLedger.ContactPlaneValid} cpNz={cpn.Z:F3} walkableZ={passableZ:F3} walkInterp={path.WalkInterp:F3} accept={admit} pos=({path.CheckPos.X:F3},{path.CheckPos.Y:F3},{path.CheckPos.Z:F3})"));
        }

        if (passagePhase == ShiftVerdict.OK
            && ContactLedger.ContactPlaneValid
            && ContactLedger.ContactPlane.Normal.Z >= passableZ)
        {
            if (MoverFacts.EdgeSlide
                && !path.StepUp
                && !DoVerifyPassable(passableZ, engine))

                return false;

            SlotKind storedSlot = path.InsertType;
            float winterpPriorStance = path.WalkInterp;
            path.InsertType = SlotKind.Placement;

            ShiftVerdict placePhase = AttemptSlot(1, engine);

            path.InsertType = storedSlot;

            if (placePhase != ShiftVerdict.OK
                && KineticTelemetry.ProbePlacementFailEnabled)
            {
                var info = System.Globalization.CultureInfo.InvariantCulture;
                Console.WriteLine(string.Format(info,
                    "[place-fail] source=StepDownBy returned={0} " +
                    "sphere=({1:F4},{2:F4},{3:F4}) cell=0x{4:X8} " +
                    "stepDownHeight={5:F4} walkableZ={6:F4} " +
                    "winterpBefore={7:F4} " +
                    "contactPlane.Nz={8:F4} contactPlaneValid={9} spStepUp={10}",
                    placePhase,
                    path.CheckPos.X, path.CheckPos.Y, path.CheckPos.Z,
                    path.CheckCellId,
                    hopDownHeight, passableZ,
                    winterpPriorStance,
                    ContactLedger.ContactPlane.Normal.Z,
                    ContactLedger.ContactPlaneValid,
                    path.StepUp));
            }

            if (hopStrollSensor)
            {
                KineticTelemetry.TraceHopStroll(
                    "stepdown-after-placement", -1, 0, path, ContactLedger, MoverFacts,
                    Vector3.Zero, Vector3.Zero,
                    placePhase,
                    $"height={hopDownHeight:F4} walkableZ={passableZ:F4} winterpBeforePlacement={winterpPriorStance:F4}");
            }

            return placePhase == ShiftVerdict.OK;
        }

        if (hopStrollSensor)
        {
            KineticTelemetry.TraceHopStroll(
                "stepdown-reject", -1, 0, path, ContactLedger, MoverFacts,
                Vector3.Zero, Vector3.Zero,
                passagePhase,
                $"height={hopDownHeight:F4} walkableZ={passableZ:F4}");
        }

        return false;
    }
}
