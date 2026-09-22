using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

internal sealed partial class SimPeerKineticsStepper
{
    internal void ProgressInterpolationRecovery(
        SimActorRecord capture, PeerMotion distant,
        ImmutableArray<PackedContactSphere> orbs,
        float scaling, float hopUpHeight, float hopDownHeight,
        MoverState carrierFlagSet,
        float radius = 0.48f, float height = 1.835f)
    {
        bool OwnsCorpus() => _register.Entities.IsCurrent(capture)
            && ReferenceEquals(capture.PeerMotion, distant)
            && ReferenceEquals(capture.KineticBody, distant.Body);
        if (!OwnsCorpus())
            return;
        var stance = _register.SetPosition;
        bool hasMark = distant.Lerp.TryFetchRecoveryMark(out var mark);
        var queued = distant.InterpolationRecoveryStance;
        if (queued.IsValid)
        {
            if (hasMark && mark == distant.InterpolationRecoveryMark)
            {
                if (stance.TryGlimpseAcknowledgedStance(queued, out var proj))
                {
                    stance.AbsorbAcknowledgedStance(queued, proj);
                    stance.DropStanceWrapUp(queued);
                    distant.InterpolationRecoveryStance = default;
                    distant.Lerp.ConcludeRecovery(mark);
                    return;
                }
                if (stance.IsStanceLatest(queued))
                    return;
            }
            CancelRecovery(distant);
            if (!OwnsCorpus())
                return;
            hasMark = distant.Lerp.TryFetchRecoveryMark(out mark);
        }
        if (!hasMark || mark.CellId is 0u
            || !_register.TryFetchRealmCycleShift(mark.CellId, out float shiftX, out float shiftY))

            return;
        if (orbs.IsDefaultOrEmpty)
        {
            if (radius < 0.05f)
            {
                radius = 0.48f;
                height = 1.835f;
            }
            orbs = height > 0f
                ? [new(new(0f, 0f, radius), radius),
                   new(new(0f, 0f, height - radius), radius)]
                : [new(new(0f, 0f, radius), radius)];
            scaling = 1f;
        }
        var ticket = stance.TryCommenceExclusiveStance(
            capture, capture.PositionAuthorityVersion, SimSetPositionOperationKind.RemoteAuthoritative);
        if (!ticket.IsValid)
            return;
        distant.InterpolationRecoveryStance = ticket;
        distant.InterpolationRecoveryMark = mark;
        stance.MonitorStanceWrapUp(ticket);
        var req = new KineticSetPositionRequest(
            mark.Position, mark.Orientation, mark.CellId,
            mark.Position - new Vector3(shiftX, shiftY, 0f),
            orbs, scaling, hopUpHeight, hopDownHeight,
            MoverFlags: carrierFlagSet,
            Flags: KineticSetPositionFlags.Teleport
                | KineticSetPositionFlags.Slide
                | KineticSetPositionFlags.SendPositionEvent);
        SimSetPositionDirective directive = new SimSetPositionDirective(req,
            SimSetPositionOperationKind.RemoteAuthoritative,
            _register.StanceSimulationMoment(distant.Body.PreviousRefreshMoment),
            capture.VelArbiterVer, shiftX, shiftY);
        var verdict = stance.SubmitReadiedStance(ticket, directive);
        if (!OwnsCorpus() || distant.InterpolationRecoveryStance != ticket)
            return;
        if (verdict.Error == PlaceError.Ok
            && verdict.Residence == KineticResidenceVerdict.Committed
            && stance.IsStanceWrapUpFollowed(ticket))
        {
            stance.DropStanceWrapUp(ticket);
            distant.InterpolationRecoveryStance = default;
            distant.Lerp.ConcludeRecovery(mark);
        }
        else if (verdict.Status != SimSetPositionStatus.DeferredCell)
        {
            CancelRecovery(distant);
        }
    }

    private void CancelRecovery(PeerMotion distant)
    {
        var ticket = distant.InterpolationRecoveryStance;
        distant.InterpolationRecoveryStance = default;
        _register.SetPosition.DropStanceWrapUp(ticket);
        var abort = _register.SetPosition
            .DropPreciseStance(ticket, revertCancelledPark: true);
        if (abort.IsValid)
            _register.SetPosition.BroadcastAbort(abort);
    }
}
