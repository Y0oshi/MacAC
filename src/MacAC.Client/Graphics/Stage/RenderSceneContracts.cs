using System.Numerics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Stage;

internal readonly record struct RenderMirrorId
{
    private RenderMirrorId(ulong val) => _val = val;

    internal static RenderMirrorId FromRaw(ulong val) => new(val);

    private readonly ulong _val;

    internal ulong RawValue => _val;
    internal byte Domain => (byte)(_val >> 56);
    internal RasterizeOrderTag ToOrderTag() => new(_val);
    internal void AppendTo(ref StableRasterizeHash128 digest) => digest.Add(_val);

    public int CompareTo(RenderMirrorId another) =>
        _val.CompareTo(another._val);

    public override string ToString() => $"projection:{_val:X16}";
}

internal readonly record struct RasterizeHolderIncarnation
{
    private RasterizeHolderIncarnation(ulong val) => _val = val;

    internal static RasterizeHolderIncarnation FromRaw(ulong val) => new(val);

    private readonly ulong _val;

    internal ulong RawValue => _val;

    public int CompareTo(RasterizeHolderIncarnation another) =>
        _val.CompareTo(another._val);

    public override string ToString() => $"incarnation:{_val}";
}

internal readonly record struct RenderStageEpoch
{
    private RenderStageEpoch(ulong val) => _val = val;

    internal static RenderStageEpoch FromRaw(ulong val) => new(val);

    private readonly ulong _val;

    internal ulong RawValue => _val;

    public int CompareTo(RenderStageEpoch another) =>
        _val.CompareTo(another._val);

    public override string ToString() => $"generation:{_val}";
}

internal readonly record struct RasterizeSpatialBin
{
    private RasterizeSpatialBin(ulong val) => _val = val;

    internal static RasterizeSpatialBin FromRaw(ulong val) => new(val);

    private readonly ulong _val;

    internal ulong RawValue => _val;

    public int CompareTo(RasterizeSpatialBin another) =>
        _val.CompareTo(another._val);

    public override string ToString() => $"bucket:{_val:X16}";
}

internal readonly record struct RasterizeAssetHnd
{
    private RasterizeAssetHnd(ulong val) => _val = val;

    internal static RasterizeAssetHnd FromRaw(ulong val) => new(val);

    private readonly ulong _val;

    internal ulong RawValue => _val;

    public int CompareTo(RasterizeAssetHnd another) =>
        _val.CompareTo(another._val);

    public override string ToString() => $"asset:{_val:X16}";
}

internal enum RenderMirrorClass : byte
{
    OutdoorStatic,
    IndoorCellStatic,
    LiveDynamicRoot,
    ActiveAnimatedStatic,
    EquippedChild,
}

[Flags]
internal enum RenderMirrorFlags : uint
{
    None = 0,
    Draw = 1 << 0,
    Hidden = 1 << 1,
    AncestorHidden = 1 << 2,
    Translucent = 1 << 3,
    Selectable = 1 << 4,
    LightCandidate = 1 << 5,
    PortalStraddling = 1 << 6,
    SpatiallyResident = 1 << 7,
}

[Flags]
internal enum RasterizeStaleBitmask : ushort
{
    None = 0,
    Transform = 1 << 0,
    Appearance = 1 << 1,
    Flags = 1 << 2,
    SpatialResidency = 1 << 3,
    WorldBounds = 1 << 4,
    SortKey = 1 << 5,
    All = ushort.MaxValue,
}

internal readonly record struct RasterizeTransform(
    Vector3 Position,
    Quaternion Rotation,
    float UniformScale,
    Matrix4x4 LocalToWorld)
{
    public RasterizeTransform(Matrix4x4 ownToRealm)
        : this(
            ownToRealm.Translation,
            Quaternion.Identity,
            1.0f,
            ownToRealm)
    {
    }

    public static RasterizeTransform FromTrunk(
        Vector3 locus,
        Quaternion spin,
        float uniformScaling) =>
        new(
            locus,
            spin,
            uniformScaling,
            Matrix4x4.CreateFromQuaternion(spin)
            * Matrix4x4.CreateTranslation(locus));
}

internal readonly record struct EarlierRasterizeTransform(Matrix4x4 LocalToWorld);

internal readonly record struct RasterizeTriMeshGroup(
    RasterizeAssetHnd Handle,
    int MeshCount,
    ulong Revision);

internal readonly record struct RasterizeMatlVariant(
    ulong PaletteKey,
    ulong TextureReplacementKey,
    float Opacity);

internal readonly record struct RenderSpatialTenancy(
    RasterizeSpatialBin Bucket,
    uint OwnerLandblockId,
    uint FullCellId);

internal readonly record struct RenderRealmBounds(Vector3 Minimum, Vector3 Maximum);

internal readonly record struct RenderDegradeLedger(byte Level, uint Revision);

internal readonly record struct RasterizeOrderTag(ulong Value);

internal interface IRasterizeTraversalOrderingOrigin
{
    bool TryFetchTraversalOrderTag(
        RealmActor actor,
        out RasterizeOrderTag orderTag);
}

internal static class RasterizeTraversalOrderTag
{
    public static RasterizeOrderTag Compose(
        uint landblockOrder,
        int entityIndex)
    {
        if (landblockOrder == 0)
            throw new ArgumentOutOfRangeException(nameof(landblockOrder));
        return entityIndex < 0
            ? throw new ArgumentOutOfRangeException(nameof(entityIndex))
            : new RasterizeOrderTag(
            ((ulong)landblockOrder << 32)
            | checked((uint)entityIndex));
    }
}

internal readonly record struct RasterizeOriginMetadata(
    uint LocalEntityId,
    uint ServerGuid,
    uint SourceId,
    uint ParentCellId,
    uint EffectCellId,
    uint BuildingShellAnchorCellId,
    RenderStageHash128 TransformFingerprint,
    RenderStageHash128 GeometryFingerprint,
    RenderStageHash128 AppearanceFingerprint,
    uint CurrentProjectionFlags = 0,
    RenderStageHash128 DirectionalShadowTopologyFingerprint = default);

internal enum RasterizeInvokerPersonaFlavor : byte
{
    Unclassified,
    OutdoorStatic,
    Building,
    LocalPlayer,
    RemotePlayer,
    NonPlayerCreature,
    OtherLiveDynamic,
    EquippedChild,
}

internal readonly record struct RenderActorPayload(
    IReadOnlyList<TriMeshRef> MeshRefs,
    SwatchOverride? PaletteOverride,
    bool IsBuildingShell,
    RasterizeInvokerPersonaFlavor CasterIdentity =
        RasterizeInvokerPersonaFlavor.Unclassified);

internal readonly record struct RenderMirrorRecord(
    RenderMirrorId Id,
    RenderMirrorClass ProjectionClass,
    RasterizeHolderIncarnation OwnerIncarnation,
    RasterizeTransform Transform,
    EarlierRasterizeTransform PreviousTransform,
    RasterizeTriMeshGroup MeshSet,
    RasterizeMatlVariant Material,
    RenderSpatialTenancy Residency,
    RenderRealmBounds Bounds,
    RenderMirrorFlags Flags,
    RenderDegradeLedger DegradeState,
    RasterizeOrderTag SortKey,
    RasterizeStaleBitmask DirtyMask,
    RasterizeOriginMetadata Source = default,
    RenderActorPayload EntityPayload = default);

internal enum RenderMirrorDiffKind : byte
{
    Register,
    UpdateTransform,
    UpdateAppearance,
    UpdateFlags,
    Rebucket,
    Unregister,
}

internal readonly record struct RenderMirrorDiff(
    RenderMirrorDiffKind Kind,
    RenderStageEpoch Generation,
    ulong JournalSequence,
    RenderMirrorRecord Record)
{
    public static RenderMirrorDiff Register(
        RenderStageEpoch gen,
        ulong journalSeries,
        in RenderMirrorRecord capture) =>
        new(
            RenderMirrorDiffKind.Register,
            gen,
            journalSeries,
            capture);

    public static RenderMirrorDiff Update(
        RenderMirrorDiffKind kind,
        RenderStageEpoch gen,
        ulong journalSeries,
        in RenderMirrorRecord capture)
    {
        return kind is RenderMirrorDiffKind.Register
            or RenderMirrorDiffKind.Unregister
            ? throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Use Register or Unregister for structural deltas")
            : new RenderMirrorDiff(kind, gen, journalSeries, capture);
    }

    public static RenderMirrorDiff Unregister(
        RenderStageEpoch gen,
        ulong journalSeries,
        RenderMirrorId ident,
        RasterizeHolderIncarnation holderIncarnation) =>
        new(
            RenderMirrorDiffKind.Unregister,
            gen,
            journalSeries,
            new RenderMirrorRecord() with
            {
                Id = ident,
                OwnerIncarnation = holderIncarnation,
            });
}

internal readonly record struct DynamicMirrorPulse(
    RenderMirrorId Id,
    RasterizeHolderIncarnation OwnerIncarnation,
    RasterizeTransform Transform,
    RenderRealmBounds Bounds);

internal readonly ref struct DynamicMirrorSyncInput
{
    public DynamicMirrorSyncInput(
        RenderStageEpoch gen,
        ReadOnlySpan<DynamicMirrorPulse> updates)
    {
        Generation = gen;
        Updates = updates;
    }

    public RenderStageEpoch Generation { get; }

    public ReadOnlySpan<DynamicMirrorPulse> Updates { get; }
}

internal readonly record struct RenderMirrorCounts(
    int Total,
    int OutdoorStatic,
    int IndoorCellStatic,
    int LiveDynamicRoot,
    int ActiveAnimatedStatic,
    int EquippedChild)
{
    public int For(RenderMirrorClass projectionClass) =>
        projectionClass switch
        {
            RenderMirrorClass.OutdoorStatic => OutdoorStatic,
            RenderMirrorClass.IndoorCellStatic => IndoorCellStatic,
            RenderMirrorClass.LiveDynamicRoot => LiveDynamicRoot,
            RenderMirrorClass.ActiveAnimatedStatic => ActiveAnimatedStatic,
            RenderMirrorClass.EquippedChild => EquippedChild,
            _ => throw new ArgumentOutOfRangeException(
                nameof(projectionClass),
                projectionClass,
                null),
        };
}

internal enum RenderStageIndex : byte
{
    OutdoorStatic,
    IndoorCellStatic,
    Dynamic,
    OutdoorDynamic,
    PortalStraddlingDynamic,
    Translucent,
    Selectable,
    LightCandidate,
    Dirty,
}

internal readonly record struct RenderStageIndexCounts(
    int OutdoorStatic,
    int IndoorCellStatic,
    int Dynamic,
    int OutdoorDynamic,
    int PortalStraddlingDynamic,
    int Translucent,
    int Selectable,
    int LightCandidate,
    int Dirty)
{
    public int For(RenderStageIndex index) =>
        index switch
        {
            RenderStageIndex.OutdoorStatic => OutdoorStatic,
            RenderStageIndex.IndoorCellStatic => IndoorCellStatic,
            RenderStageIndex.Dynamic => Dynamic,
            RenderStageIndex.OutdoorDynamic => OutdoorDynamic,
            RenderStageIndex.PortalStraddlingDynamic =>
                PortalStraddlingDynamic,
            RenderStageIndex.Translucent => Translucent,
            RenderStageIndex.Selectable => Selectable,
            RenderStageIndex.LightCandidate => LightCandidate,
            RenderStageIndex.Dirty => Dirty,
            _ => throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                null),
        };
}

internal readonly record struct RenderDiffApplyResult(
    long Applied,
    long Registered,
    long Updated,
    long Replaced,
    long Unregistered,
    long RejectedGeneration,
    long RejectedOutOfOrderSequence,
    long RejectedStaleIncarnation,
    long RejectedMissing)
{
    public long Rejected =>
        RejectedGeneration
        + RejectedOutOfOrderSequence
        + RejectedStaleIncarnation
        + RejectedMissing;
}

internal readonly record struct RenderStageMemoryAccounting(
    int EntityCount,
    int ArchEntityCapacity,
    int ArchetypeCount,
    int AllocatedChunkCount,
    long EstimatedChunkPayloadBytes,
    int ProjectionLookupCapacity,
    long EstimatedProjectionLookupBytes,
    long EstimatedIndexBytes,
    long EstimatedJournalBufferBytes,
    long EstimatedSynchronizationSourceBytes)
{
    public long TotalEstimatedBytes =>
        EstimatedChunkPayloadBytes
        + EstimatedProjectionLookupBytes
        + EstimatedIndexBytes
        + EstimatedJournalBufferBytes
        + EstimatedSynchronizationSourceBytes;
}

internal readonly record struct RenderStageDigest(
    RenderStageEpoch Generation,
    RenderMirrorCounts Counts,
    RenderStageHash128 Hash);

internal static class DirectionalShadeTransformChangeDiary
{
    internal const int Capacity = 16_384;
}

internal readonly struct DirectionalShadeTransformCapture
{
    internal DirectionalShadeTransformCapture(
        RenderMirrorId ident,
        RenderMirrorClass projClass,
        RasterizeTransform xform,
        RenderActorPayload actorCargo)
    {
        Id = ident;
        ProjClass = projClass;
        Transform = xform;
        ActorCargo = actorCargo;
    }

    internal readonly RenderMirrorId Id;
    internal readonly RenderMirrorClass ProjClass;
    internal readonly RasterizeTransform Transform;
    internal readonly RenderActorPayload ActorCargo;

    internal static DirectionalShadeTransformCapture Capture(
        in RenderMirrorRecord proj) =>
        new(
            proj.Id,
            proj.ProjectionClass,
            proj.Transform,
            proj.EntityPayload);
}

internal readonly record struct DirectionalShadeTransformChanges(
    ulong LatestRevision,
    int Count,
    bool RequiresFullRefresh,
    int UpdateTransformCount = 0,
    int UpdateAppearanceCount = 0,
    int DynamicSynchronizationCount = 0,
    int ActiveAnimatedStaticCount = 0,
    int LiveDynamicRootCount = 0,
    int EquippedChildCount = 0);

internal enum DirectionalShadeTransformChangeKind : byte
{
    UpdateTransform,
    UpdateAppearance,
    DynamicSynchronization,
}

internal sealed class RenderStageDigestBuffer
{
    internal List<RenderMirrorRecord> Records { get; } = [];

    internal int Capacity => Records.Capacity;
}

internal interface IRenderStageProbeSource
{
    RenderMirrorCounts GetCounts(RenderStageEpoch gen);
    RenderStageIndexCounts GetIndexCounts(RenderStageEpoch gen);
    ulong GetIndexRevision(RenderStageEpoch gen);
    ulong GetDirectionalShadowTopologyRevision(
        RenderStageEpoch gen);
    ulong GetDirectionalShadowTransformRevision(
        RenderStageEpoch gen);

    DirectionalShadeTransformChanges CopyDirectionalShadowTransformChanges(
        RenderStageEpoch gen,
        ulong followingRev,
        Span<DirectionalShadeTransformCapture> dest);

    bool TryGet(
        RenderStageEpoch gen,
        RenderMirrorId ident,
        out RenderMirrorRecord capture);

    bool TryGetByLocalEntityId(
        RenderStageEpoch gen,
        uint ownActorIdent,
        out RenderMirrorRecord capture);

    int CopyById(
        RenderStageEpoch gen,
        ReadOnlySpan<RenderMirrorId> idents,
        Span<RenderMirrorRecord> dest);

    int CopyTo(
        RenderStageEpoch gen,
        RenderMirrorClass? projClass,
        Span<RenderMirrorRecord> dest);

    int CopyIndexTo(
        RenderStageEpoch gen,
        RenderStageIndex ordinal,
        Span<RenderMirrorRecord> dest);

}

internal readonly struct RenderStageProbe
{
    internal RenderStageProbe(
        IRenderStageProbeSource src,
        RenderStageEpoch gen)
    {
        Source = src;
        Generation = gen;
    }

    public RenderStageEpoch Generation { get; }

    public RenderMirrorCounts Counts =>
        Source.GetCounts(Generation);

    public RenderStageIndexCounts OrdinalCounts =>
        Source.GetIndexCounts(Generation);

    public ulong OrdinalRev =>
        Source.GetIndexRevision(Generation);

    public ulong DirectedShadeWiringRev =>
        Source.GetDirectionalShadowTopologyRevision(Generation);

    public ulong DirectedShadeXformRev =>
        Source.GetDirectionalShadowTransformRevision(Generation);

    public DirectionalShadeTransformChanges DuplicateDirectedShadeXformEdits(
        ulong followingRev,
        Span<DirectionalShadeTransformCapture> dest) =>
        Source.CopyDirectionalShadowTransformChanges(
            Generation,
            followingRev,
            dest);

    public bool TryGet(
        RenderMirrorId ident,
        out RenderMirrorRecord capture) =>
        Source.TryGet(Generation, ident, out capture);

    public bool TryFetchByOwnActorIdent(
        uint ownActorIdent,
        out RenderMirrorRecord capture) =>
        Source.TryGetByLocalEntityId(Generation, ownActorIdent, out capture);

    public int DuplicateByIdent(
        ReadOnlySpan<RenderMirrorId> idents,
        Span<RenderMirrorRecord> dest) =>
        Source.CopyById(Generation, idents, dest);

    public int CopyTo(Span<RenderMirrorRecord> dest) =>
        Source.CopyTo(Generation, null, dest);

    public int CopyTo(
        RenderMirrorClass projClass,
        Span<RenderMirrorRecord> dest) =>
        Source.CopyTo(Generation, projClass, dest);

    public int DuplicateOrdinalTo(
        RenderStageIndex ordinal,
        Span<RenderMirrorRecord> dest) =>
        Source.CopyIndexTo(Generation, ordinal, dest);

    private IRenderStageProbeSource Source =>
        field
        ?? throw new InvalidOperationException("The render-scene query is uninitialized");
}

internal interface IRenderStage : IDisposable
{
    RenderStageEpoch Generation { get; }

    RenderMirrorCounts Counts { get; }

    RenderStageMemoryAccounting Memory { get; }

    RenderDiffApplyResult Apply(ReadOnlySpan<RenderMirrorDiff> diffs);

    void SynchronizeDynamicSrcs(in DynamicMirrorSyncInput feed);

    RenderStageDigest AssembleDigest(RenderStageDigestBuffer reuse);

    RenderStageProbe OpenAsk();

    void WipeStale();

    void Clear(RenderStageEpoch substituteGen);
}
