using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

internal sealed partial class SimPeerKineticsStepper
{
    private const double StaleSrvVelSecs = 0.60;

    private readonly SimKineticsLedger _register;

    internal SimPeerKineticsStepper(
        SimKineticsLedger physics)
    {
        _register = physics ?? throw new ArgumentNullException(nameof(physics));
    }

    private static bool IsAvatarOid(uint oid) => (oid & 0xFF000000u) == 0x50000000u;

    internal bool Tick(
        SimActorRecord capture,
        PeerMotion motion,
        float objectScaling,
        AnimSequencer? scheduler,
        float dt,
        ulong objectTimerEpoch,
        MotionDeltaPose trunkLocomotionOwnCycle,
        float radius,
        float height,
        int onlineMiddleX,
        int onlineMiddleY,
        System.Action<uint, AnimSequencer>?
            procAnimTaps = null,
        System.Action<Vector3>? enactStaleVelCycle = null,
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
        ArgumentNullException.ThrowIfNull(trunkLocomotionOwnCycle);
        if (!OwnsCore(
                capture,
                motion,
                objectTimerEpoch,
                externalHolderValid))
        {
            return false;
        }
        uint srvOid = capture.ServerGuid;
        KineticTelemetry.CommenceDistantSlideAttribution(
            srvOid);
        uint ownActorIdent = capture.OwnActorTag
            ?? throw new InvalidOperationException(
                $"Runtime entity 0x{srvOid:X8}/{capture.Incarnation} has no local identity");
        {
            double instantSec = _register.UtcInstantSecs;

            bool corpusOnPassableAtBeatBegin = motion.Body.OnWalkable;
            Vector3 scaledTrunkLocomotionOwnOrigin =
                corpusOnPassableAtBeatBegin
                    ? trunkLocomotionOwnCycle.Origin * objectScaling
                    : Vector3.Zero;

            bool slideForcedLink = !motion.Body.InContact;
            bool slideForcedPassable = !motion.Body.OnWalkable;
            Vector3 slideVelPriorZero = motion.Body.Velocity;

            motion.Body.TransientState |=
                TransientPhaseFlagSet.Active;

            if (!motion.Airborne)
            {
                bool relocateToLoaded = motion.MoveTo is
                { TravelKindPhase: not TravelKind.Invalid };
                bool stickyLoaded =
                    (motion.Host?.LocusKeeper.FetchStickyObjectIdent() ?? 0u) != 0u;
                if (!IsAvatarOid(srvOid) && motion.HasSrvVel
                    && !relocateToLoaded && !stickyLoaded)
                {
                    double velAge = instantSec - motion.PreviousSrvSpotMoment;
                    if (velAge > StaleSrvVelSecs)
                    {
                        motion.SrvVel = Vector3.Zero;
                        motion.HasSrvVel = false;
                        enactStaleVelCycle?.Invoke(
                            Vector3.Zero);
                    }
                }

            }

            var preIntegrateSpot = motion.Body.Position;
            if (motion.Host is { } npcHub)
            {
                MotionDeltaPose pmDiff =
                    motion.LocusKeeperDiffTemp;
                pmDiff.Origin = scaledTrunkLocomotionOwnOrigin;
                pmDiff.Orientation = trunkLocomotionOwnCycle.Orientation;
                float upperPaceNpc = motion.Motion.FetchAdjustedUpperPace();
                motion.Position.ConstructShift(
                    dt,
                    motion.Body.Position,
                    motion.Body.Orientation,
                    pmDiff,
                    motion.Lerp,
                    upperPaceNpc,
                    pmDiff,
                    inLink: motion.Body.InContact,
                    isSticky: (motion.Host?.LocusKeeper.FetchStickyObjectIdent() ?? 0u) != 0u);
                npcHub.LocusKeeper.AdjustOffset(pmDiff, dt);
                motion.Body.IsFullyConstrained = npcHub.LocusKeeper.IsFullyConstrained();
                FoldKeeperDiff(motion.Body, pmDiff);
            }
            else
            {
                MotionDeltaPose pmDiff =
                    motion.LocusKeeperDiffTemp;
                pmDiff.Origin = scaledTrunkLocomotionOwnOrigin;
                pmDiff.Orientation = trunkLocomotionOwnCycle.Orientation;
                float upperPaceNpc = motion.Motion.FetchAdjustedUpperPace();
                motion.Position.ConstructShift(
                    dt,
                    motion.Body.Position,
                    motion.Body.Orientation,
                    pmDiff,
                    motion.Lerp,
                    upperPaceNpc,
                    pmDiff,
                    inLink: motion.Body.InContact,
                    isSticky: (motion.Host?.LocusKeeper.FetchStickyObjectIdent() ?? 0u) != 0u);
                FoldKeeperDiff(motion.Body, pmDiff);
            }
            motion.Body.calc_acceleration();
            motion.Body.RefreshKineticsInternal(dt);
            if (scheduler is { } tapScheduler)
                procAnimTaps?.Invoke(ownActorIdent, tapScheduler);
            if (!OwnsCore(
                    capture,
                    motion,
                    objectTimerEpoch,
                    externalHolderValid))
            {
                return false;
            }
            var postIntegrateSpot = motion.Body.Position;
            uint sealedChamberIdent = motion.CellId;

            if (motion.CellId != 0 && _register.Engine.LandblockTally > 0)
            {
                float deR = radius;
                float deH = height;
                if (deR < 0.05f) { deR = 0.48f; deH = 1.835f; }
                bool earlierLink = motion.Body.InContact;
                bool earlierOnPassable = motion.Body.OnWalkable;
                var locateOutcome = _register.Engine.ResolveWithTransition(
                    preIntegrateSpot, postIntegrateSpot, motion.CellId,
                    orbRadius: deR,
                    orbHeight: deH,
                    hopUpHeight: hopUpHeight,
                    hopDownHeight: hopDownHeight,
                    orbRoster: orbRoster,
                    orbScaling: orbScaling,
                    isOnTerrain: earlierOnPassable,
                    corpus: motion.Body,
                    carrierFlagSet: (IsAvatarOid(srvOid)
                        ? MoverState.IsPlayer
                          | MoverState.EdgeSlide
                        : MoverState.EdgeSlide)
                        | carrierPvpPhase,
                    movingActorIdent: ownActorIdent);

                sealedChamberIdent = SealSweepVerdict(
                    motion.Body, locateOutcome, preIntegrateSpot, dt, sealedChamberIdent);

                bool contenderMoved = postIntegrateSpot != preIntegrateSpot;
                if (locateOutcome.Ok && contenderMoved)
                {
                    bool finalOnPassable = KineticObjUpdate
                        .SealSetLocusLinkStem(
                            motion.Body,
                            locateOutcome.InContact,
                            locateOutcome.OnWalkable,
                            earlierOnPassable);

                    if (!earlierOnPassable && finalOnPassable)
                    {
                        motion.Movement.HitGround();

                        if (!OwnsCore(
                                capture,
                                motion,
                                objectTimerEpoch,
                                externalHolderValid))
                        {
                            return false;
                        }

                        motion.Lerp.Clear();
                        if (Environment.GetEnvironmentVariable("MACAC_DUMP_MOTION") == "1")
                            Console.WriteLine($"VU.land guid=0x{srvOid:X8} Z={motion.Body.Position.Z:F2}");
                    }
                    else if (earlierOnPassable && !finalOnPassable)
                    {
                        motion.Motion.LeaveGround();
                        if (!OwnsCore(
                                capture,
                                motion,
                                objectTimerEpoch,
                                externalHolderValid))
                        {
                            return false;
                        }
                    }

                    KineticObjUpdate
                        .SealSetLocusPostTerrain(motion.Body);

                    KineticObjUpdate.HandleAllCollisions(
                        motion.Body,
                        locateOutcome.CollisionNormalValid,
                        locateOutcome.CollisionNormal,
                        earlierLink,
                        earlierOnPassable,
                        motion.Body.OnWalkable);

                    motion.Airborne = !motion.Body.OnWalkable;
                }

                {
                    bool slideCorpusCpValid = motion.Body.ContactPlaneValid;
                    float slideCorpusCpNz = motion.Body.ContactPlane.Normal.Z;
                    int slideSignature =
                          (motion.Airborne ? 1 << 0 : 0)
                        | (slideForcedLink ? 1 << 1 : 0)
                        | (slideForcedPassable ? 1 << 2 : 0)
                        | (locateOutcome.InContact ? 1 << 3 : 0)
                        | (locateOutcome.OnWalkable ? 1 << 4 : 0)
                        | (locateOutcome.IsOnGround ? 1 << 5 : 0)
                        | (motion.Body.InContact ? 1 << 6 : 0)
                        | (motion.Body.OnWalkable ? 1 << 7 : 0)
                        | (motion.Body.HasGravity ? 1 << 8 : 0)
                        | (slideCorpusCpValid ? 1 << 9 : 0)
                        | (slideCorpusCpValid
                           && slideCorpusCpNz
                              < KineticConstants.FloorZ
                                                          ? 1 << 10 : 0)
                        | (Vector3.Distance(
                               preIntegrateSpot, motion.Body.Position) > 0.01f
                                                          ? 1 << 11 : 0);
                    if (KineticTelemetry
                            .ShouldEmitDistantSlideBeat(srvOid, slideSignature))
                    {
                        KineticTelemetry.TraceDistantSlideBeat(
                            oid: srvOid,
                            airborne: motion.Airborne,
                            forcedLink: slideForcedLink,
                            forcedPassable: slideForcedPassable,
                            velPriorZero: slideVelPriorZero,
                            settled: true,
                            locateInLink: locateOutcome.InContact,
                            locateOnPassable: locateOutcome.OnWalkable,
                            locateIsOnTerrain: locateOutcome.IsOnGround,
                            locateLinkPlaneValid: locateOutcome.InContact,
                            locateLinkPlaneNormZ:
                                locateOutcome.ContactPlane.Normal.Z,
                            corpusLinkPlaneValid: slideCorpusCpValid,
                            corpusLinkPlaneNormZ: slideCorpusCpNz,
                            link: motion.Body.InContact,
                            onPassable: motion.Body.OnWalkable,
                            gravity: motion.Body.HasGravity,
                            vel: motion.Body.Velocity,
                            acceleration: motion.Body.Acceleration,
                            preIntegrateLocus: preIntegrateSpot,
                            postIntegrateLocus: postIntegrateSpot,
                            settledLocus: motion.Body.Position);
                    }
                }

            }
            else
            {
                bool skipCorpusCpValid = motion.Body.ContactPlaneValid;
                float skipCorpusCpNz = motion.Body.ContactPlane.Normal.Z;
                int skipSignature =
                      (motion.Airborne ? 1 << 0 : 0)
                    | (slideForcedLink ? 1 << 1 : 0)
                    | (slideForcedPassable ? 1 << 2 : 0)
                    | (motion.Body.InContact ? 1 << 6 : 0)
                    | (motion.Body.OnWalkable ? 1 << 7 : 0)
                    | (motion.Body.HasGravity ? 1 << 8 : 0)
                    | (skipCorpusCpValid ? 1 << 9 : 0)
                    | (1 << 12);
                if (KineticTelemetry
                        .ShouldEmitDistantSlideBeat(srvOid, skipSignature))
                {
                    KineticTelemetry.TraceDistantSlideBeat(
                        oid: srvOid,
                        airborne: motion.Airborne,
                        forcedLink: slideForcedLink,
                        forcedPassable: slideForcedPassable,
                        velPriorZero: slideVelPriorZero,
                        settled: false,
                        locateInLink: false,
                        locateOnPassable: false,
                        locateIsOnTerrain: false,
                        locateLinkPlaneValid: false,
                        locateLinkPlaneNormZ: 0f,
                        corpusLinkPlaneValid: skipCorpusCpValid,
                        corpusLinkPlaneNormZ: skipCorpusCpNz,
                        link: motion.Body.InContact,
                        onPassable: motion.Body.OnWalkable,
                        gravity: motion.Body.HasGravity,
                        vel: motion.Body.Velocity,
                        acceleration: motion.Body.Acceleration,
                        preIntegrateLocus: preIntegrateSpot,
                        postIntegrateLocus: postIntegrateSpot,
                        settledLocus: motion.Body.Position);
                }
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
            {
                return false;
            }
            bool chamberAltered = sealedChamberIdent != 0
                && sealedChamberIdent != motion.CellId;
            if (chamberAltered)
                motion.CellId = sealedChamberIdent;
            if (!OwnsCore(
                    capture,
                    motion,
                    objectTimerEpoch,
                    externalHolderValid))
            {
                return false;
            }

            if (ShouldSynchronizeShade(
                    chamberAltered,
                    motion.Body.Position,
                    motion.Body.Orientation,
                    motion.PreviousShadeSynchronizeSpot,
                    motion.LastShadowSyncOrientation))
            {
                SynchronizeDistantShadeToCorpus(
                    ownActorIdent,
                    motion,
                    onlineMiddleX,
                    onlineMiddleY);
            }
        }

        CanonObjectKeeperTail.Run(
            motion.Host?.MarkKeeper,
            motion.Movement,
            scheduler?.Manager,
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

    // Applies a sweep outcome to the body
    internal static uint SealSweepVerdict(
        KineticBody corpus,
        in ResolveVerdict outcome,
        Vector3 preIntegrateSpot,
        float dt,
        uint sealedChamberIdent)
    {
        if (outcome.Ok)
        {
            corpus.Position = outcome.Position;
            if (outcome.CellId != 0)
                sealedChamberIdent = outcome.CellId;
            corpus.StashedVel = dt > 0f
                ? (outcome.Position - preIntegrateSpot) / dt
                : Vector3.Zero;
        }
        else
        {
            corpus.Position = preIntegrateSpot;
            corpus.StashedVel = Vector3.Zero;
        }
        return sealedChamberIdent;
    }

    private bool OwnsCore(
        SimActorRecord capture,
        PeerMotion distant,
        ulong objectTimerEpoch,
        System.Func<bool>? externalHolderValid) =>
        _register.IsSpatialDistant(capture, distant)
        && capture.ObjectTimerEpoch == objectTimerEpoch
        && ReferenceEquals(capture.KineticBody, distant.Body)
        && (externalHolderValid?.Invoke() ?? true);

    private static void FoldKeeperDiff(
        KineticBody corpus,
        MotionDeltaPose diff)
    {
        if (diff.Origin != Vector3.Zero)
            corpus.Position += Vector3.Transform(diff.Origin, corpus.Orientation);
        if (!diff.Orientation.IsIdentity)
            corpus.Orientation = PoseOps.AssignSpin(
                corpus.Position,
                corpus.Orientation,
                corpus.Orientation * diff.Orientation);
    }
}
