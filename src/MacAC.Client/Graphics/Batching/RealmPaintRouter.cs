using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MacAC.Dat;
using MacAC.Client.Graphics.Gpu;
using MacAC.Client.Graphics.Picking;
using MacAC.Client.Graphics.Stage;
using MacAC.Client.Graphics.Tenancy;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Illumination;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Batching;

public sealed partial class RealmPaintRouter : IDisposable
{
    public enum ActorSet
    {
        All,
    }

    private readonly BitmapStash _textures;

    private readonly RealmTriMeshBridge _triMeshBridge;

    private readonly ActorSummonBridge _actorSummonBridge;

    private readonly ICanonPickingRenderSink? _pickDrain;

    private readonly ICanonPickingLightingSource? _pickIllumination;

    private readonly CanonAlphaFifo? _alphaFifo;

    private readonly Func<uint, float>? _hierarchicalSeeThrough;

    private readonly AlphaPaintOrigin _alphaSrc;

    private readonly RetainedScratchCapacityRule _alphaTempRule;

    private int _tempPeakUnits;

    private ICurrentRenderRouterWatcher? _latestRasterizeTableauWatcher;

    public readonly record struct PaintStats(
        ActorSet Set,
        int EntitiesWalked,
        int MeshRefs,
        int Instances,
        int Draws,
        int CullRuns,
        int OpaqueDraws,
        int TransparentDraws,
        long Triangles);

    public bool CompoundTexturesPrimed { get; private set; } = true;

    internal const int CeilingCompoundWarmupScanActorsPerCycle = 4096;

    internal const int CeilingCompoundWarmupReadyActorsPerCycle = 128;

    private readonly Queue<RealmActor> _compoundWarmupFifo = new();

    private readonly HashSet<RealmActor> _compoundWarmupFollowed = [];

    private IReadOnlyList<RealmActor>? _compoundWarmupSrc;

    private ulong _compoundWarmupSrcGen;

    private uint _compoundWarmupDestChamber;

    private int _compoundWarmupRadius;

    private int _compoundWarmupScanOrdinal;

    private bool _compoundWarmupScanDone = true;

    private enum CompoundWarmupOutcome : byte
    {
        Complete,
        Pending,
        UploadBudgetBlocked,
    }

    private long _sensorWarmupPreviousEmitTs;

    private readonly ActorTaxonomyStash _stash;

    private readonly MacAC.Mechanics.Drawing.SeeThroughFadeKeeper _seeThroughFades;

    private readonly bool _tier1StashDisabled =
        string.Equals(Environment.GetEnvironmentVariable("MACAC_DISABLE_TIER1_CACHE"), "1", StringComparison.Ordinal);

    public bool AlphaToCoverage { get; set; } = true;

    public IReadOnlySet<uint> FoliageWindExclusions { get; set; } =
        System.Collections.Frozen.FrozenSet<uint>.Empty;



    private float[] _globalLampBlob = new float[SceneLightPacker.FloatsPerLamp * 16];   // 16 floats (4 vec4) per GlobalLight




    private bool _dynamicCycleBegun;


    private IReadOnlyList<LightEmitter>? _ptCapture;

    private readonly int[] _latestActorLampSetTemp = new int[LightKeeper.UpperLightsPerObject];

    private InstLampGroup _latestActorLampSet = InstLampGroup.Disabled;

    private bool _latestActorInside;

    private bool _latestActorStructureSpecifics;

    private Vector2 _latestActorPickIllumination = new(0f, 1f);

    private uint _sharedClipZoneSsbo;

    private uint _latestActorSocket;

    private bool _latestActorCulled;

    // Per-frame scratch arrays - Tasks 9-10 fully wire these
    /// <summary>The per-instance channels this frame stages for the GPU.</summary>
    private readonly InstStageBuffers _stage = new();

    private BatchData[] _lotBlob = new BatchData[256];

    private DrawElementsIndirectDirective[] _indirectDirectives = new DrawElementsIndirectDirective[256];

    private FaceCulling[] _drawCullModes = new FaceCulling[256];

    private FaceCulling[] _sequencedPaintPruneManners = new FaceCulling[256];

    private LotBlobPublic[] _lotPublicTemp = new LotBlobPublic[256];

    private readonly List<IndirectClusterFeed> _clusterFeedTemp = new(256);

    private readonly List<ClusterTag> _retiredClusterTags = [];

    private long _upcomingClusterEnrollment = 1;

    private long _clusterCycle;

    private int _solidPaintTally;

    private int _seeThruPaintTally;

    private int _seeThruByteShift;

    // std430 layout: uint TextureIndex at offset 0, float SurfaceOpacity at offset 4, uint
    // TextureLayer at offset 8, uint Flags at offset 12.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    /// <summary>
    /// The per-batch constants a draw hands the shaders: which texture, how opaque the surface is
    /// authored to be, and the flag word.
    /// </summary>
    private struct BatchData
    {
        /// <summary>Bit 0: this batch may take the retail detail overlay, if one is bound.</summary>
        public const uint AcceptsDetailOverlayBit = 1u;

        public uint TextureIndex;   // slot into the device texture table
        public float SurfaceOpacity;
        public uint TextureStratum;

        /// <summary>
        /// <see cref="AcceptsDetailOverlayBit"/> plus the foliage wind bits the vertex shader reads.
        /// </summary>
        public uint Flags;

        /// <summary>
        /// The constants for one draw identity. Two passes build this — the deferred alpha pass and
        /// the ordered stream — and both have to pick the same four fields off the identity and OR
        /// the same bit in. A batch that lost the detail bit simply stops taking its overlay, which
        /// looks like flat ground rather than like a bug.
        /// </summary>
        public static BatchData For(ClusterTag tag) => new()
        {
            TextureIndex = tag.TextureSlot.Index,
            SurfaceOpacity = tag.SurfaceOpacity,
            TextureStratum = tag.TextureLayer,
            Flags = AcceptsDetailOverlayBit | tag.FoliageFlags,
        };
    }

    /// <summary>
    /// One transparent instance held back to be drawn in the retail alpha order. It is the draw
    /// identity plus the same per-instance channels every other instance carries — spelling them
    /// out again here was a third copy of that list to keep in step.
    /// </summary>
    private readonly record struct PostponedAlphaInst(
        ClusterTag Key,
        InstFacts Inst);

    internal readonly record struct InstLampGroup(
        int L0, int L1, int L2, int L3,
        int L4, int L5, int L6, int L7)
    {
        public static InstLampGroup Disabled { get; } = new(
            -1, -1, -1, -1, -1, -1, -1, -1);

        public static InstLampGroup From(ReadOnlySpan<int> source)
        {
            return source.Length < LightKeeper.UpperLightsPerObject
                ? throw new ArgumentException("A retail object-light set needs eight entries", nameof(source))
                : new InstLampGroup(
                source[0], source[1], source[2], source[3],
                source[4], source[5], source[6], source[7]);
        }

        public void DuplicateTo(int[] dest, int shift)
        {
            dest[shift + 0] = L0;
            dest[shift + 1] = L1;
            dest[shift + 2] = L2;
            dest[shift + 3] = L3;
            dest[shift + 4] = L4;
            dest[shift + 5] = L5;
            dest[shift + 6] = L6;
            dest[shift + 7] = L7;
        }

        public int this[int index] => index switch
        {
            0 => L0,
            1 => L1,
            2 => L2,
            3 => L3,
            4 => L4,
            5 => L5,
            6 => L6,
            7 => L7,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
    }

    private sealed class AlphaPaintOrigin(RealmPaintRouter holder) : ICanonAlphaDrawSource
    {
        public void ReadyAlphaDraws(ReadOnlySpan<int> tickets)
            => holder.ReadyPostponedAlphaDraws(tickets);

        public void SketchReadiedAlphaLot(int leadReadiedPaint, int paintTally)
            => holder.PaintReadiedAlphaLot(leadReadiedPaint, paintTally);

        public void RestartAlphaSubmissions()
            => holder.RestartPostponedAlphaSubmissions();
    }

    // Per-frame scratch - reused across frames to avoid per-frame allocation.
    private readonly Dictionary<ClusterTag, InstCluster> _clusters = [];

    private readonly List<InstCluster> _solidDraws = [];

    private readonly List<InstCluster> _translucentDraws = [];

    private readonly List<ClientAlphaFingerprint> _alphaFingerprintTemp = [];

    private readonly List<PostponedAlphaInst> _deferredAlpha = new(128);

    private SeeThroughKind[] _postponedAlphaSorts = new SeeThroughKind[128];

    private Matrix4x4 _postponedAlphaLensProj;

    private int _upcomingInstSubmissionOrdering;

    // A.5 T26 follow-up (Bug B): WalkEntities populates this scratch list instead of allocating a
    // fresh List<(WorldEntity, int)> per frame.
    private readonly List<(RealmActor Entity, int MeshRefIndex, uint LandblockId)> _strollTemp = [];

    private readonly List<RasterizeInstTuple> _contenderTupleTemp = [];

    private readonly List<ShelvedBatch> _fillTemp = [];

    private readonly List<ShelvedPickingPart> _fillPickTemp = [];

    private const float PerActorPruneRadius = 5.0f;

    private RetryableAssetFreeRegister? _teardownAssetList;

    private bool _disposing;

    private bool _destroyed;

    private int _actorsObserved;

    private int _actorsDrawn;

    private int _triMeshesAbsent;

    private int _drawsIssued;

    private int _instsIssued;

    private long _previousTraceBeat;

    private readonly HashSet<ulong> _missRequested = [];

    private readonly HashSet<ulong> _missLogged = [];

    // CPU + GPU timing for [WB-DIAG] under MACAC_WB_DIAG=1.
    private readonly System.Diagnostics.Stopwatch _cpuStopwatch = new();

    private readonly long[] _cpuSpecimens = new long[256];   // microseconds

    private int _cpuSpecimenCur;

    private readonly long[] _gpuSpecimens = new long[256];   // microseconds

    private int _gpuSpecimenCur;

    public readonly record struct LandblockListing(
        uint LandblockId,
        Vector3 AabbMin,
        Vector3 AabbMax,
        IReadOnlyList<RealmActor> Entities,
        IReadOnlyDictionary<uint, RealmActor>? AnimatedById);

    public struct StrideResult
    {
        public int ActorsWalked;
        public int StructureShellMooringPass;
        public int StructureShellMooringReject;
        public List<(RealmActor Entity, int MeshRefIndex, uint LandblockId)> ToPaint;
    }

    internal static void TraverseActorsInto(
        IEnumerable<LandblockListing> lbListings,
        FrustumFacets? frustum,
        uint? neverPruneLbIdent,
        HashSet<uint>? shownChamberIdents,
        HashSet<uint>? movingActorIdents,
        List<(RealmActor Entity, int MeshRefIndex, uint LandblockId)> temp,
        ref StrideResult outcome,
        ActorSet set = ActorSet.All)
    {
        temp.Clear();
        outcome.ActorsWalked = 0;
        outcome.ToPaint = temp;

        foreach (var listing in lbListings)
        {
            bool lbShown = frustum is null
                || listing.LandblockId == neverPruneLbIdent
                || FrustumPruner.IsAabbShown(frustum.Value, listing.AabbMin, listing.AabbMax);

            if (!lbShown)
            {
                if (movingActorIdents is null || movingActorIdents.Count == 0) continue;
                if (listing.AnimatedById is null) continue;
                foreach (var movingIdent in movingActorIdents)
                {
                    if (!listing.AnimatedById.TryGetValue(movingIdent, out var actor)) continue;
                    if (!actor.IsPaintShown || !actor.IsAncestorPaintShown) continue;
                    if (!ActorFitsSet(actor, set)) continue;
                    if (actor.MeshRefs.Count == 0) continue;
                    bool shellScoped = IsShellScopedSet(set)
                        && actor.IsStructureShell
                        && shownChamberIdents is not null;
                    if (!ActorPasssShownChamberLatch(actor, shownChamberIdents, set))
                    {
                        if (shellScoped) outcome.StructureShellMooringReject++;
                        continue;
                    }
                    if (shellScoped) outcome.StructureShellMooringPass++;
                    outcome.ActorsWalked++;
                    for (int idx = 0; idx < actor.MeshRefs.Count; idx++)
                        temp.Add((actor, idx, listing.LandblockId));
                }
                continue;
            }

            foreach (var actor in listing.Entities)
            {
                if (!actor.IsPaintShown || !actor.IsAncestorPaintShown) continue;
                if (!ActorFitsSet(actor, set)) continue;
                if (actor.MeshRefs.Count == 0) continue;

                bool shellScoped = IsShellScopedSet(set)
                    && actor.IsStructureShell
                    && shownChamberIdents is not null;
                bool chamberInVis = ActorPasssShownChamberLatch(actor, shownChamberIdents, set);
                if (!chamberInVis)
                {
                    if (shellScoped) outcome.StructureShellMooringReject++;
                    continue;
                }
                if (shellScoped) outcome.StructureShellMooringPass++;

                bool isMoving = movingActorIdents?.Contains(actor.Id) == true;
                bool aabbShown = true;
                if (frustum is not null && !isMoving && listing.LandblockId != neverPruneLbIdent)
                {
                    if (actor.AabbStale) actor.RenewAabb();
                    aabbShown = FrustumPruner.IsAabbShown(frustum.Value, actor.AabbMin, actor.AabbMax);
                }

                if (!aabbShown)
                {
                    continue;
                }

                outcome.ActorsWalked++;
                for (int idx = 0; idx < actor.MeshRefs.Count; idx++)
                    temp.Add((actor, idx, listing.LandblockId));
            }
        }
    }

    public void Draw(
        IClientCamera cam,
        IEnumerable<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax,
                     IReadOnlyList<RealmActor> Entities,
                     IReadOnlyDictionary<uint, RealmActor>? AnimatedById)> lbListings,
        FrustumFacets? frustum = null,
        uint? neverPruneLbIdent = null,
        HashSet<uint>? shownChamberIdents = null,
        HashSet<uint>? movingActorIdents = null,
        ActorSet set = ActorSet.All)
    {
        bool diag = CommenceActorRelay(
            cam,
            out Matrix4x4 vp,
            out Vector3 camSpot);

        _upcomingInstSubmissionOrdering = 0;
        foreach (InstCluster cluster in _clusters.Values)
            cluster.WipePerInstBlob();

        uint anyVao = 0;

        static IEnumerable<LandblockListing> ToListings(
            IEnumerable<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax,
                         IReadOnlyList<RealmActor> Entities,
                         IReadOnlyDictionary<uint, RealmActor>? AnimatedById)> src)
        {
            foreach (var e in src)
                yield return new LandblockListing(e.LandblockId, e.AabbMin, e.AabbMax, e.Entities, e.AnimatedById);
        }

        // A.5 T26 follow-up (Bug B): use the no-alloc WalkEntitiesInto overload that populates
        // _walkScratch (a per-dispatcher field reused across frames) instead of allocating a fresh
        // List<(WorldEntity, int)> per frame.
        var strollOutcome = default(StrideResult);
        TraverseActorsInto(
            ToListings(lbListings),
            frustum,
            neverPruneLbIdent,
            shownChamberIdents,
            movingActorIdents,
            _strollTemp,
            ref strollOutcome,
            set);
        _latestRasterizeTableauWatcher?.WatchRouterPaint(
            set,
            strollOutcome.ActorsWalked,
            _strollTemp);
        AssembleLatestContenderTuples(
            _strollTemp,
            movingActorIdents,
            _contenderTupleTemp);

        uint? fillActorIdent = null;
        uint fillLbIdent = 0;

        uint? previousStrikeActorIdent = null;

        bool latestActorIncomplete = false;

        uint? earlierTupleActorIdent = null;

        foreach (RasterizeInstTuple tuple in _contenderTupleTemp)
        {
            RasterizeInstContender actor = tuple.Candidate;
            int pieceIndex = tuple.MeshRefIndex;
            uint lbIdent = actor.TupleLandblockId;
            if (diag) _actorsObserved++;

            if (previousStrikeActorIdent == actor.Id)
            {
                if (diag) _actorsDrawn++;
                continue;
            }

            if (previousStrikeActorIdent.HasValue && previousStrikeActorIdent.Value != actor.Id)
            {
                previousStrikeActorIdent = null;
            }

            uint stashLb = LocateStashLbHint(in actor);

            bool isNewActor = !earlierTupleActorIdent.HasValue || earlierTupleActorIdent.Value != actor.Id;
            if (isNewActor)
            {
                if (fillActorIdent.HasValue && latestActorIncomplete)
                {
                    _fillTemp.Clear();
                    _fillPickTemp.Clear();
                    fillActorIdent = null;
                }
                latestActorIncomplete = false;

                (_latestActorSocket, _latestActorCulled) = LocateSocketForCycle();

                CalculateActorLampSet(actor);
                _latestActorStructureSpecifics = actor.IsBuildingShell;
                _latestActorPickIllumination =
                    _pickIllumination?.TryFetchIllumination(
                        actor.ServerGuid,
                        actor.Id,
                        out var illumination) == true
                        ? new Vector2(illumination.Luminosity, illumination.Diffuse)
                        : new Vector2(0f, 1f);

            }
            earlierTupleActorIdent = actor.Id;

            (fillActorIdent, fillLbIdent) = MaybeDrainOnActorEdit(
                fillActorIdent, fillLbIdent, actor.Id, _stash,
                _fillTemp, _fillPickTemp);

            if (_latestActorCulled)
                continue;

            Matrix4x4 actorRealm = actor.RootWorld;

            bool isMoving = actor.Animated;

            if (!isMoving && !_tier1StashDisabled && _stash.TryGet(actor.Id, stashLb, out var stashedListing))
            {
                ImposeStashStrikeStraight(stashedListing!, actorRealm);

                if (_pickDrain is not null)
                    BroadcastStashedPickPieces(stashedListing!, actor, actorRealm);

                if (anyVao == 0)
                {
                    TriMeshRef leadTriMeshRef = tuple.MeshRef;
                    var leadRasterizeBlob = _triMeshBridge.TryFetchRenderData(leadTriMeshRef.GfxObjId);
                    if (leadRasterizeBlob is not null) anyVao = leadRasterizeBlob.VAO;
                }

                if (diag) _actorsDrawn++;
                previousStrikeActorIdent = actor.Id;

#if DEBUG
                System.Diagnostics.Debug.Assert(
                    !isMoving,
                    $"EntityClassificationCache hit on animated entity {actor.Id} — invariant violated");
#endif

                continue;
            }

            SwatchCompoundPersona swatchPersona = default;
            if (actor.PaletteOverride is not null)
                swatchPersona = BitmapStash.FetchSwatchPersona(actor.PaletteOverride);

            TriMeshRef triMeshRef = tuple.MeshRef;
            ulong gfxObjRefIdent = triMeshRef.GfxObjId;

            var rasterizeBlob = _triMeshBridge.TryFetchRenderData(gfxObjRefIdent);

            if (rasterizeBlob is null)
            {
                latestActorIncomplete = true;
                if (diag) _triMeshesAbsent++;
                if (_missRequested.Add(gfxObjRefIdent))
                {
                    _triMeshBridge.SecureFetched(gfxObjRefIdent);
                    if (diag && _missLogged.Add(gfxObjRefIdent))
                        Console.WriteLine($"[mesh-miss] 0x{gfxObjRefIdent:X10} re-requested at point of use");
                }
                continue;
            }
            if (anyVao == 0) anyVao = rasterizeBlob.VAO;

            var collector = isMoving ? null : _fillTemp;
            var pickCollector = isMoving ? null : _fillPickTemp;

            bool drewAny = false;
            if (rasterizeBlob.IsSetup && rasterizeBlob.SetupParts.Count > 0)
            {
                bool actorHasCutoutSubset = FoliageWindTaxonomy.CalculateActorHasCutoutSubset(
                    rasterizeBlob.SetupParts,
                    _triMeshBridge,
                    static (bridge, piece) => bridge.TryFetchRenderData(piece.GfxObjId) is { HasCutoutSubset: true });

                for (int rigPieceOrdinal = 0; rigPieceOrdinal < rasterizeBlob.SetupParts.Count; rigPieceOrdinal++)
                {
                    var (pieceGfxObjRefIdent, pieceXform) = rasterizeBlob.SetupParts[rigPieceOrdinal];
                    var pieceBlob = _triMeshBridge.TryFetchRenderData(pieceGfxObjRefIdent);
                    if (pieceBlob is null)
                    {
                        latestActorIncomplete = true;
                        if (diag) _triMeshesAbsent++;
                        if (_missRequested.Add(pieceGfxObjRefIdent))
                        {
                            _triMeshBridge.SecureFetched(pieceGfxObjRefIdent);
                            if (diag && _missLogged.Add(pieceGfxObjRefIdent))
                                Console.WriteLine($"[mesh-miss] 0x{pieceGfxObjRefIdent:X10} (setup part) re-requested at point of use");
                        }
                        continue;
                    }

                    var model = ConstructPieceRealmMatrix(
                        actorRealm, triMeshRef.PartTransform, pieceXform);

                    var restPosture = pieceXform * triMeshRef.PartTransform;

                    float densityMultiplier = ActorDensity(actor.ServerGuid);
                    if (densityMultiplier <= 0f) continue;
                    if (_seeThroughFades.TryFetchLatestVal(actor.Id, (uint)rigPieceOrdinal, out float seeThroughVal))
                    {
                        if (seeThroughVal >= 1.0f) continue; // skip this part's draw entirely
                        densityMultiplier *= 1f - seeThroughVal;
                    }

                    if (!ClassifyLots(pieceBlob, model, actor, triMeshRef, swatchPersona, restPosture, densityMultiplier, collector, actorHasCutoutSubset))
                        latestActorIncomplete = true;
                    _pickDrain?.AddVisiblePart(
                        actor.ServerGuid,
                        actor.LocalEntityId,
                        unchecked((pieceIndex << 16) | (rigPieceOrdinal & 0xFFFF)),
                        (uint)pieceGfxObjRefIdent,
                        model);
                    pickCollector?.Add(new ShelvedPickingPart(
                        unchecked((pieceIndex << 16) | (rigPieceOrdinal & 0xFFFF)),
                        (uint)pieceGfxObjRefIdent,
                        restPosture));
                    drewAny = true;
                }
            }
            else
            {
                float densityMultiplier = ActorDensity(actor.ServerGuid);
                bool fullyInvisible = false;
                if (densityMultiplier <= 0f)
                    fullyInvisible = true;
                if (_seeThroughFades.TryFetchLatestVal(actor.Id, (uint)pieceIndex, out float seeThroughVal))
                {
                    if (seeThroughVal >= 1.0f) fullyInvisible = true;
                    else densityMultiplier *= 1f - seeThroughVal;
                }

                if (!fullyInvisible)
                {
                    var model = triMeshRef.PartTransform * actorRealm;
                    if (!ClassifyLots(rasterizeBlob, model, actor, triMeshRef, swatchPersona, restPosture: triMeshRef.PartTransform, densityMultiplier: densityMultiplier, collector: collector))
                        latestActorIncomplete = true;
                    _pickDrain?.AddVisiblePart(
                        actor.ServerGuid,
                        actor.LocalEntityId,
                        pieceIndex,
                        (uint)gfxObjRefIdent,
                        model);
                    pickCollector?.Add(new ShelvedPickingPart(
                        pieceIndex,
                        (uint)gfxObjRefIdent,
                        triMeshRef.PartTransform));
                    drewAny = true;
                }
            }

            if (collector is not null)
            {
                fillActorIdent = actor.Id;
                fillLbIdent = stashLb;
            }

            if (diag && drewAny) _actorsDrawn++;
        }

        if (latestActorIncomplete)
        {
            _fillTemp.Clear();
            _fillPickTemp.Clear();
            fillActorIdent = null;
        }

        FinalDrainFill(
            fillActorIdent, fillLbIdent, _stash,
            _fillTemp, _fillPickTemp);

        ExecuteClassifiedGroups(
            vp,
            camSpot,
            anyVao,
            _clusters.Values,
            set,
            strollOutcome.ActorsWalked,
            _strollTemp.Count,
            diag,
            watchLatestTrail: true);
    }

    private void ExecuteClassifiedGroups(
        Matrix4x4 vp,
        Vector3 camSpot,
        uint anyVao,
        IEnumerable<InstCluster> clusters,
        ActorSet set,
        int actorsWalked,
        int tupleTally,
        bool diag,
        bool watchLatestTrail)
    {
        // Nothing visible - skip the pass entirely
        if (!TriMeshSrcPrimed())
        {
            PreviousPaintStats = new PaintStats(set, actorsWalked, tupleTally, 0, 0, 0, 0, 0, 0);
            ObserveClassifiedDispatcherSubmission(watchLatestTrail,
                shownInstTally: 0,
                immediateInstTally: 0,
                deferSeeThru: false);
            _cpuStopwatch.Stop();
            if (diag) MaybeDrainDiag();
            return;
        }

        bool deferSeeThru = _alphaFifo?.IsCollecting == true;
        var instCounts = PartitionInstanceGroups(
            clusters,
            deferSeeThru,
            camSpot,
            _solidDraws,
            _translucentDraws);
        int sumInsts = instCounts.VisibleInstances;
        int immediateInsts = instCounts.ImmediateInstances;
        if (sumInsts == 0)
        {
            PreviousPaintStats = new PaintStats(set, actorsWalked, tupleTally, 0, 0, 0, 0, 0, 0);
            ObserveClassifiedDispatcherSubmission(watchLatestTrail,
                shownInstTally: 0,
                immediateInstTally: 0,
                deferSeeThru);
            _cpuStopwatch.Stop();
            if (diag) MaybeDrainDiag();
            return;
        }

        _solidDraws.Sort(ContrastSolidSubmissionOrdering);
        if (deferSeeThru)
            DeferTransparentGroups(vp);
        else
            _translucentDraws.Sort(ContrastSeeThruSubmissionOrdering);

        _stage.EnsureRoom(immediateInsts);

        int cur = 0;
        foreach (InstCluster grp in _solidDraws)
            JunctureImmediateCluster(grp, ref cur);
        if (!deferSeeThru)
        {
            foreach (InstCluster grp in _translucentDraws)
                JunctureImmediateCluster(grp, ref cur);
        }
        System.Diagnostics.Debug.Assert(cur == immediateInsts);

        int immediateSeeThruTally = deferSeeThru ? 0 : _translucentDraws.Count;
        int sumDraws = _solidDraws.Count + immediateSeeThruTally;
        FollowTempDemand(Math.Max(sumInsts, sumDraws));
        if (_lotBlob.Length < sumDraws)
            _lotBlob = new BatchData[sumDraws + 64];
        if (_indirectDirectives.Length < sumDraws)
            _indirectDirectives = new DrawElementsIndirectDirective[sumDraws + 64];
        if (_drawCullModes.Length < sumDraws)
            _drawCullModes = new FaceCulling[sumDraws + 64];
        if (_lotPublicTemp.Length < sumDraws)
            _lotPublicTemp = new LotBlobPublic[sumDraws + 64];

        _clusterFeedTemp.Clear();
        foreach (var g in _solidDraws) _clusterFeedTemp.Add(ToFeed(g));
        if (!deferSeeThru)
            foreach (var g in _translucentDraws) _clusterFeedTemp.Add(ToFeed(g));

        var arrangement = AssembleIndirectArrs(
            _clusterFeedTemp,
            _indirectDirectives,
            _lotPublicTemp,
            _drawCullModes);
        long sumTriangles = 0;
        foreach (var feed in _clusterFeedTemp)
            sumTriangles += (long)(feed.IndexCount / 3) * feed.InstanceCount;
        int pruneExecutions =
            TallyPruneExecutions(_drawCullModes, 0, arrangement.OpaqueCount) +
            TallyPruneExecutions(_drawCullModes, arrangement.OpaqueCount, arrangement.TransparentCount);

        for (int idx = 0; idx < sumDraws; idx++)
        {
            _lotBlob[idx] = new BatchData
            {
                TextureIndex = _lotPublicTemp[idx].TextureIndex,
                SurfaceOpacity = _lotPublicTemp[idx].SurfaceOpacity,
                TextureStratum = _lotPublicTemp[idx].TextureLayer,
                Flags = _lotPublicTemp[idx].Flags,
            };
        }
        _solidPaintTally = arrangement.OpaqueCount;
        _seeThruPaintTally = arrangement.TransparentCount;
        _seeThruByteShift = arrangement.TransparentByteOffset;
        PreviousPaintStats = new PaintStats(
            set,
            actorsWalked,
            tupleTally,
            sumInsts,
            sumDraws,
            pruneExecutions,
            _solidPaintTally,
            _seeThruPaintTally,
            sumTriangles);
        ObserveClassifiedDispatcherSubmission(watchLatestTrail,
            sumInsts,
            immediateInsts,
            deferSeeThru);

        SubmitRhi(vp, immediateInsts, sumDraws, diag);
        _cpuStopwatch.Stop();
        if (diag)
        {
            long cpuUs = _cpuStopwatch.ElapsedTicks * 1_000_000L
                / System.Diagnostics.Stopwatch.Frequency;
            _cpuSpecimens[_cpuSpecimenCur] = cpuUs;
            _cpuSpecimenCur = (_cpuSpecimenCur + 1) % _cpuSpecimens.Length;
            _drawsIssued += _solidPaintTally + _seeThruPaintTally;
            _instsIssued += sumInsts;
            MaybeDrainDiag();
        }
    }

    public void Draw(
        IClientCamera cam,
        IEnumerable<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax,
                     IReadOnlyList<RealmActor> Entities,
                     IReadOnlyDictionary<uint, RealmActor>? AnimatedById)> lbListings,
        IReadOnlyCollection<uint> chamberIdents,
        FrustumFacets? frustum = null,
        uint? neverPruneLbIdent = null,
        HashSet<uint>? movingActorIdents = null,
        ActorSet set = ActorSet.All)
    {
        HashSet<uint> chamberIdentSet = chamberIdents is HashSet<uint> hs ? hs : [.. chamberIdents];
        Draw(cam, lbListings,
             frustum: frustum,
             neverPruneLbIdent: neverPruneLbIdent,
             shownChamberIdents: chamberIdentSet,
             movingActorIdents: movingActorIdents,
             set: set);
    }

    internal readonly record struct InstanceArrangementCounts(
        int VisibleInstances,
        int ImmediateInstances);

    internal static CurrentRenderRouterSubmission
        CreateDispatcherSubmission(
            int shownInstTally,
            int immediateInstTally,
            bool deferSeeThru,
            IReadOnlyList<InstCluster> solid,
            IReadOnlyList<InstCluster> seeThru,
            List<ClientAlphaFingerprint> alphaTemp)
    {
        IReadOnlyList<InstCluster> approvedSolid =
            shownInstTally == 0
                ? Array.Empty<InstCluster>()
                : solid;
        IReadOnlyList<InstCluster> approvedSeeThru =
            shownInstTally == 0
                ? Array.Empty<InstCluster>()
                : seeThru;
        int solidClusterTally = approvedSolid.Count;
        int seeThruClusterTally = approvedSeeThru.Count;
        var digest = StableRasterizeHash128.Create();
        digest.Add(shownInstTally);
        digest.Add(immediateInstTally);
        digest.Add(solidClusterTally);
        digest.Add(seeThruClusterTally);
        digest.Add(deferSeeThru);
        RenderStageHash128 solidDigest =
            AssembleSolidSubmissionDigest(approvedSolid);
        RenderStageHash128 seeThruDigest =
            BuildTransparentSubmissionDigest(
                approvedSeeThru,
                alphaTemp);
        RenderStageHash128 seeThruSetDigest =
            AssembleSolidSubmissionDigest(approvedSeeThru);
        digest.Add(solidDigest.Low);
        digest.Add(solidDigest.High);
        digest.Add(seeThruDigest.Low);
        digest.Add(seeThruDigest.High);
        digest.Add(seeThruSetDigest.Low);
        digest.Add(seeThruSetDigest.High);

        return new CurrentRenderRouterSubmission(
            VisibleInstanceCount: shownInstTally,
            ImmediateInstanceCount: immediateInstTally,
            OpaqueGroupCount: solidClusterTally,
            TransparentGroupCount: seeThruClusterTally,
            TransparentDeferred: deferSeeThru,
            OpaqueDigest: solidDigest,
            TransparentDigest: seeThruDigest,
            TransparentSetDigest: seeThruSetDigest,
            Digest: digest.Finish());
    }

    internal readonly record struct ClientAlphaFingerprint(
        InstCluster Group,
        int InstanceIndex,
        int SubmissionOrder);

    private sealed class AlphaSubmissionOrderingComparer :
        IComparer<ClientAlphaFingerprint>
    {
        public static AlphaSubmissionOrderingComparer Instance { get; } =
            new();

        private AlphaSubmissionOrderingComparer()
        {
        }

        public int Compare(
            ClientAlphaFingerprint left,
            ClientAlphaFingerprint right) =>
            left.SubmissionOrder.CompareTo(right.SubmissionOrder);
    }

    internal static (uint? PopulateEntityId, uint PopulateLandblockId)
        MaybeDrainOnActorEdit(
            uint? fillActorIdent,
            uint fillLbIdent,
            uint latestActorIdent,
            ActorTaxonomyStash stash,
            List<ShelvedBatch> fillTemp,
            List<ShelvedPickingPart>? pickTemp = null)
    {
        if (fillActorIdent.HasValue && fillActorIdent.Value != latestActorIdent)
        {
            if (fillTemp.Count > 0)
            {
                stash.Fill(
                    fillActorIdent.Value,
                    fillLbIdent,
                    [.. fillTemp],
                    pickTemp?.ToArray());
            }
            fillTemp.Clear();
            pickTemp?.Clear();
            return (null, 0u);
        }
        return (fillActorIdent, fillLbIdent);
    }

    private bool ClassifyLots(
        ThingRasterizeBlob rasterizeBlob,
        Matrix4x4 model,
        in RasterizeInstContender actor,
        TriMeshRef triMeshRef,
        SwatchCompoundPersona swatchPersona,
        Matrix4x4 restPosture,
        float densityMultiplier = 1.0f,
        List<ShelvedBatch>? collector = null,
        bool? actorHasCutoutSubsetOverride = null)
    {
        if (_triMeshBridge.IsCoreConcealedMarker(triMeshRef.GfxObjId))
            return true;

        bool actorHasCutoutSubset = actorHasCutoutSubsetOverride ?? rasterizeBlob.HasCutoutSubset;
        bool allTexturesPrimed = true;
        for (int lotIndex = 0; lotIndex < rasterizeBlob.Batches.Count; lotIndex++)
        {
            bool survives = TryClassifyLot(
                rasterizeBlob, lotIndex, in actor, triMeshRef, swatchPersona,
                densityMultiplier, actorHasCutoutSubset,
                out ClusterTag tag, out bool compoundQueued);
            if (compoundQueued)
                allTexturesPrimed = false;
            if (!survives)
                continue;
            GpuTextureSlot bmpSocket = tag.TextureSlot;

            InstCluster grp = FetchOrBuildInstCluster(tag);
            grp.Instances.Append(LatestInstance(model, densityMultiplier));
            collector?.Add(new ShelvedBatch(
                tag,
                bmpSocket,
                restPosture,
                grp,
                grp.Ticket));
        }
        return allTexturesPrimed;
    }

    private readonly record struct SettledBitmap(GpuTextureSlot Slot, uint Layer);

    public const int PaintDirectiveStride = 20; // sizeof(DrawElementsIndirectDirective): 5 × uint

    public readonly record struct IndirectClusterFeed(
        int IndexCount,
        uint FirstIndex,
        int BaseVertex,
        int InstanceCount,
        int FirstInstance,
        uint TextureIndex,
        uint TextureLayer,
        SeeThroughKind Translucency,
        CanonSurfaceMaterialState MaterialState,
        float SurfaceOpacity = 1f,
        FaceCulling CullMode = FaceCulling.CounterClockwise,
        uint FoliageFlags = 0u);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct LotBlobPublic
    {
        public uint TextureIndex;
        public float SurfaceOpacity;
        public uint TextureLayer;
        public uint Flags;
    }

    public readonly record struct IndirectArrangementResult(
        int OpaqueCount,
        int TransparentCount,
        int TransparentByteOffset);

    internal readonly record struct DetailDirectiveRun(
        int FirstCommand,
        int CommandCount);

    internal sealed class InstCluster(long enrollment = 0)
    {
        /// <summary>
        /// Unique for this cluster's lifetime, and cleared to zero when it retires. Tickets compare
        /// against it, so it is set here and by <see cref="Retire"/> and nowhere else.
        /// </summary>
        public long Enrollment { get; private set; } = enrollment;

        /// <summary>A ticket a shelved batch can hold to find its way back here.</summary>
        public ClusterTicket Ticket => new(Enrollment);

        /// <summary>
        /// Takes the cluster out of service: hands its instance storage back and clears the
        /// registration, so every ticket still naming it stops matching.
        /// </summary>
        public void Retire()
        {
            Enrollment = 0;
            FreePerInstDepot();
        }

        public long PreviousConsumedCycle;
        public uint LeadIndex;
        public int BaseVertex;
        public int IdxTally;
        public MacAC.Client.Graphics.Gpu.GpuTextureSlot TextureSlot =
            MacAC.Client.Graphics.Gpu.GpuTextureSlot.Unassigned;
        public uint TextureStratum;
        public SeeThroughKind Translucency;
        public CanonSurfaceMaterialState MaterialState =
            CanonSurfaceMaterialState.Opaque;
        public float SurfaceOpacity = 1f;
        public FaceCulling CullMode;
        public int LeadInst;   // offset into the shared instance VBO (in instances, not bytes)
        public int InstCount;

        public uint FoliageFlagSet;

        public float SortDistance;
        /// <summary>The instances this cluster draws, appended whole so the channels cannot drift.</summary>
        public readonly InstRoster Instances = new();

        public void WipePerInstBlob() => Instances.Clear();

        public void FreePerInstDepot() => Instances.Release();
    }

    private static bool IsSolid(SeeThroughKind kind)
        => kind is SeeThroughKind.Opaque or SeeThroughKind.ClipMap;

    internal int DynamicBufSetTally => 0;

    internal long AlphaTempAllowanceOctets => _alphaTempRule.AllowanceBytes;

    internal long KeptAlphaTempOctets => checked(
        _stage.OctetsHeld
        + (long)_lotBlob.Length * Unsafe.SizeOf<BatchData>()
        + (long)_indirectDirectives.Length
            * Unsafe.SizeOf<DrawElementsIndirectDirective>()
        + (long)_drawCullModes.Length * Unsafe.SizeOf<FaceCulling>()
        + (long)_lotPublicTemp.Length
            * Unsafe.SizeOf<LotBlobPublic>()
        + (long)_postponedAlphaSorts.Length
            * Unsafe.SizeOf<SeeThroughKind>()
        + (long)_deferredAlpha.Capacity
            * Unsafe.SizeOf<PostponedAlphaInst>());
}
