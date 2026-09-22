using MacAC.Client.Realm;
using MacAC.Sim.Actors;
using MacAC.Sim.Presence;

namespace MacAC.Client.Kinetics;

internal sealed partial class OnlineActorNetworkRefreshDriver
{
    public void ImposeSameGen(
        SameEpochCreateObjectEvents renew) =>
        OnlineActorSameEpochPulseRouter.Apply(renew, this);

    internal static RemoteContactRouting ImposeDistantLinkRouting(
        SimPeerPlacementPilot stanceSteer,
        SimActorRecord canon,
        PeerMotion distant,
        SimSovereignPositionRoute? course,
        System.Numerics.Vector3 realmSpot,
        System.Numerics.Quaternion spin,
        bool willBeDrTicked,
        Func<bool> execWarpTap)
    {
        ArgumentNullException.ThrowIfNull(stanceSteer);
        ArgumentNullException.ThrowIfNull(canon);
        ArgumentNullException.ThrowIfNull(distant);
        ArgumentNullException.ThrowIfNull(execWarpTap);

        if (SimPeerWarpPosition.OwnsTeleportPlacement(course))
        {
            bool tapRan = execWarpTap();
            var warpCondition =
                stanceSteer.ImposeApprovedDistantWarp(
                    canon,
                    distant,
                    course!.Value);
            if (MacAC.Mechanics.Kinetics.KineticTelemetry.ProbeRemoteTeleportEnabled)
            {
                MacAC.Mechanics.Kinetics.KineticTelemetry.TraceDistantWarp(
                    canon.ServerGuid,
                    cause: course.Value.Authority.WarpAdvanced
                        ? "teleport-ts"
                        : "cellless",
                    tapRan,
                    warpCondition.ToString());
            }
            return new RemoteContactRouting(
                RemoteContactArm.TeleportPlacement,
                warpCondition);
        }

        MacAC.Mechanics.Kinetics.KineticTelemetry.CommenceDistantSlideAttribution(
            canon.ServerGuid);
        if (!distant.Body.InContact)
        {
            distant.Body.Position = realmSpot;
            distant.Body.Orientation = spin;
            return new RemoteContactRouting(
                RemoteContactArm.AirborneSnap, Placement: null);
        }

        return SimPeerFarSnapPosition.ResolveArm(course) switch
        {
            SimPeerGrantedPositionArm.FarSnapPlacement => new RemoteContactRouting(
                                RemoteContactArm.FarSnapPlacement,
                                stanceSteer.ImposeApprovedDistantFarawaySnap(
                                    canon,
                                    distant,
                                    course!.Value)),
            SimPeerGrantedPositionArm.NearInterpolate => new RemoteContactRouting(
                                RemoteContactArm.SteadyStateInterpolate,
                                Placement: null,
                                Interpolation: SimPeerSettledStatePosition.ImposeLerp(
                                    distant,
                                    realmSpot,
                                    spin,
                                    isMovingTo: distant.Movement.IsMovingTo(),
                                    willBeDrTicked,
                                    (canon.Snapshot.Physics?.Position ?? canon.Snapshot.Position)?.LandblockId ?? 0u)),
            SimPeerGrantedPositionArm.AirborneNoOperation => throw new InvalidOperationException(
                                "A NoPositionOperation (airborne no-op) classification "
                                + "has to be handled by the caller's own early return "
                                + "prior to routing; MoveOrTeleport writes nothing "
                                + "at all on that branch"),
            _ => new RemoteContactRouting(
                                RemoteContactArm.UnroutedCatchUp,
                                Placement: null,
                                Interpolation: SimPeerSettledStatePosition.ImposeLerp(
                                    distant,
                                    realmSpot,
                                    spin,
                                    isMovingTo: distant.Movement.IsMovingTo(),
                                    willBeDrTicked,
                                    (canon.Snapshot.Physics?.Position ?? canon.Snapshot.Position)?.LandblockId ?? 0u)),
        };
    }

    private static void ImposeWireAirborneLeftoverBookkeeping(
        PeerMotion distant,
        uint wireChamberIdent,
        System.Numerics.Vector3 realmSpot,
        double instantSec)
    {
        ArgumentNullException.ThrowIfNull(distant);
        distant.CellId = wireChamberIdent;
        distant.PreviousSrvSpot = realmSpot;
        distant.PreviousSrvSpotMoment = instantSec;
    }

    private void ImposePlainVector(
        MacAC.Wire.Messages.VelocityUpdate.Parsed refresh,
        OnlineActorRecord approvedVectorCapture,
        ulong approvedVectorArbiterVer,
        ulong approvedVectorVelArbiterVer)
    {
        if (!_onlineActors.ContainsRealmActor(refresh.Guid)) return;

        if (refresh.Guid == _avatarSrvOid) return;          // local jump uses our own physics
        if (!_onlineActors.TryFetchDistantLocomotionCore(
                refresh.Guid,
                out ISimPeerMotion? distantCore)
            || distantCore is not PeerMotion motion)

            return;
        var distantCapture = approvedVectorCapture;

        if (!_onlineActors.TryCommitAuthoritativeVector(
                distantCapture,
                motion.Body,
                refresh.Velocity,
                refresh.Omega,
                _physicsScriptGameTime))

            return;

        if (MacAC.Mechanics.Kinetics.KineticTelemetry.ShouldTraceDistantSlide(
                refresh.Guid))
        {
            MacAC.Mechanics.Kinetics.KineticTelemetry.TraceDistantSlideVector(
                oid: refresh.Guid,
                wireVel: refresh.Velocity,
                wireOmega: refresh.Omega,
                willFlagAirborne: refresh.Velocity.Z > 0.5f,
                airbornePrior: motion.Airborne,
                link: motion.Body.InContact,
                onPassable: motion.Body.OnWalkable,
                gravity: motion.Body.HasGravity,
                corpusVel: motion.Body.Velocity,
                linkPlaneValid: motion.Body.ContactPlaneValid,
                linkPlaneNormZ: motion.Body.ContactPlane.Normal.Z);
        }

        if (refresh.Velocity.Z > 0.5f)
        {
            motion.Airborne = true;
            motion.Body.TransientState &= ~(MacAC.Mechanics.Kinetics.TransientPhaseFlagSet.Contact
                                      | MacAC.Mechanics.Kinetics.TransientPhaseFlagSet.OnWalkable);

            if (_onlineActors.TryFetchRealmActor(refresh.Guid, out var ent)
                && _animatedEntities.TryGetValue(ent.Id, out var ledger)
                && ledger.Sequencer is not null)
            {
                _locomotionCore.EnsureRemoteMotionBindings(motion, ledger, refresh.Guid);
                motion.Motion.LeaveGround();
                if (!_onlineActors.IsLatestVectorArbiter(
                        distantCapture,
                        approvedVectorArbiterVer)
                    || !_onlineActors.IsLatestVelArbiter(
                        distantCapture,
                        approvedVectorVelArbiterVer)
                    || !_onlineActors.TryCommitAuthoritativeVector(
                        distantCapture,
                        motion.Body,
                        refresh.Velocity,
                        refresh.Omega,
                        _physicsScriptGameTime))

                    return;
            }
        }

        if (Environment.GetEnvironmentVariable("MACAC_DUMP_MOTION") == "1")
        {
            Console.WriteLine(
                $"VU    guid=0x{refresh.Guid:X8} vel=({refresh.Velocity.X:F2},{refresh.Velocity.Y:F2},{refresh.Velocity.Z:F2}) airborne={motion.Airborne}");
        }
    }
}
