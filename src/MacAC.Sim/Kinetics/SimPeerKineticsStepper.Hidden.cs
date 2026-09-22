using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

// The reduced tick for movers that are hidden or out of the live area
internal sealed partial class SimPeerKineticsStepper
{
    internal bool PulseConcealed(
        SimActorRecord capture,
        PeerMotion motion,
        float dt,
        ulong objectTimerEpoch,
        float radius,
        float height,
        MotionTableKeeper?
            pieceArrHndTravel = null,
        System.Action<uint, AnimSequencer>?
            procAnimTaps = null,
        AnimSequencer? scheduler = null,
        System.Func<SimPeerKineticsCapture, bool>?
            acknowledgeProj = null,
        System.Func<bool>? externalHolderValid = null,
        ImmutableArray<PackedContactSphere>
            orbRoster = default,
        float orbScaling = 1f,
        float hopUpHeight = 0.4f,
        float hopDownHeight = 0.4f,
        MoverState carrierPvpPhase =
            MoverState.None)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(motion);
        if (!OwnsCore(
                capture,
                motion,
                objectTimerEpoch,
                externalHolderValid))

            return false;
        uint ownActorIdent = capture.OwnActorTag
            ?? throw new InvalidOperationException(
                $"Runtime entity 0x{capture.ServerGuid:X8}/{capture.Incarnation} has no local identity");

        KineticTelemetry.CommenceDistantSlideAttribution(
            capture.ServerGuid);

        Vector3 preConstructLocus = motion.Body.Position;

        var locusDiff =
            motion.LocusKeeperDiffTemp;
        locusDiff.Reset();
        motion.Position.ConstructShift(
            dt,
            motion.Body.Position,
            motion.Body.Orientation,
            locusDiff,
            motion.Lerp,
            motion.Motion.FetchAdjustedUpperPace(),
            locusDiff,
            inLink: motion.Body.InContact,
                    isSticky: (motion.Host?.LocusKeeper.FetchStickyObjectIdent() ?? 0u) is not 0u);
        motion.Host?.LocusKeeper.AdjustOffset(locusDiff, dt);
        if (motion.Host is { } concealedHub)
            motion.Body.IsFullyConstrained = concealedHub.LocusKeeper.IsFullyConstrained();
        FoldKeeperDiff(motion.Body, locusDiff);

        if (scheduler is not null)
            procAnimTaps?.Invoke(ownActorIdent, scheduler);
        if (!OwnsCore(
                capture,
                motion,
                objectTimerEpoch,
                externalHolderValid))

            return false;

        Vector3 composedLocus = motion.Body.Position;
        uint sealedChamberIdent = motion.CellId;
        if (motion.CellId is not 0
            && composedLocus != preConstructLocus
            && _register.Engine.LandblockTally > 0)
        {
            if (radius < 0.05f)
            {
                radius = 0.48f;
                height = 1.835f;
            }

            bool earlierLink = motion.Body.InContact;
            bool earlierOnPassable = motion.Body.OnWalkable;
            ResolveVerdict settled = _register.Engine.ResolveWithTransition(
                preConstructLocus,
                composedLocus,
                motion.CellId,
                radius,
                height,
                hopUpHeight: hopUpHeight,
                hopDownHeight: hopDownHeight,
                isOnTerrain: earlierOnPassable,
                corpus: motion.Body,
                carrierFlagSet: (IsAvatarOid(capture.ServerGuid)
                    ? MoverState.IsPlayer
                      | MoverState.EdgeSlide
                    : MoverState.EdgeSlide)
                    | carrierPvpPhase,
                movingActorIdent: ownActorIdent,
                orbRoster: orbRoster,
                orbScaling: orbScaling);
            motion.Body.Position = settled.Position;
            if (settled.CellId is not 0)
                sealedChamberIdent = settled.CellId;
            if (!KineticObjUpdate.SealSetLocusChangeover(
                motion.Body,
                settled.InContact,
                settled.OnWalkable,
                settled.CollisionNormalValid,
                settled.CollisionNormal,
                earlierLink,
                earlierOnPassable,
                motion.Movement.HitGround,
                motion.Motion.LeaveGround,
                () => OwnsCore(
                    capture,
                    motion,
                    objectTimerEpoch,
                    externalHolderValid)))

                return false;
            motion.Airborne = !motion.Body.OnWalkable;
        }

        if (!(acknowledgeProj?.Invoke(
                new SimPeerKineticsCapture(
                    motion.Body.Position,
                    motion.Body.Orientation,
                    sealedChamberIdent)) ?? true)
            || !OwnsCore(
                capture,
                motion,
                objectTimerEpoch,
                externalHolderValid))

            return false;
        if (sealedChamberIdent is not 0 && sealedChamberIdent != motion.CellId)
            motion.CellId = sealedChamberIdent;
        if (!OwnsCore(
                capture,
                motion,
                objectTimerEpoch,
                externalHolderValid))

            return false;

        CanonObjectKeeperTail.Run(
            motion.Host?.MarkKeeper,
            motion.Movement,
            pieceArrHndTravel,
            locus: null);
        if (OwnsCore(capture, motion, objectTimerEpoch, externalHolderValid))
        {
            ProgressInterpolationRecovery(capture, motion, orbRoster, orbScaling,
                hopUpHeight, hopDownHeight,
                (IsAvatarOid(capture.ServerGuid)
                    ? MoverState.IsPlayer | MoverState.EdgeSlide
                    : MoverState.EdgeSlide) | carrierPvpPhase, radius, height);
        }
        if (OwnsCore(capture, motion, objectTimerEpoch, externalHolderValid))
            motion.Host?.LocusKeeper.UseMoment();
        return OwnsCore(
            capture,
            motion,
            objectTimerEpoch,
            externalHolderValid);
    }
}
