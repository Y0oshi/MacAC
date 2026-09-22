using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Stage;
using MacAC.Mechanics.Landscape;

namespace MacAC.Client.Paging;

public readonly record struct LandblockDisplayTelemetry(
    LandblockRenderHeraldTelemetry Render,
    LandblockKineticsHeraldTelemetry Physics,
    LandblockStaticDisplayTelemetry Statics);

internal readonly record struct LandblockBulletinProceed(
    bool Completed,
    bool Progressed);

public sealed partial class LandblockDisplayPipeline
{
    private enum BulletinFlavor : byte
    {
        Loaded,
        PromoteExisting,
        PromoteSelfContained,
        Far,
    }

    private sealed class BulletinTransaction
    {
        public required BulletinFlavor Kind;
        public required LandblockAssemble Build;
        public required LandblockTessellationData TriMeshBlob;
        public required uint LandblockId;
        public required LandblockFlowPriceEstimate Cost;
        public LandblockFlowTier Tier;
        public bool ExhibitSealed;
        public LandblockRasterizeBulletin? RasterizeBulletin;
        public LandblockKineticsPublication? KineticsBulletin;
        public LandblockStaticDisplayPublication? StaticBulletin;
        public bool SpatialSealed;
        public GpuLandblockSpatialBulletin? SpatialBulletin;
        public bool SpatialExhibitSealed;
        public bool EnvironChamberRerunSealed;
        public bool OnlineRecoverySealed;

        public BulletinJunctureTimings? Timing;
    }

    private readonly Action<LandblockAssemble, LandblockTessellationData>?
        _broadcastPriorSpatialSeal;
    private readonly LandblockKineticsHerald? _kineticsPublisher;

    private readonly LandblockStaticDisplayHerald? _staticPublisher;

    private readonly GpuRealmPhase _phase;

    private readonly Action<uint>? _onLbFetched;

    private readonly Action<EnvironChamberLandblockAssemble>? _secureEnvironChamberTriMeshes;

    private readonly IRenderStaticMirrorDiarySink? _staticProjDrain;

    private readonly LandblockRetirementMarshal _retirements;

    private readonly Dictionary<LandblockFlowOutcome, BulletinTransaction> _publications =
        new(ReferenceEqualityComparer.Instance);

    internal LandblockDisplayPipeline(
        Action<LandblockAssemble, LandblockTessellationData> broadcastPriorSpatialSeal,
        GpuRealmPhase phase,
        Action<uint>? onLbFetched = null,
        Action<EnvironChamberLandblockAssemble>? secureEnvironChamberTriMeshes = null,
        LandblockRetirementMarshal? retirementCoordinator = null,
        Action<uint>? dropLand = null,
        Action<uint>? demoteNearbyStratum = null,
        IRenderStaticMirrorDiarySink? staticProjDrain = null)
    {
        ArgumentNullException.ThrowIfNull(broadcastPriorSpatialSeal);
        ArgumentNullException.ThrowIfNull(phase);

        _broadcastPriorSpatialSeal = broadcastPriorSpatialSeal;
        _phase = phase;
        _onLbFetched = onLbFetched;
        _secureEnvironChamberTriMeshes = secureEnvironChamberTriMeshes;
        _staticProjDrain = staticProjDrain;
        if (retirementCoordinator is not null
            && !retirementCoordinator.FitsPhase(_phase))
        {
            throw new ArgumentException(
                "The retirement coordinator must own the pipeline's world state",
                nameof(retirementCoordinator));
        }
        _retirements = retirementCoordinator
            ?? LandblockRetirementMarshal.BuildLegacy(
                phase,
                dropLand,
                demoteNearbyStratum);
    }

    public LandblockDisplayPipeline(
        LandblockRenderHerald rasterizePublisher,
        LandblockKineticsHerald kineticsPublisher,
        LandblockStaticDisplayHerald staticPublisher,
        GpuRealmPhase phase,
        LandblockDisplayRetirementOwner sunsetHolder,
        Action<uint>? onLbFetched = null,
        Action<EnvironChamberLandblockAssemble>? secureEnvironChamberTriMeshes = null)
        : this(
            rasterizePublisher,
            kineticsPublisher,
            staticPublisher,
            phase,
            sunsetHolder,
            onLbFetched,
            secureEnvironChamberTriMeshes,
            staticProjDrain: null)
    {
    }

    internal LandblockDisplayPipeline(
        LandblockRenderHerald renderPublisher,
        LandblockKineticsHerald physicsPublisher,
        LandblockStaticDisplayHerald staticPublisher,
        GpuRealmPhase state,
        LandblockDisplayRetirementOwner retirementOwner,
        Action<uint>? onLbFetched,
        Action<EnvironChamberLandblockAssemble>? secureEnvironChamberTriMeshes,
        IRenderStaticMirrorDiarySink? staticProjDrain)
    {
        _rasterizePublisher = renderPublisher
            ?? throw new ArgumentNullException(nameof(renderPublisher));
        _kineticsPublisher = physicsPublisher
            ?? throw new ArgumentNullException(nameof(physicsPublisher));
        _staticPublisher = staticPublisher
            ?? throw new ArgumentNullException(nameof(staticPublisher));
        _phase = state ?? throw new ArgumentNullException(nameof(state));
        if (!_rasterizePublisher.FitsPhase(_phase))
        {
            throw new ArgumentException(
                "The render publisher must own the pipeline's world state",
                nameof(state));
        }
        ArgumentNullException.ThrowIfNull(retirementOwner);
        if (!retirementOwner.Fits(
                _rasterizePublisher,
                _kineticsPublisher,
                _staticPublisher))
        {
            throw new ArgumentException(
                "The retirement owner must reference the pipeline's concrete publishers",
                nameof(retirementOwner));
        }
        _onLbFetched = onLbFetched;
        _secureEnvironChamberTriMeshes = secureEnvironChamberTriMeshes;
        _staticProjDrain = staticProjDrain;
        _retirements = LandblockRetirementMarshal.BuildBudgeted(
            state,
            retirementOwner.ProgressOne,
            retirementOwner.Advance,
            staticProjDrain is null
                ? null
                : staticProjDrain.Retire);
    }
}
