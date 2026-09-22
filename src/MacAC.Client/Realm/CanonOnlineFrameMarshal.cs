using MacAC.Client.Link;
using MacAC.Client.Paging;
using MacAC.Client.Pulse;
using MacAC.Sim.Presence;

namespace MacAC.Client.Realm;

internal sealed class CanonOnlineFrameMarshal(
    IOnlineObjectFramePhase objects,
    GpuRealmPhase worldState,
    ISimOnlineSessionFramePhase session,
    IPostWireDirectiveFramePhase localPlayer,
    IOnlineSpatialReconcilePhase spatialReconciler,
    IRealmEpochAvailability? readiness = null,
    IRenderMirrorSyncPhase? rasterizeProjSynchronize = null,
    IEnginePlacementMirrorRetryPhase? stanceProjReattempt = null) : ICanonOnlineFramePhase
{
    private readonly IOnlineObjectFramePhase _objects = objects ?? throw new ArgumentNullException(nameof(objects));
    private readonly GpuRealmPhase _realmPhase = worldState ?? throw new ArgumentNullException(nameof(worldState));
    private readonly ISimOnlineSessionFramePhase _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly IPostWireDirectiveFramePhase _ownAvatar = localPlayer ?? throw new ArgumentNullException(nameof(localPlayer));
    private readonly IOnlineSpatialReconcilePhase _spatialReconciler = spatialReconciler
            ?? throw new ArgumentNullException(nameof(spatialReconciler));
    private readonly IRealmEpochAvailability _readiness = readiness ?? AlwaysAvailableRealmEpoch.Instance;
    private readonly IRenderMirrorSyncPhase? _rasterizeProjSynchronize = rasterizeProjSynchronize;
    private readonly IEnginePlacementMirrorRetryPhase?
        _stanceProjReattempt = stanceProjReattempt;

    public void Tick(float diffSecs)
    {
        float cycleDiff = (float)LevelDiffSecs(diffSecs);
        if (_readiness.IsRealmOnHand)
            _objects.Tick(cycleDiff);
        using (_realmPhase.CommenceAlterationLot())
        {
            _session.Tick();
            _stanceProjReattempt?.ReattemptPending();
        }
        _ownAvatar.ExecutePostNetworkDirectiveStage();
        if (_readiness.IsRealmOnHand)
            _spatialReconciler.Reconcile();
        else
            _rasterizeProjSynchronize?.SynchronizeEngagedSrcs();
    }

    public static double LevelDiffSecs(double diffSecs) =>
        PulseFrameClock.StandardizeDiffSecs(diffSecs);
}
