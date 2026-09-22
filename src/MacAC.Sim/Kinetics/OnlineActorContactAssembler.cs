using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Wire;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Dat;

namespace MacAC.Sim.Kinetics;

// Everything the proxy registry needs to file one live entity's collision and render parts
internal sealed record OnlineActorContactRegistration(
    uint EntityId,
    uint SourceId,
    Vector3 EntityWorldPosition,
    Quaternion EntityWorldRotation,
    IReadOnlyList<ProxyShape> Shapes,
    uint State,
    ActorImpactFlagSet Flags,
    float WorldOffsetX,
    float WorldOffsetY,
    uint LandblockId,
    uint SeedCellId,
    IReadOnlyList<ProxyShape> RenderParts);

internal sealed class OnlineActorContactAssembler
{
    private readonly Func<uint, ProxyPartGeometry?> _bspLimits;
    private readonly Func<uint, bool> _hasBsp;
    private readonly OnlineActorDefaultPosePicker _posture;
    private readonly Func<uint, GfxObjKinetics?> _gfxObjRef;
    private readonly Func<uint, GfxObjVisualExtent?> _visualLimits;

    public OnlineActorContactAssembler(KineticAssetCache kineticsBlob, OnlineActorDefaultPosePicker defaultPosture)
        : this(ident => BspLimitsOf(kineticsBlob, ident), defaultPosture, ident => kineticsBlob.FetchGfxObjRef(ident), ident => kineticsBlob.FetchVisualLimits(ident))
    {
        ArgumentNullException.ThrowIfNull(kineticsBlob);
    }

    internal OnlineActorContactAssembler(
        Func<uint, ProxyPartGeometry?> physicsBspBounds,
        OnlineActorDefaultPosePicker defaultPose,
        Func<uint, GfxObjKinetics?>? fetchGfxObjRef = null,
        Func<uint, GfxObjVisualExtent?>? fetchVisualLimits = null)
    {
        _bspLimits = physicsBspBounds ?? throw new ArgumentNullException(nameof(physicsBspBounds));
        _hasBsp = ident => _bspLimits(ident) is not null;
        _posture = defaultPose ?? throw new ArgumentNullException(nameof(defaultPose));
        _gfxObjRef = fetchGfxObjRef ?? (static _ => null);
        _visualLimits = fetchVisualLimits ?? (static _ => null);
    }

    public static uint[] LocateNetPieceIdentities(IReadOnlyList<uint> postAnimPieceGfxObjRefIdents, Func<uint, uint> locateSocketZero)
    {
        ArgumentNullException.ThrowIfNull(postAnimPieceGfxObjRefIdents);
        ArgumentNullException.ThrowIfNull(locateSocketZero);
        uint[] net = new uint[postAnimPieceGfxObjRefIdents.Count];
        for (int idx = 0; idx < net.Length; ++idx)
            net[idx] = locateSocketZero(postAnimPieceGfxObjRefIdents[idx]);
        return net;
    }

    public OnlineActorContactRegistration? Build(
        RealmActor actor,
        RigSpec rig,
        IReadOnlyList<uint> netPieceGfxObjRefIdents,
        RealmSession.MoverSpawn summon,
        uint anticipatedSrvOid,
        ulong anticipatedGen,
        RealmActor anticipatedActor,
        KineticStateFlags anticipatedFinalKineticsPhase,
        Vector3 realmOrigin,
        bool retainVacantCargo = false)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(rig);
        ArgumentNullException.ThrowIfNull(netPieceGfxObjRefIdents);
        ArgumentNullException.ThrowIfNull(anticipatedActor);
        if (summon.Position is not { } locus)
            return null;
        if (summon.Guid != anticipatedSrvOid || summon.InstanceSequence != anticipatedGen || !ReferenceEquals(anticipatedActor, actor))
            throw new InvalidOperationException("Live collision construction needs the exact materialized record");

        float scaling = summon.ObjScale ?? 1f;
        var posture = _posture.Resolve(summon.MotionTableId ?? 0u, rig.PartIds.Count);
        var forms = ProxyShapeBuilder.FromSetup(
            rig, scaling, _hasBsp, piecePostureOverride: posture, netPieceGfxObjRefIdents: netPieceGfxObjRefIdents, kineticsBspLimits: _bspLimits);
        IReadOnlyList<ProxyShape> rasterizePieces = ProxyShapeBuilder.FromRigRasterizePieces(rig, scaling, netPieceGfxObjRefIdents, posture, _gfxObjRef, _visualLimits);

        if (forms.Count is 0 && rasterizePieces.Count is 0 && !retainVacantCargo)
            return null;

        var flagSet = ActorImpactFlagSet.HasWeenie;
        if (summon.ObjectDescriptionFlags is { } pwd)
            flagSet |= EntityContactFlagsExt.FromPwdBitfield(pwd);
        if (summon.ItemType == (uint)GearKind.Creature)
            flagSet |= ActorImpactFlagSet.IsCreature;

        return new OnlineActorContactRegistration(
            actor.Id,
            actor.SrcGfxObjRefOrRigIdent,
            actor.Position,
            actor.Rotation,
            forms,
            (uint)anticipatedFinalKineticsPhase,
            flagSet,
            realmOrigin.X,
            realmOrigin.Y,
            locus.LandblockId,
            locus.LandblockId,
            rasterizePieces);
    }

    public static void Register(ProxyRegistry registry, OnlineActorContactRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(registration);
        registry.EnrollMultiPiece(
            registration.EntityId, registration.EntityWorldPosition, registration.EntityWorldRotation, registration.Shapes, registration.State, registration.Flags,
            registration.WorldOffsetX, registration.WorldOffsetY, registration.LandblockId, registration.SeedCellId, isStatic: false, pieceArr: registration.RenderParts);

        if (!KineticTelemetry.ProbeBuildingEnabled)
            return;

        int cylinders = registration.Shapes.Count(static shape => shape.ImpactKind == ProxyContactType.Cylinder);
        int bsps = registration.Shapes.Count - cylinders;
        Console.WriteLine(FormattableString.Invariant(
            $"[entity-source] id=0x{registration.EntityId:X8} entityId=0x{registration.EntityId:X8} src=0x{registration.SourceId:X8} gfxObj=0x{registration.SourceId:X8} lb=0x{registration.LandblockId:X8} shapes=cyl{cylinders}+bsp{bsps} note=server-spawn-root state=0x{registration.State:X8} flags={registration.Flags}"));
    }

    public static void SettleLooks(ProxyRegistry registry, uint actorIdent, OnlineActorContactRegistration? registration, bool suspendIfNew)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (registration is null)
            return;
        if (registration.EntityId != actorIdent)
            throw new InvalidOperationException("Collision replacement belongs to a different live entity");

        registry.ReplaceMultiPieceCargo(
            registration.EntityId, registration.EntityWorldPosition, registration.EntityWorldRotation, registration.Shapes, registration.State, registration.Flags,
            registration.WorldOffsetX, registration.WorldOffsetY, registration.LandblockId, registration.SeedCellId, isStatic: false, suspendIfNew, pieceArr: registration.RenderParts);
    }

    private static ProxyPartGeometry? BspLimitsOf(KineticAssetCache kineticsBlob, uint ident)
    {
        var asset = kineticsBlob.FetchPlanarGfxObjRef(ident);
        var bsp = asset?.PhysicsBsp;
        return bsp is { TrunkOrdinal: >= 0 } ? ProxyPartGeometry.Create(bsp.Joints[bsp.TrunkOrdinal].BoundingSphere, asset!.VisualBounds) : null;
    }
}
