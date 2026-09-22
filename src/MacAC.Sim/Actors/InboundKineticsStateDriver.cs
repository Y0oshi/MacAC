using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

public sealed partial class InboundKineticsStateDriver
{
    private readonly Dictionary<uint, KineticStampGate> _stampLatches = new();

    private readonly Dictionary<uint, RealmSession.MoverSpawn> _currentByOid = new();

    public IReadOnlyDictionary<uint, RealmSession.MoverSpawn> Snapshots => _currentByOid;

    public bool TryFetchCapture(uint oid, out RealmSession.MoverSpawn summon) =>
        _currentByOid.TryGetValue(oid, out summon);

    public SpawnStampVerdict PreviewBuildDisposition(
        RealmSession.MoverSpawn incoming)
    {
        return _stampLatches.TryGetValue(incoming.Guid, out KineticStampGate? latch)
            ? latch.PreviewBuildObject(incoming.InstanceSequence)
            : SpawnStampVerdict.InitialGeneration;
    }

    public IncomingBuildOutcome AllowBuild(RealmSession.MoverSpawn incoming) =>
        AdmitBuild(incoming, deferSameGenWeenieBlurb: false);

    public bool TryErase(ObjectDeletion.Parsed erase, bool isOwnAvatar)
    {
        if (!_stampLatches.TryGetValue(erase.Guid, out KineticStampGate? latch))
            return false;

        if (!latch.TryAdmitEraseSignal(erase.InstanceSequence, isOwnAvatar))
            return false;

        _stampLatches.Remove(erase.Guid);
        _currentByOid.Remove(erase.Guid);
        return true;
    }

    public bool IsFreshTeleportBegin(uint ownAvatarOid, ushort warpSeries)
    {
        return _stampLatches.TryGetValue(ownAvatarOid, out KineticStampGate? latch)
        && latch.IsFreshWarpStart(warpSeries);
    }

    public void Clear()
    {
        _stampLatches.Clear();
        _currentByOid.Clear();
    }

    internal bool TryFetchApprovedTimestamps(
        uint oid,
        out GrantedKineticsTimestamps timestamps)
    {
        if (_stampLatches.TryGetValue(oid, out KineticStampGate? latch))
        {
            timestamps = GrantedStamps(latch);
            return true;
        }
        timestamps = default;
        return false;
    }

    internal IncomingBuildOutcome AdmitBuildPostponedSameGen(
        RealmSession.MoverSpawn incoming) =>
        AdmitBuild(incoming, deferSameGenWeenieBlurb: true);

    internal bool ImposeApprovedWeenieBlurbCapture(
        uint oid,
        RealmSession.MoverSpawn incoming,
        out RealmSession.MoverSpawn merged)
    {
        if (!_currentByOid.TryGetValue(oid, out RealmSession.MoverSpawn kept))
        {
            merged = default;
            return false;
        }
        merged = FoldUnstampedBuild(kept, incoming);
        _currentByOid[oid] = merged;
        return true;
    }

    internal bool TryRenewObjectBlurbFlagSet(
        uint oid,
        uint bitfield,
        out RealmSession.MoverSpawn merged)
    {
        if (!_currentByOid.TryGetValue(oid, out RealmSession.MoverSpawn kept))
        {
            merged = default;
            return false;
        }
        if (kept.ObjectDescriptionFlags == bitfield)
        {
            merged = kept;
            return false;
        }
        merged = kept with { ObjectDescriptionFlags = bitfield };
        _currentByOid[oid] = merged;
        return true;
    }

    private IncomingBuildOutcome AdmitBuild(
        RealmSession.MoverSpawn incoming,
        bool deferSameGenWeenieBlurb)
    {
        if (!_stampLatches.TryGetValue(incoming.Guid, out KineticStampGate? latch))
        {
            latch = new KineticStampGate();
            _stampLatches.Add(incoming.Guid, latch);
        }

        var timestamps = StampsOf(incoming);
        var disposition = latch.SeedForBuildObject(
            timestamps.Position,
            timestamps.Movement,
            timestamps.State,
            timestamps.Vector,
            timestamps.Teleport,
            timestamps.ServerControlledMove,
            timestamps.ForcePosition,
            timestamps.ObjDesc,
            timestamps.Instance);

        if (disposition is SpawnStampVerdict.StaleGeneration)
            return new IncomingBuildOutcome(disposition, default, null, GrantedStamps(latch));

        if (disposition is not SpawnStampVerdict.ExistingGeneration
            || !_currentByOid.TryGetValue(incoming.Guid, out RealmSession.MoverSpawn kept))
        {
            _currentByOid[incoming.Guid] = incoming;
            return new IncomingBuildOutcome(disposition, incoming, null, GrantedStamps(latch));
        }

        RealmSession.MoverSpawn merged = deferSameGenWeenieBlurb
            ? kept
            : FoldUnstampedBuild(kept, incoming);
        if (!deferSameGenWeenieBlurb)
            _currentByOid[incoming.Guid] = merged;
        return new IncomingBuildOutcome(
            disposition,
            merged,
            incoming.Physics is null ? null : SameEpochSignalsFor(incoming),
            GrantedStamps(latch));
    }

    private bool Consult(
        uint oid,
        out KineticStampGate latch,
        out RealmSession.MoverSpawn summon)
    {
        if (_stampLatches.TryGetValue(oid, out latch!)
            && _currentByOid.TryGetValue(oid, out summon))
            return true;

        latch = null!;
        summon = default;
        return false;
    }

    private static KineticStamps StampsOf(RealmSession.MoverSpawn summon)
    {
        return summon.Physics?.Timestamps
        ?? new KineticStamps(
            summon.PositionSequence,
            summon.MovementSequence,
            0,
            0,
            0,
            summon.ServerControlSequence,
            0,
            0,
            summon.InstanceSequence);
    }

    private static GrantedKineticsTimestamps GrantedStamps(
        KineticStampGate latch,
        bool warpAdvanced = false,
        ushort? earlierWarp = null)
    {
        return new(
        latch.InstStamp,
        latch.SrvControlledRelocateStamp,
        latch.WarpStamp,
        latch.ForceLocusStamp,
        warpAdvanced,
        PreviousTeleport: earlierWarp ?? latch.WarpStamp);
    }

    private static RealmSession.MoverSpawn FoldUnstampedBuild(
        RealmSession.MoverSpawn kept,
        RealmSession.MoverSpawn incoming)
    {
        return incoming with
        {
            Position = kept.Position,
            SetupTableId = kept.SetupTableId,
            AnimPartChanges = kept.AnimPartChanges,
            TextureChanges = kept.TextureChanges,
            SubPalettes = kept.SubPalettes,
            BasePaletteId = kept.BasePaletteId,
            ObjScale = kept.ObjScale,
            MotionState = kept.MotionState,
            MotionTableId = kept.MotionTableId,
            PhysicsState = kept.PhysicsState,
            Friction = kept.Friction,
            Elasticity = kept.Elasticity,
            InstanceSequence = kept.InstanceSequence,
            MovementSequence = kept.MovementSequence,
            ServerControlSequence = kept.ServerControlSequence,
            PositionSequence = kept.PositionSequence,
            ParentGuid = kept.ParentGuid,
            ParentLocation = kept.ParentLocation,
            PlacementId = kept.PlacementId,
            Physics = kept.Physics,
        };
    }

    private static SameEpochCreateObjectEvents SameEpochSignalsFor(
        RealmSession.MoverSpawn incoming)
    {
        var kinetics = incoming.Physics!.Value;
        var stamps = kinetics.Timestamps;

        CreateAnchorUpdate? ancestor = kinetics.Parent is { } affix
            ? new CreateAnchorUpdate(
                incoming.Guid,
                affix.Guid,
                affix.LocationId,
                kinetics.AnimationFrame ?? 0u,
                stamps.Instance,
                stamps.Position)
            : null;
        RealmSession.MoverPositionUpdate? locus = ancestor is null
            && kinetics.Position is { } p
            && p.LandblockId is not 0
                ? new RealmSession.MoverPositionUpdate(
                    incoming.Guid,
                    p,
                    kinetics.Velocity,
                    kinetics.AnimationFrame,
                    IsGrounded: true,
                    stamps.Instance,
                    stamps.Position,
                    stamps.Teleport,
                    stamps.ForcePosition)
                : null;
        PickupNotice.Parsed? lift = ancestor is null && locus is null
            ? new PickupNotice.Parsed(incoming.Guid, stamps.Instance, stamps.Position)
            : null;
        RealmSession.MoverMotionUpdate? travel =
            kinetics.Movement is { } travelBlob
            && !travelBlob.RawData.IsEmpty
            && travelBlob.MotionState is { } locomotionPhase
                ? new RealmSession.MoverMotionUpdate(
                    incoming.Guid,
                    locomotionPhase,
                    stamps.Instance,
                    stamps.Movement,
                    stamps.ServerControlledMove,
                    travelBlob.IsAutonomous is true)
                : null;

        return new SameEpochCreateObjectEvents(
            kinetics,
            new ObjDescNotice.Parsed(
                incoming.Guid,
                new ObjectCreation.SchemeBlob(
                    incoming.BasePaletteId,
                    incoming.SubPalettes,
                    incoming.TextureChanges,
                    incoming.AnimPartChanges),
                stamps.Instance,
                stamps.ObjDesc),
            ancestor,
            locus,
            lift,
            travel,
            new GroupPhase.Parsed(incoming.Guid, kinetics.RawState, stamps.Instance, stamps.State),
            new VelocityUpdate.Parsed(
                incoming.Guid,
                kinetics.Velocity ?? System.Numerics.Vector3.Zero,
                kinetics.AngularVelocity ?? System.Numerics.Vector3.Zero,
                stamps.Instance,
                stamps.Vector));
    }
}
