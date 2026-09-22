using System.Diagnostics;
using MacAC.Client.Graphics.Batching;
using MacAC.Mechanics.Landscape;

namespace MacAC.Client.Paging;

public sealed partial class PagingDriver
    : IPagingFrameBackend,
      IRealmRevealPagingRota
{
    private readonly record struct DestReservation(
        long RevealGeneration,
        uint LandblockId,
        int Radius);

    private sealed class OriginRecenterSunset
    {
        public bool RadiiConverged;
        public bool GenAdvanced;
        public bool QueuedLoadsCleared;
        public bool WrapUpFifoCleared;
        public bool QueuedPublicationsCleared;
        public bool ZoneCleared;
        public bool SpatialGenDetached;
        public bool PrepSealed;
        public (int X, int Y, bool IsSealedDungeon)? Destination;
        public bool DestConfigured;
        public bool DestPullEnqueued;
    }

    private sealed class WholePaneSunset
    {
        public bool GenerationAdvanced;
        public bool PendingLoadsCleared;
        public bool WrapUpQueueCleared;
        public bool PendingPublicationsCleared;
        public bool RegionCleared;
        public List<uint>? HousedIdents;
        public IEnumerator<uint>? HousedEnumerator;
        public int SunsetCur;
        public bool PrepCommitted;
    }

    private readonly Action<uint, LandblockFlowJobFlavor, ulong> _queuePull;

    private readonly Action<uint, ulong> _queueUnload;

    private readonly ILandblockWrapUpOrigin _wrapUpSrc;

    private readonly Action? _wipeQueuedLoads;

    private readonly LandblockDisplayPipeline _exhibit;

    private readonly Func<LandblockFlowOutcome, bool>
        _isBulletinBlockedBySunset;

    private readonly PagingWorkAllowanceKnobs _configuredJobAllowanceKnobs;

    private PagingWorkAllowance _jobAllowance;

    private readonly Func<long> _jobStamp;

    private readonly long _jobStampFrequency;

    private PagingWorkMeter? _engagedJobGauge;

    private PagingWorkMeterCapture _previousJobGauge;

    private readonly PagingCompletionQueue _wrapUpFifo = new();

    private long _upcomingWrapUpSeries;
    private DestReservation? _destReservation;

    private long _lifespanJobOverruns;

    private long _lifespanOversizedHeadway;

    private double _ceilingJobCycleMillis;

    private string? _ceilingJobCycleJuncture;

    private double _ceilingJobOpMillis;

    private string? _ceilingJobOpJuncture;

    private readonly GpuRealmPhase _phase;

    private PagingRegion? _zone;

    private ClientRadiiReconfiguration? _queuedRadiiReconfiguration;

    private bool _advancingRadiiReconfiguration;

    private (int NearRadius, int FarRadius)? _postponedRadiiReq;

    private OriginRecenterSunset? _originRecenterSunset;

    private WholePaneSunset? _wholePaneSunset;

    private bool _advancingOriginRecenter;

    private ulong _gen;
    private uint _collapsedMiddle;

    internal PagingDriver(
        Action<uint, LandblockFlowJobFlavor, ulong> queuePull,
        Action<uint, ulong> queueUnload,
        Func<int, IReadOnlyList<LandblockFlowOutcome>> emptyCompletions,
        Action<LandblockAssemble, LandblockTessellationData> enactLand,
        GpuRealmPhase phase,
        int nearbyRadius,
        int farawayRadius,
        Action<uint>? dropLand = null,
        Action<uint>? demoteNearbyStratum = null,
        Action? wipeQueuedLoads = null,
        Action<uint>? onLbFetched = null,
        Action<EnvironChamberLandblockAssemble>? secureEnvironChamberTriMeshes = null,
        LandblockRetirementMarshal? sunsetCoordinator = null,
        PagingWorkAllowanceKnobs? jobAllowanceKnobs = null)
        : this(
            queuePull,
            queueUnload,
            emptyCompletions,
            phase,
            nearbyRadius,
            farawayRadius,
            new LandblockDisplayPipeline(
                enactLand,
                phase,
                onLbFetched,
                secureEnvironChamberTriMeshes,
                sunsetCoordinator,
                dropLand,
                demoteNearbyStratum),
            wipeQueuedLoads,
            jobAllowanceKnobs)
    {
    }

    internal PagingDriver(
        Action<uint, LandblockFlowJobFlavor, ulong> queuePull,
        Action<uint, ulong> queueUnload,
        Func<int, IReadOnlyList<LandblockFlowOutcome>> emptyCompletions,
        GpuRealmPhase phase,
        int nearbyRadius,
        int farawayRadius,
        LandblockDisplayPipeline exhibitPipe,
        Action? wipeQueuedLoads = null,
        PagingWorkAllowanceKnobs? jobAllowanceKnobs = null)
        : this(
            queuePull,
            queueUnload,
            new DelegateLandblockWrapUpOrigin(emptyCompletions),
            phase,
            nearbyRadius,
            farawayRadius,
            exhibitPipe,
            wipeQueuedLoads,
            jobAllowanceKnobs)
    {
    }

    public PagingDriver(
        Action<uint, LandblockFlowJobFlavor, ulong> queuePull,
        Action<uint, ulong> queueUnload,
        ILandblockWrapUpOrigin wrapUpSrc,
        GpuRealmPhase phase,
        int nearbyRadius,
        int farawayRadius,
        LandblockDisplayPipeline exhibitPipe,
        Action? wipeQueuedLoads = null,
        PagingWorkAllowanceKnobs? jobAllowanceKnobs = null)
        : this(
            queuePull,
            queueUnload,
            wrapUpSrc,
            phase,
            nearbyRadius,
            farawayRadius,
            exhibitPipe,
            Stopwatch.GetTimestamp,
            Stopwatch.Frequency,
            wipeQueuedLoads,
            jobAllowanceKnobs)
    {
    }

    internal PagingDriver(
        Action<uint, LandblockFlowJobFlavor, ulong> queuePull,
        Action<uint, ulong> queueUnload,
        ILandblockWrapUpOrigin completionSource,
        GpuRealmPhase phase,
        int nearbyRadius,
        int farawayRadius,
        LandblockDisplayPipeline presentationPipeline,
        Func<long> workTimestamp,
        long workTimestampFrequency,
        Action? wipeQueuedLoads = null,
        PagingWorkAllowanceKnobs? jobAllowanceKnobs = null)
    {
        _queuePull = queuePull;
        _queueUnload = queueUnload;
        _wrapUpSrc = completionSource
            ?? throw new ArgumentNullException(nameof(completionSource));
        _wipeQueuedLoads = wipeQueuedLoads;
        _phase = phase;
        _exhibit = presentationPipeline
            ?? throw new ArgumentNullException(nameof(presentationPipeline));
        _isBulletinBlockedBySunset =
            IsBulletinBlockedBySunset;
        _configuredJobAllowanceKnobs =
            jobAllowanceKnobs ?? PagingWorkAllowanceKnobs.Default;
        _jobAllowance = _configuredJobAllowanceKnobs.ToAllowance();
        _jobStamp = workTimestamp
            ?? throw new ArgumentNullException(nameof(workTimestamp));
        if (workTimestampFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(workTimestampFrequency));
        _jobStampFrequency = workTimestampFrequency;
        if (!_exhibit.FitsPhase(_phase))
        {
            throw new ArgumentException(
                "The presentation pipeline must own the controller's world state",
                nameof(presentationPipeline));
        }
        NearRadius = nearbyRadius;
        FarRadius = farawayRadius;
    }

    internal PagingDriver(
        Action<uint, LandblockFlowJobFlavor> queuePull,
        Action<uint> queueUnload,
        Func<int, IReadOnlyList<LandblockFlowOutcome>> emptyCompletions,
        Action<LandblockAssemble, LandblockTessellationData> enactLand,
        GpuRealmPhase phase,
        int nearbyRadius,
        int farawayRadius,
        Action<uint>? dropLand = null,
        Action<uint>? demoteNearbyStratum = null,
        Action? wipeQueuedLoads = null,
        Action<uint>? onLbFetched = null,
        Action<EnvironChamberLandblockAssemble>? secureEnvironChamberTriMeshes = null,
        LandblockRetirementMarshal? sunsetCoordinator = null,
        PagingWorkAllowanceKnobs? jobAllowanceKnobs = null)
        : this(
            (ident, sort, _) => queuePull(ident, sort),
            (ident, _) => queueUnload(ident),
            emptyCompletions,
            enactLand,
            phase,
            nearbyRadius,
            farawayRadius,
            dropLand,
            demoteNearbyStratum,
            wipeQueuedLoads,
            onLbFetched,
            secureEnvironChamberTriMeshes,
            sunsetCoordinator,
            jobAllowanceKnobs)
    {
    }

    private void QueuePull(uint ident, LandblockFlowJobFlavor sort) =>
        _queuePull(ident, sort, _gen);

    private sealed class ClientRadiiReconfiguration(
        int nearbyRadius,
        int farawayRadius,
        PagingRegion zone,
        List<PagingDriver.RadiiAlteration> alterations)
    {
        public int NearRadius { get; } = nearbyRadius;
        public int FarRadius { get; } = farawayRadius;
        public PagingRegion Region { get; } = zone;
        public List<RadiiAlteration> Alterations { get; } = alterations;
        public bool GenerationAdvanced { get; set; }
        public bool PendingLoadsCleared { get; set; }
    }

    private sealed class RadiiAlteration(Action enact)
    {
        public Action Apply { get; } = enact;
        public bool Completed { get; set; }
    }
}
