using System.Runtime.CompilerServices;
using MacAC.Client.Controls;
using MacAC.Client.Graphics.Stage.Arch;
using MacAC.Client.Realm;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Stage;

internal readonly record struct RenderStageShadeComparisonCapture(
    bool Enabled,
    ulong RenderFrameSequence,
    ulong ComparisonCount,
    ulong SuccessfulComparisonCount,
    ulong ComparedOracleFrameSequence,
    int MatchedProjectionCount,
    long MismatchCount,
    string? FirstMismatch,
    int PendingDeltaCount,
    ulong DrainSequence,
    RenderDiffApplyResult LastApply,
    RenderDiffApplyResult CumulativeApply,
    RenderMirrorCounts SceneCounts,
    RenderStageIndexCounts SceneIndexCounts,
    RenderStageMemoryAccounting Memory,
    RenderStageDigest Digest)
{
    public static RenderStageShadeComparisonCapture Disabled { get; } =
        new(
            Enabled: false,
            RenderFrameSequence: 0,
            ComparisonCount: 0,
            SuccessfulComparisonCount: 0,
            ComparedOracleFrameSequence: 0,
            MatchedProjectionCount: 0,
            MismatchCount: 0,
            FirstMismatch: null,
            PendingDeltaCount: 0,
            DrainSequence: 0,
            LastApply: default,
            CumulativeApply: default,
            SceneCounts: default,
            SceneIndexCounts: default,
            Memory: default,
            Digest: default);
}

internal interface IRenderStageShadeCaptureSource
{
    RenderStageShadeComparisonCapture GrabCheckpointCapture();
}

internal sealed class RenderStageShadeEngine : IDisposable
{
    private readonly ArchRenderStage _tableau;
    private readonly RenderStageDigestBuffer _digestBuf = new();
    private OnlineRenderMirrorDiary? _online;
    private bool _destroyed;

    public RenderStageShadeEngine(RenderStageEpoch gen)
    {
        _tableau = new ArchRenderStage(gen);
        _journal = new RenderMirrorDiary(gen);
        StaticProjections = new StaticRenderMirrorDiary(_journal);
    }

    public StaticRenderMirrorDiary StaticProjections { get; }

    public IOnlineRenderMirrorSink? OnlineProjections => _online;

    public int QueuedDiffTally => _journal.Count;

    public ulong EmptySeries { get; private set; }

    public RenderDiffApplyResult PreviousEnact { get; private set; }

    public RenderDiffApplyResult CumulativeEnact { get; private set; }

    public RenderMirrorCounts Counts => _tableau.Counts;

    private readonly RenderMirrorDiary _journal;

    internal RenderMirrorDiary Journal => _journal;
    internal RenderStageProbe Ask => _tableau.OpenAsk();

    public OnlineRenderMirrorDiary AttachOnlineCore(
        OnlineActorCore core,
        IRasterizeTraversalOrderingOrigin traversalOrdering,
        IAvatarIdentitySource? ownAvatar = null)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(traversalOrdering);
        if (_online is not null)
        {
            throw new InvalidOperationException(
                "The shadow render scene is by now bound to a live runtime");
        }

        _online = new OnlineRenderMirrorDiary(
            core,
            _journal,
            traversalOrdering,
            ownAvatar);
        return _online;
    }

    public void OnLiveAssetRegistered(RealmActor actor)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentNullException.ThrowIfNull(actor);
    }

    public void OnLiveAssetUnregistered(RealmActor actor)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentNullException.ThrowIfNull(actor);
        (_online ?? throw new InvalidOperationException(
            "The shadow render scene wasn't bound prior to live teardown"))
            .OnAssetWithdraw(actor);
    }

    public RenderDiffApplyResult EmptyRefreshBoundary()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        PreviousEnact = _journal.EmptyTo(_tableau);
        CumulativeEnact = Add(CumulativeEnact, PreviousEnact);
        EmptySeries = checked(EmptySeries + 1);
        return PreviousEnact;
    }

    public RenderStageMemoryAccounting GrabMemory()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        var memory = _tableau.Memory;
        return memory with
        {
            EstimatedJournalBufferBytes = checked(
                (long)_journal.Capacity
                * Unsafe.SizeOf<RenderMirrorDiff>()),
            EstimatedSynchronizationSourceBytes = checked(
                (long)(StaticProjections.ProjCount
                    + (_online?.ProjTally ?? 0))
                * Unsafe.SizeOf<RenderMirrorRecord>()),
        };
    }

    public RenderStageDigest ConstructDigest()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        return _tableau.AssembleDigest(_digestBuf);
    }

    public void PurgeStale()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        _tableau.WipeStale();
    }

    public void Clear(RenderStageEpoch substituteGen)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        _online?.RestartTracking();
        StaticProjections.Clear(substituteGen);
        _tableau.Clear(substituteGen);
        PreviousEnact = default;
        CumulativeEnact = default;
        EmptySeries = 0;
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _online?.RestartTracking();
        _tableau.Dispose();
    }

    private static RenderDiffApplyResult Add(
        RenderDiffApplyResult left,
        RenderDiffApplyResult right)
    {
        return new(
            Applied: checked(left.Applied + right.Applied),
            Registered: checked(left.Registered + right.Registered),
            Updated: checked(left.Updated + right.Updated),
            Replaced: checked(left.Replaced + right.Replaced),
            Unregistered: checked(left.Unregistered + right.Unregistered),
            RejectedGeneration: checked(
                left.RejectedGeneration + right.RejectedGeneration),
            RejectedOutOfOrderSequence: checked(
                left.RejectedOutOfOrderSequence
                + right.RejectedOutOfOrderSequence),
            RejectedStaleIncarnation: checked(
                left.RejectedStaleIncarnation
                + right.RejectedStaleIncarnation),
            RejectedMissing: checked(
                left.RejectedMissing + right.RejectedMissing));
    }
}

internal sealed partial class RenderStageShadeComparisonDriver :
    IRenderFramePostTelemetryPhase,
    IRenderStageShadeCaptureSource
{
    private const ulong ComparisonCadenceCycles = 30;

    private const byte StaticProjDomain = 1;

    private const byte EnvironChamberProjDomain = 2;

    private const byte OnlineProjDomain = 3;

    private readonly RenderStageShadeEngine _shade;

    private readonly CurrentRenderStageOracle _oracle;

    private readonly Action<string>? _trace;

    private readonly bool _acknowledgeStale;

    private readonly List<RenderMirrorRecord> _tableauRecords = [];

    private readonly HashSet<RenderMirrorId> _anticipatedIdents = [];

    private ulong _rasterizeCycleSeries;

    private ulong _comparisonTally;

    private ulong _successfulComparisonTally;

    private long _mismatchTally;

    private string? _leadMismatch;

    private int _matchedProjTally;

    private ulong _comparedOracleCycleSeries;

    public RenderStageShadeComparisonDriver(
        RenderStageShadeEngine shadow,
        CurrentRenderStageOracle oracle,
        Action<string>? trace = null,
        bool acknowledgeStale = true)
    {
        _shade = shadow ?? throw new ArgumentNullException(nameof(shadow));
        _oracle = oracle ?? throw new ArgumentNullException(nameof(oracle));
        _trace = trace;
        _acknowledgeStale = acknowledgeStale;
        Snapshot = AssembleCapture();
    }

    public void Process(RasterizeCycleFeed feed, RenderFrameVerdict verdict)
    {
        _rasterizeCycleSeries = checked(_rasterizeCycleSeries + 1);
        if (verdict.World.NormalWorldDrawn
            && _rasterizeCycleSeries % ComparisonCadenceCycles is 0)
        {
            Contrast(force: false);
        }
        else
        {
            Snapshot = AssembleCapture();
        }
    }

    public RenderStageShadeComparisonCapture Snapshot { get; private set; }

    public RenderStageShadeComparisonCapture GrabCheckpointCapture()
    {
        Contrast(force: true);
        return Snapshot;
    }

    private static string Values<T>(
        string lane,
        RenderMirrorId ident,
        string field,
        T anticipated,
        T actual) =>
        Stem(lane, ident, field, $"{anticipated}/{actual}");

    private RenderStageShadeComparisonCapture AssembleCapture()
    {
        var ask = _shade.Ask;
        return new RenderStageShadeComparisonCapture(
            Enabled: true,
            RenderFrameSequence: _rasterizeCycleSeries,
            ComparisonCount: _comparisonTally,
            SuccessfulComparisonCount: _successfulComparisonTally,
            ComparedOracleFrameSequence: _comparedOracleCycleSeries,
            MatchedProjectionCount: _matchedProjTally,
            MismatchCount: _mismatchTally,
            FirstMismatch: _leadMismatch,
            PendingDeltaCount: _shade.QueuedDiffTally,
            DrainSequence: _shade.EmptySeries,
            LastApply: _shade.PreviousEnact,
            CumulativeApply: _shade.CumulativeEnact,
            SceneCounts: ask.Counts,
            SceneIndexCounts: ask.OrdinalCounts,
            Memory: _shade.GrabMemory(),
            Digest: _shade.ConstructDigest());
    }

    private static RenderMirrorId AnticipatedIdent(
        in CurrentRenderMirrorFingerprint fingerprint)
    {
        return fingerprint.ServerGuid is 0
            ? StaticRenderMirrorDiary.StaticActorIdent(
                fingerprint.LandblockId,
                fingerprint.EntityId)
            : OnlineRenderMirrorDiary.ProjIdent(
                fingerprint.EntityId);
    }

    private static byte Domain(RenderMirrorId ident) =>
        ident.Domain;

    private static string Channel(RenderMirrorId ident)
    {
        return Domain(ident) switch
        {
            StaticProjDomain => "static",
            EnvironChamberProjDomain => "envCell",
            OnlineProjDomain => "live",
            byte domain => $"unknown:{domain}",
        };
    }

    private static string Stem(
        string lane,
        RenderMirrorId ident,
        string field,
        string vals)
    {
        return $"sourceChannel={lane} id={ident} field={field} "
        + $"expected/actual={vals}";
    }
}
