using System.Threading.Channels;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Paging;

public sealed partial class LandblockStreamer : IDisposable, ILandblockWrapUpOrigin
{
    public const int DefaultEmptyLotDims = 4;

    private readonly Func<LandblockBuildAsk, LandblockAssemble?> _pullLb;

    private readonly bool _supportsReqOrigin;

    private readonly Func<uint, MountedLandblock?, MacAC.Mechanics.Landscape.LandblockTessellationData?> _assembleTriMeshOrNull;

    private readonly Channel<LandblockFlowJob>[] _lanes;

    private readonly Channel<LandblockFlowOutcome> _outbox;

    private readonly CancellationTokenSource _abort = new();

    private readonly object _inboxLatch = new();

    private Thread[]? _workers;

    private int _engagedWorkers;

    private Exception? _workerMiss;

    private int _wrapUpBacklog;

    private int _destroyed;

    private readonly object _teardownLatch = new();

    private bool _teardownFinished;

    private LandblockStreamer(
        Func<LandblockBuildAsk, LandblockAssemble?> pullLb,
        Func<uint, MountedLandblock?, MacAC.Mechanics.Landscape.LandblockTessellationData?>? assembleTriMeshOrNull,
        bool supportsReqOrigin,
        int? workerCount)
    {
        if (workerCount is < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workerCount),
                workerCount,
                "The landblock build pool needs no fewer than one worker");
        }
        _pullLb = pullLb;
        _supportsReqOrigin = supportsReqOrigin;
        _assembleTriMeshOrNull = assembleTriMeshOrNull ?? ((_, _) => null);
        int lanes = workerCount ?? DefaultWorkerTally;
        _lanes = new Channel<LandblockFlowJob>[lanes];
        for (int idx = 0; idx < lanes; ++idx)
        {
            _lanes[idx] = Channel.CreateUnbounded<LandblockFlowJob>(
                new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        }
        _outbox = Channel.CreateUnbounded<LandblockFlowOutcome>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    }

    public LandblockStreamer(
        Func<uint, LandblockFlowJobFlavor, LandblockAssemble?> pullLb,
        Func<uint, MountedLandblock?, MacAC.Mechanics.Landscape.LandblockTessellationData?>? assembleTriMeshOrNull = null,
        int? workerTally = null)
        : this(
            req => pullLb(req.LandblockId, req.Kind) is { } assemble
                ? assemble
                : null,
            assembleTriMeshOrNull,
            supportsReqOrigin: false,
            workerTally)
    {
    }

    public LandblockStreamer(
        Func<uint, LandblockFlowJobFlavor, MountedLandblock?> pullLb,
        Func<uint, MountedLandblock?, MacAC.Mechanics.Landscape.LandblockTessellationData?>? assembleTriMeshOrNull = null,
        int? workerTally = null)
        : this(
            req => pullLb(req.LandblockId, req.Kind) is { } lb
                ? new LandblockAssemble(lb, Origin: req.Origin)
                : null,
            assembleTriMeshOrNull,
            supportsReqOrigin: false,
            workerTally)
    {
    }

    public LandblockStreamer(
        Func<uint, MountedLandblock?> pullLb,
        Func<uint, MountedLandblock?, MacAC.Mechanics.Landscape.LandblockTessellationData?>? assembleTriMeshOrNull = null,
        int? workerTally = null)
        : this(
            req => pullLb(req.LandblockId) is { } lb
                ? new LandblockAssemble(lb, Origin: req.Origin)
                : null,
            assembleTriMeshOrNull,
            supportsReqOrigin: false,
            workerTally)
    {
    }

    internal int WorkerTally => _lanes.Length;
}
