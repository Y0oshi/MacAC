using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

public sealed partial class InboundKineticsStateDriver
{
    public bool TryEnactObjDesc(
        ObjDescNotice.Parsed refresh,
        out RealmSession.MoverSpawn approved)
    {
        if (!Consult(refresh.Guid, out KineticStampGate? latch, out RealmSession.MoverSpawn former)
            || !latch.TryAdmitObjRefDscSignal(refresh.InstanceSequence, refresh.ObjDescSequence))
        {
            approved = default;
            return false;
        }

        approved = ImposeApprovedObjRefDsc(former, refresh);
        _currentByOid[refresh.Guid] = approved;
        return true;
    }

    public bool TryEnactPickup(
        PickupNotice.Parsed refresh,
        out RealmSession.MoverSpawn approved)
    {
        if (!Consult(refresh.Guid, out KineticStampGate? latch, out RealmSession.MoverSpawn former)
            || !latch.TryAdmitLocusLaneSignal(
                refresh.InstanceSequence, refresh.PositionSequence))
        {
            approved = default;
            return false;
        }

        approved = ImposeApprovedLift(former, refresh);
        _currentByOid[refresh.Guid] = approved;
        return true;
    }

    public bool TryEnactCreateParent(
        CreateAnchorUpdate refresh,
        out RealmSession.MoverSpawn approved)
    {
        if (!Consult(refresh.ChildGuid, out KineticStampGate? descendantLatch, out RealmSession.MoverSpawn descendant)
            || !descendantLatch.TryAdmitLocusLaneSignal(
                refresh.ChildInstanceSequence, refresh.ChildPositionSequence))
        {
            approved = default;
            return false;
        }

        approved = ImposeApprovedBuildAncestor(descendant, refresh);
        _currentByOid[refresh.ChildGuid] = approved;
        return true;
    }

    public bool TryEnactParent(
        AncestorSignal.Parsed refresh,
        out RealmSession.MoverSpawn approved)
    {
        if (!_stampLatches.TryGetValue(refresh.ParentGuid, out KineticStampGate? ancestorLatch)
            || !ancestorLatch.IsLatestInst(refresh.ParentInstanceSequence)
            || !Consult(refresh.ChildGuid, out KineticStampGate? descendantLatch, out RealmSession.MoverSpawn descendant)
            || !descendantLatch.TryAdmitLocusLaneSignal(
                descendantLatch.InstStamp, refresh.ChildPositionSequence))
        {
            approved = default;
            return false;
        }

        approved = ImposeApprovedAncestor(descendant, refresh);
        _currentByOid[refresh.ChildGuid] = approved;
        return true;
    }

    public bool TrySealAncestor(
        uint descendantOid,
        uint ancestorOid,
        uint ancestorLocale,
        uint stanceIdent,
        ushort locusSeries,
        out RealmSession.MoverSpawn approved)
    {
        if (!Consult(descendantOid, out KineticStampGate? latch, out RealmSession.MoverSpawn descendant)
            || latch.LocusStamp != locusSeries
            || descendant.PositionSequence != locusSeries)
        {
            approved = default;
            return false;
        }

        approved = Reparent(
            descendant,
            ancestorOid,
            ancestorLocale,
            stanceIdent,
            locusSeries);
        _currentByOid[descendantOid] = approved;
        return true;
    }

    public bool TryEnactMotion(
        RealmSession.MoverMotionUpdate refresh,
        bool retainCargo,
        out RealmSession.MoverSpawn approved,
        out GrantedKineticsTimestamps timestamps)
    {
        if (!Consult(refresh.Guid, out KineticStampGate? latch, out RealmSession.MoverSpawn former))
        {
            approved = default;
            timestamps = default;
            return false;
        }

        bool enactCargo = latch.TryAdmitTravelSignal(
            refresh.InstanceSequence,
            refresh.MovementSequence,
            refresh.ServerControlSequence);
        timestamps = GrantedStamps(latch);

        var stamped = ImposeApprovedLocomotion(
            former,
            latch.TravelStamp,
            latch.SrvControlledRelocateStamp,
            refresh,
            retainCargo: false);
        _currentByOid[refresh.Guid] = stamped;

        if (!enactCargo)
        {
            approved = default;
            return false;
        }

        if (!retainCargo)
        {
            approved = stamped;
            return true;
        }

        approved = ImposeApprovedLocomotion(
            stamped,
            latch.TravelStamp,
            latch.SrvControlledRelocateStamp,
            refresh,
            retainCargo: true);
        _currentByOid[refresh.Guid] = approved;
        return true;
    }

    public bool TryApplyVector(
        VelocityUpdate.Parsed refresh,
        out RealmSession.MoverSpawn approved)
    {
        if (!Consult(refresh.Guid, out KineticStampGate? latch, out RealmSession.MoverSpawn former)
            || !latch.TryAdmitVectorSignal(refresh.InstanceSequence, refresh.VectorSequence))
        {
            approved = default;
            return false;
        }

        approved = ImposeApprovedVector(former, refresh);
        _currentByOid[refresh.Guid] = approved;
        return true;
    }

    public bool TryEnactPhase(
        GroupPhase.Parsed refresh,
        out RealmSession.MoverSpawn approved)
    {
        if (!Consult(refresh.Guid, out KineticStampGate? latch, out RealmSession.MoverSpawn former)
            || !latch.TryAdmitPhaseSignal(refresh.InstanceSequence, refresh.StateSequence))
        {
            approved = default;
            return false;
        }

        approved = ImposeApprovedPhase(former, refresh);
        _currentByOid[refresh.Guid] = approved;
        return true;
    }

    public bool TryEnactPlace(
        RealmSession.MoverPositionUpdate refresh,
        bool isOwnAvatar,
        System.Numerics.Quaternion? forceLocusSpin,
        System.Numerics.Vector3? latestOwnVel,
        out PoseStampVerdict disposition,
        out RealmSession.MoverSpawn approved,
        out GrantedKineticsTimestamps timestamps)
    {
        if (!Consult(refresh.Guid, out KineticStampGate? latch, out RealmSession.MoverSpawn former))
        {
            disposition = PoseStampVerdict.Rejected;
            approved = default;
            timestamps = default;
            return false;
        }

        ushort earlierWarp = latch.WarpStamp;
        bool advancesWarp = KineticStampGate.IsNewer(
            earlierWarp,
            refresh.TeleportSequence);
        disposition = latch.TryAdmitLocusSignal(
            refresh.InstanceSequence,
            refresh.PositionSequence,
            refresh.TeleportSequence,
            refresh.ForcePositionSequence,
            isOwnAvatar);
        timestamps = GrantedStamps(
            latch,
            warpAdvanced: disposition is PoseStampVerdict.Apply
                && advancesWarp,
            earlierWarp: earlierWarp);
        bool hasAnims =
            (former.MotionTableId ?? former.Physics?.MotionTableId)
                is { } locomotionChartIdent
            && locomotionChartIdent is not 0u;
        var preStance =
            SimSovereignPositionRouteSorter.DerivePreStanceFlagSet(
                disposition,
                hasAnims);
        approved = ImposeApprovedLocus(
            former,
            refresh,
            disposition,
            timestamps,
            isOwnAvatar,
            forceLocusSpin,
            latestOwnVel,
            installStanceCycle: preStance.ApplyPlacementFrameBeforeRouting,
            wipeAncestor: preStance.UnparentBeforeRouting);
        _currentByOid[refresh.Guid] = approved;
        return true;
    }

    internal bool ImposeApprovedObjRefDscCapture(
        uint oid,
        ObjDescNotice.Parsed refresh,
        out RealmSession.MoverSpawn approved)
    {
        if (!_currentByOid.TryGetValue(oid, out RealmSession.MoverSpawn former))
        {
            approved = default;
            return false;
        }
        approved = ImposeApprovedObjRefDsc(former, refresh);
        _currentByOid[oid] = approved;
        return true;
    }

    internal static RealmSession.MoverSpawn ImposeApprovedObjRefDsc(
        RealmSession.MoverSpawn former,
        ObjDescNotice.Parsed refresh)
    {
        var kinetics = former.Physics;
        if (kinetics is { } descriptor)
            kinetics = descriptor with
            {
                Timestamps = descriptor.Timestamps with { ObjDesc = refresh.ObjDescSequence },
            };

        return former with
        {
            AnimPartChanges = refresh.ModelData.AnimPartChanges,
            TextureChanges = refresh.ModelData.TextureChanges,
            SubPalettes = refresh.ModelData.SubPalettes,
            BasePaletteId = refresh.ModelData.BasePaletteId,
            Physics = kinetics,
        };
    }

    internal static RealmSession.MoverSpawn ImposeApprovedLift(
        RealmSession.MoverSpawn former,
        PickupNotice.Parsed refresh) =>
        PutUnparented(former, null, refresh.PositionSequence);

    internal bool ImposeApprovedLiftCapture(
        uint oid,
        PickupNotice.Parsed refresh,
        out RealmSession.MoverSpawn approved)
    {
        if (!_currentByOid.TryGetValue(oid, out RealmSession.MoverSpawn former))
        {
            approved = default;
            return false;
        }
        approved = ImposeApprovedLift(former, refresh);
        _currentByOid[oid] = approved;
        return true;
    }

    internal static RealmSession.MoverSpawn ImposeApprovedBuildAncestor(
        RealmSession.MoverSpawn descendant,
        CreateAnchorUpdate refresh) =>
        StampLocusSole(descendant, refresh.ChildPositionSequence);

    internal bool ImposeApprovedBuildAncestorCapture(
        uint oid,
        CreateAnchorUpdate refresh,
        out RealmSession.MoverSpawn approved)
    {
        if (!_currentByOid.TryGetValue(oid, out RealmSession.MoverSpawn former))
        {
            approved = default;
            return false;
        }
        approved = ImposeApprovedBuildAncestor(former, refresh);
        _currentByOid[oid] = approved;
        return true;
    }

    internal static RealmSession.MoverSpawn ImposeApprovedAncestor(
        RealmSession.MoverSpawn descendant,
        AncestorSignal.Parsed refresh) =>
        StampLocusSole(descendant, refresh.ChildPositionSequence);

    internal bool ImposeApprovedAncestorCapture(
        uint oid,
        AncestorSignal.Parsed refresh,
        out RealmSession.MoverSpawn approved)
    {
        if (!_currentByOid.TryGetValue(oid, out RealmSession.MoverSpawn former))
        {
            approved = default;
            return false;
        }
        approved = ImposeApprovedAncestor(former, refresh);
        _currentByOid[oid] = approved;
        return true;
    }

    internal static RealmSession.MoverSpawn ImposeApprovedLocomotion(
        RealmSession.MoverSpawn former,
        ushort travelSeries,
        ushort approvedSrvControlledRelocate,
        RealmSession.MoverMotionUpdate refresh,
        bool retainCargo)
    {
        var stampedKinetics = former.Physics;
        if (stampedKinetics is { } stampedDsc)
            stampedKinetics = stampedDsc with
            {
                Timestamps = stampedDsc.Timestamps with
                {
                    Movement = travelSeries,
                    ServerControlledMove = approvedSrvControlledRelocate,
                },
            };
        var stamped = former with
        {
            MovementSequence = travelSeries,
            ServerControlSequence = approvedSrvControlledRelocate,
            Physics = stampedKinetics,
        };
        if (!retainCargo)
            return stamped;

        var kinetics = stamped.Physics;
        if (kinetics is { } descriptor)
            kinetics = descriptor with
            {
                Movement = new KineticMovementData(
                    ReadOnlyMemory<byte>.Empty,
                    refresh.MotionState,
                    refresh.IsAutonomous),
            };

        return stamped with
        {
            MotionState = refresh.MotionState,
            Physics = kinetics,
        };
    }

    internal bool ImposeApprovedLocomotionCapture(
        uint oid,
        ushort travelSeries,
        ushort approvedSrvControlledRelocate,
        RealmSession.MoverMotionUpdate refresh,
        bool retainCargo,
        out RealmSession.MoverSpawn approved)
    {
        if (!_currentByOid.TryGetValue(oid, out RealmSession.MoverSpawn former))
        {
            approved = default;
            return false;
        }
        approved = ImposeApprovedLocomotion(
            former,
            travelSeries,
            approvedSrvControlledRelocate,
            refresh,
            retainCargo);
        _currentByOid[oid] = approved;
        return true;
    }

    internal static RealmSession.MoverSpawn ImposeApprovedVector(
        RealmSession.MoverSpawn former,
        VelocityUpdate.Parsed refresh)
    {
        var kinetics = former.Physics;
        if (kinetics is { } descriptor)
            kinetics = descriptor with
            {
                Velocity = refresh.Velocity,
                AngularVelocity = refresh.Omega,
                Timestamps = descriptor.Timestamps with { Vector = refresh.VectorSequence },
            };

        return former with { Physics = kinetics };
    }

    internal bool ImposeApprovedVectorCapture(
        uint oid,
        VelocityUpdate.Parsed refresh,
        out RealmSession.MoverSpawn approved)
    {
        if (!_currentByOid.TryGetValue(oid, out RealmSession.MoverSpawn former))
        {
            approved = default;
            return false;
        }
        approved = ImposeApprovedVector(former, refresh);
        _currentByOid[oid] = approved;
        return true;
    }

    internal static RealmSession.MoverSpawn ImposeApprovedPhase(
        RealmSession.MoverSpawn former,
        GroupPhase.Parsed refresh)
    {
        var kinetics = former.Physics;
        if (kinetics is { } descriptor)
            kinetics = descriptor with
            {
                RawState = refresh.PhysicsState,
                Timestamps = descriptor.Timestamps with { State = refresh.StateSequence },
            };

        return former with
        {
            PhysicsState = refresh.PhysicsState,
            Physics = kinetics,
        };
    }

    internal bool ImposeApprovedPhaseCapture(
        uint oid,
        GroupPhase.Parsed refresh,
        out RealmSession.MoverSpawn approved)
    {
        if (!_currentByOid.TryGetValue(oid, out RealmSession.MoverSpawn former))
        {
            approved = default;
            return false;
        }
        approved = ImposeApprovedPhase(former, refresh);
        _currentByOid[oid] = approved;
        return true;
    }

    internal bool ImposeApprovedLocusCapture(
        uint oid,
        RealmSession.MoverPositionUpdate refresh,
        PoseStampVerdict disposition,
        GrantedKineticsTimestamps timestamps,
        bool isOwnAvatar,
        System.Numerics.Quaternion? forceLocusSpin,
        System.Numerics.Vector3? latestOwnVel,
        bool installStanceCycle,
        bool wipeAncestor,
        out RealmSession.MoverSpawn approved)
    {
        if (!_currentByOid.TryGetValue(oid, out RealmSession.MoverSpawn former))
        {
            approved = default;
            return false;
        }
        approved = ImposeApprovedLocus(
            former,
            refresh,
            disposition,
            timestamps,
            isOwnAvatar,
            forceLocusSpin,
            latestOwnVel,
            installStanceCycle,
            wipeAncestor);
        _currentByOid[oid] = approved;
        return true;
    }

    internal bool ImposeApprovedLocusExecutionRejectedCapture(
        uint oid,
        ushort approvedLocusSeries,
        GrantedKineticsTimestamps timestamps,
        out RealmSession.MoverSpawn approved)
    {
        if (!_currentByOid.TryGetValue(oid, out RealmSession.MoverSpawn former))
        {
            approved = default;
            return false;
        }
        var kinetics = former.Physics;
        if (kinetics is { } descriptor)
            kinetics = descriptor with
            {
                Timestamps = descriptor.Timestamps with
                {
                    Position = approvedLocusSeries,
                    Teleport = timestamps.Teleport,
                    ForcePosition = timestamps.ForcePosition,
                },
            };
        approved = former with
        {
            PositionSequence = approvedLocusSeries,
            Physics = kinetics,
        };
        _currentByOid[oid] = approved;
        return true;
    }

    internal static RealmSession.MoverSpawn ImposeApprovedLocus(
        RealmSession.MoverSpawn former,
        RealmSession.MoverPositionUpdate refresh,
        PoseStampVerdict disposition,
        GrantedKineticsTimestamps timestamps,
        bool isOwnAvatar,
        System.Numerics.Quaternion? forceLocusSpin,
        System.Numerics.Vector3? latestOwnVel,
        bool installStanceCycle,
        bool wipeAncestor)
    {
        if (disposition is PoseStampVerdict.Rejected)
            return StampGrantedLocusSole(former, timestamps);

        var imposedLocus = refresh.Position;
        if (disposition is PoseStampVerdict.ForcePosition
            && forceLocusSpin is { } preserved)
        {
            imposedLocus = refresh.Position with
            {
                RotationW = preserved.W,
                RotationX = preserved.X,
                RotationY = preserved.Y,
                RotationZ = preserved.Z,
            };
        }

        uint? imposedStance = installStanceCycle
            ? (disposition is PoseStampVerdict.Apply
                ? refresh.PlacementId ?? 0u
                : former.PlacementId)
            : former.PlacementId;

        System.Numerics.Vector3? imposedVel = disposition switch
        {
            PoseStampVerdict.ForcePosition =>
                latestOwnVel ?? former.Physics?.Velocity,

            PoseStampVerdict.Apply when isOwnAvatar =>
                timestamps.TeleportAdvanced
                    ? System.Numerics.Vector3.Zero
                    : latestOwnVel ?? former.Physics?.Velocity,

            PoseStampVerdict.Apply =>
                refresh.Velocity ?? System.Numerics.Vector3.Zero,

            _ => former.Physics?.Velocity,
        };
        uint? ancestorOid = wipeAncestor ? null : former.ParentGuid;
        uint? ancestorLocale = wipeAncestor ? null : former.ParentLocation;
        KineticAttachment? kineticsAncestor = wipeAncestor ? null : former.Physics?.Parent;
        var kinetics = former.Physics;
        if (kinetics is { } descriptor)
            kinetics = descriptor with
            {
                Position = imposedLocus,
                AnimationFrame = imposedStance,
                Velocity = imposedVel,
                Parent = kineticsAncestor,
                Timestamps = descriptor.Timestamps with
                {
                    Position = refresh.PositionSequence,
                    Teleport = timestamps.Teleport,
                    ForcePosition = timestamps.ForcePosition,
                },
            };

        return former with
        {
            Position = imposedLocus,
            PositionSequence = refresh.PositionSequence,
            ParentGuid = ancestorOid,
            ParentLocation = ancestorLocale,
            PlacementId = imposedStance,
            Physics = kinetics,
        };
    }

    private static RealmSession.MoverSpawn StampGrantedLocusSole(
        RealmSession.MoverSpawn former,
        GrantedKineticsTimestamps timestamps)
    {
        var kinetics = former.Physics;
        if (kinetics is { } descriptor)
            kinetics = descriptor with
            {
                Timestamps = descriptor.Timestamps with
                {
                    ForcePosition = timestamps.ForcePosition,
                },
            };
        return former with { Physics = kinetics };
    }

    private static RealmSession.MoverSpawn PutUnparented(
        RealmSession.MoverSpawn former,
        ObjectCreation.RemotePosition? locus,
        ushort locusSeries)
    {
        var kinetics = former.Physics;
        if (kinetics is { } descriptor)
            kinetics = descriptor with
            {
                Position = locus,
                Parent = null,
                Timestamps = descriptor.Timestamps with { Position = locusSeries },
            };

        return former with
        {
            Position = locus,
            ParentGuid = null,
            ParentLocation = null,
            PositionSequence = locusSeries,
            Physics = kinetics,
        };
    }

    private static RealmSession.MoverSpawn StampLocusSole(
        RealmSession.MoverSpawn former,
        ushort locusSeries)
    {
        var kinetics = former.Physics;
        if (kinetics is { } descriptor)
        {
            kinetics = descriptor with
            {
                Timestamps = descriptor.Timestamps with { Position = locusSeries },
            };
        }
        return former with
        {
            PositionSequence = locusSeries,
            Physics = kinetics,
        };
    }

    private static RealmSession.MoverSpawn Reparent(
        RealmSession.MoverSpawn former,
        uint ancestorOid,
        uint ancestorLocale,
        uint stanceIdent,
        ushort locusSeries)
    {
        var kinetics = former.Physics;
        if (kinetics is { } descriptor)
            kinetics = descriptor with
            {
                Position = null,
                Parent = new KineticAttachment(ancestorOid, ancestorLocale),
                AnimationFrame = stanceIdent,
                Timestamps = descriptor.Timestamps with { Position = locusSeries },
            };

        return former with
        {
            Position = null,
            ParentGuid = ancestorOid,
            ParentLocation = ancestorLocale,
            PlacementId = stanceIdent,
            PositionSequence = locusSeries,
            Physics = kinetics,
        };
    }
}
