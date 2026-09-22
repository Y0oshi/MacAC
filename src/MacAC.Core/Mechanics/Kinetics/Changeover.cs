using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

public sealed partial class Changeover
{
    public readonly MoverFacts MoverFacts = new();

    public readonly SweepPath SweepPath = new();

    public readonly ContactLedger ContactLedger = new();

    public void ResetForReuse()
    {
        MoverFacts.RewindForReuse();
        SweepPath.ResetForReuse();
        ContactLedger.RestartForReuse();
    }

    public static bool BspSoleRelay(uint actorPhase)
        => (actorPhase & (uint)KineticStateFlags.HasPhysicsBsp) is not 0;

    public bool SeekTransitionalLocus(KineticEngine engine)
    {
        SweepPath path = SweepPath;

        MoverFacts.VelocityKilled = false;

        if (path.CurCellId is 0)
            return false;

        Vector3 shift = path.EndPos - path.BeginPos;
        float distance = shift.Length();
        float radius = path.LocalSphere[0].Radius;

        if (radius <= KineticConstants.EPSILON)
            return false;

        int countHops;
        Vector3 shiftPerHop;

        if (MoverFacts.IsViewer)
        {
            if (distance > KineticConstants.EPSILON)
            {
                shiftPerHop = shift * (radius / distance);      // radius-length steps
                countHops = (int)MathF.Floor(distance / radius) + 1;
            }
            else
            {
                countHops = 0;
                shiftPerHop = Vector3.Zero;
            }
        }
        else
        {
            float hop = distance / radius;

            if (hop > 1.0f)
            {
                countHops = (int)MathF.Ceiling(hop);
                shiftPerHop = shift * (1f / countHops);
            }
            else if (shift != Vector3.Zero)
            {
                countHops = 1;
                shiftPerHop = shift;
            }
            else
            {
                countHops = 0;
                shiftPerHop = Vector3.Zero;
            }
        }

        // Apply free rotation if requested
        if (MoverFacts.ReleaseSpin)
            path.CurOrientation = path.EndOrientation;

        path.AssignVerifySpot(path.CurPos, path.CurCellId);

        if (countHops <= 0)
        {
            if (!MoverFacts.ReleaseSpin)
                path.CurOrientation = path.EndOrientation;
            return true;
        }

        var changeoverPhase = ShiftVerdict.OK;
        bool hopStrollSensor = KineticTelemetry.ProbeStepWalkEnabled;

        if (hopStrollSensor)
        {
            KineticTelemetry.TraceHopStroll(
                "find-start", -1, countHops, path, ContactLedger, MoverFacts,
                Vector3.Zero, Vector3.Zero,
                changeoverPhase,
                $"dist={distance:F4} radius={radius:F4}");
        }

        for (int idx = 0; idx < countHops; ++idx)
        {
            if (MoverFacts.IsViewer && idx == countHops - 1 && distance > KineticConstants.EPSILON)
                shiftPerHop = shift * ((distance - (countHops - 1) * radius) / distance);

            Vector3 askedShift = shiftPerHop;

            path.GlobalOffset = AdjustOffset(askedShift);

            if (hopStrollSensor)
            {
                KineticTelemetry.TraceHopStroll(
                    "after-adjust", idx, countHops, path, ContactLedger, MoverFacts,
                    askedShift, path.GlobalOffset,
                    changeoverPhase);
            }

            if (!MoverFacts.IsViewer
                && path.GlobalOffset.LengthSquared() < KineticConstants.EpsilonSq)
            {
                if (hopStrollSensor)
                {
                    KineticTelemetry.TraceHopStroll(
                        "abort-small-offset", idx, countHops, path, ContactLedger, MoverFacts,
                        askedShift, path.GlobalOffset,
                        changeoverPhase,
                        $"offsetLenSq={path.GlobalOffset.LengthSquared():F8}");
                }
                return idx is not 0 && changeoverPhase == ShiftVerdict.OK;
            }

            // Interpolate orientation (non-free-rotate path)
            if (!MoverFacts.ReleaseSpin)
            {
                float diff = (idx + 1f) / countHops;
                path.CheckOrientation = Quaternion.Slerp(path.BeginOrientation, path.EndOrientation, diff);
            }

            ContactLedger.SlidingNormalValid = false;
            ContactLedger.ContactPlaneValid = false;
            ContactLedger.ContactPlaneIsWater = false;

            path.AppendShiftToVerifySpot(path.GlobalOffset);

            if (hopStrollSensor)
            {
                KineticTelemetry.TraceHopStroll(
                    "before-insert", idx, countHops, path, ContactLedger, MoverFacts,
                    askedShift, path.GlobalOffset,
                    changeoverPhase);
            }

            ShiftVerdict outcome = AttemptSlot(3, engine);

            if (hopStrollSensor)
            {
                KineticTelemetry.TraceHopStroll(
                    "after-insert", idx, countHops, path, ContactLedger, MoverFacts,
                    askedShift, path.GlobalOffset,
                    outcome);
            }

            changeoverPhase = JudgeChangeover(outcome);

            if (hopStrollSensor)
            {
                KineticTelemetry.TraceHopStroll(
                    "after-validate", idx, countHops, path, ContactLedger, MoverFacts,
                    askedShift, path.GlobalOffset,
                    changeoverPhase);
            }

            if (ContactLedger.FramesStationaryFall is not 0
                || (ContactLedger.CollisionNormalValid && MoverFacts.PathClipped))
                break;
        }

        if (hopStrollSensor)
        {
            KineticTelemetry.TraceHopStroll(
                "find-end", -1, countHops, path, ContactLedger, MoverFacts,
                Vector3.Zero, Vector3.Zero,
                changeoverPhase);
        }

        return changeoverPhase == ShiftVerdict.OK;
    }

    internal void DuplicateFrom(Changeover src)
    {
        ArgumentNullException.ThrowIfNull(src);
        MoverFacts.DuplicateFrom(src.MoverFacts);
        SweepPath.DuplicateFrom(src.SweepPath);
        ContactLedger.DuplicateFrom(src.ContactLedger);
    }

    internal bool ImposeAnotherChamberOutcome(ShiftVerdict outcome, out ShiftVerdict finalPhase)
    {
        finalPhase = outcome;
        switch (outcome)
        {
            case ShiftVerdict.Collided:
            case ShiftVerdict.Adjusted:
                if ((MoverFacts.State & MoverState.Contact) == 0)
                    ContactLedger.CollidedWithEnvironment = true;
                return true;
            case ShiftVerdict.Slid:
                ContactLedger.ContactPlaneValid = false;
                ContactLedger.ContactPlaneIsWater = false;
                return true;
            default:
                return false;
        }
    }

    private ShiftVerdict JudgeChangeover(ShiftVerdict changeoverPhase)
    {
        SweepPath path = SweepPath;
        ContactLedger ledger = ContactLedger;
        MoverFacts facts = MoverFacts;

        bool cleanProceed = changeoverPhase == ShiftVerdict.OK && path.CheckPos != path.CurPos;

        if (changeoverPhase == ShiftVerdict.OK && path.CheckPos != path.CurPos)
        {
            JudgeChangeoverBranch2(path);
        }
        else if (changeoverPhase == ShiftVerdict.OK)
        {
            // No movement (same position): accept as-is
            path.AssignVerifySpot(path.CurPos, path.CurCellId);
        }
        else if (changeoverPhase != ShiftVerdict.Invalid)
        {
            if (ledger.LastKnownContactPlaneValid)
            {
                JudgeChangeoverBranch(facts, path, ledger);
            }

            if (!ledger.CollisionNormalValid)
                ledger.AssignImpactNorm(Vector3.UnitZ);   // default: push up

            path.AssignVerifySpot(path.CurPos, path.CurCellId);
            changeoverPhase = ShiftVerdict.OK;
        }

        if (ledger.CollisionNormalValid)
            ledger.AssignSlidingNorm(ledger.CollisionNormal);

        if (ledger.ContactPlaneValid)
        {
            JudgeChangeoverBranch3(ledger, facts);
        }
        else
        {
            ledger.LastKnownContactPlaneValid = false;
            facts.State &= ~(MoverState.Contact | MoverState.OnWalkable);
        }

        if (!facts.IsViewer && facts.MoverHasGravity)
        {
            bool replay = cleanProceed || facts.OnWalkable;
            if (!replay)
            {
                int fsf = ledger.FramesStationaryFall;
                if (fsf > 0)
                {
                    if (fsf > 1)
                    {
                        JudgeChangeoverBranch4(ledger, path, facts);
                    }
                    else
                        ledger.FramesStationaryFall = 2;
                }
                else
                    ledger.FramesStationaryFall = 1;
            }
            else
                ledger.FramesStationaryFall = 0;
        }

        return changeoverPhase;
    }

    private void JudgeChangeoverBranch(MoverFacts facts, SweepPath path, ContactLedger ledger)
    {
        facts.StopVelocity();
        Vector3 orbMiddle = path.GlobalCurrCenter[0].Center;
        float radius = path.GlobalSphere[0].Radius;
        float angle = Vector3.Dot(ledger.LastKnownContactPlane.Normal, orbMiddle)
                                      + ledger.LastKnownContactPlane.D;
        if (radius + KineticConstants.EPSILON > MathF.Abs(angle))
        {
            ledger.AssignLinkPlane(
                ledger.LastKnownContactPlane,
                ledger.LastKnownContactPlaneCellId,
                ledger.LastKnownContactPlaneIsWater);
        }
    }

    private void JudgeChangeoverBranch2(SweepPath path)
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
        path.AssignVerifySpot(path.CurPos, path.CurCellId);
    }

    private void JudgeChangeoverBranch3(ContactLedger ledger, MoverFacts facts)
    {
        ledger.LastKnownContactPlaneValid = true;
        ledger.LastKnownContactPlane = ledger.ContactPlane;
        ledger.LastKnownContactPlaneCellId = ledger.ContactPlaneCellId;
        JudgeChangeoverTail(ledger, facts);
    }

    private void JudgeChangeoverTail(ContactLedger ledger, MoverFacts facts)
    {
        ledger.LastKnownContactPlaneIsWater = ledger.ContactPlaneIsWater;
        facts.State |= MoverState.Contact;
        if (ledger.ContactPlane.Normal.Z >= KineticConstants.FloorZ)
            facts.State |= MoverState.OnWalkable;
        else
            facts.State &= ~MoverState.OnWalkable;
    }

    private void JudgeChangeoverBranch4(ContactLedger ledger, SweepPath path, MoverFacts facts)
    {
        ledger.FramesStationaryFall = 3;
        Vector3 up = Vector3.UnitZ;
        float d = path.GlobalSphere[0].Radius - path.GlobalSphere[0].Center.Z;
        ledger.AssignLinkPlane(new Plane(up, d), path.CheckCellId, isWater: false);
        if (!facts.Contact)
        {
            ledger.AssignImpactNorm(up);
            ledger.CollidedWithEnvironment = true;
        }
        JudgeChangeoverTail2(ledger, facts);
    }

    private void JudgeChangeoverTail2(ContactLedger ledger, MoverFacts facts)
    {
        facts.State |= MoverState.Contact | MoverState.OnWalkable;
        ledger.LastKnownContactPlaneValid = true;
        ledger.LastKnownContactPlane = ledger.ContactPlane;
        ledger.LastKnownContactPlaneCellId = ledger.ContactPlaneCellId;
        ledger.LastKnownContactPlaneIsWater = ledger.ContactPlaneIsWater;
    }
}
