using System.Numerics;
using MacAC.Client.Controls;
using MacAC.Client.Graphics;
using MacAC.Client.Kinetics;
using MacAC.Client.Link;
using MacAC.Client.Pulse;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Mechanics.Realm.Cells;

namespace MacAC.Client.Paging;

internal interface IPagingOriginConvergence
{
    bool Advance();
}

internal interface IPagingFrameBackend
{
    void Tick(int watcherCx, int watcherCy, bool insideDungeon = false);
}

internal interface IOfflinePagingWatcherSource
{
    Vector3 Position { get; }
}

internal sealed class FlyCameraPagingWatcherSource(CameraDriver camera)
        : IOfflinePagingWatcherSource
{
    private readonly CameraDriver _cam = camera ?? throw new ArgumentNullException(nameof(camera));

    public Vector3 Position => _cam.Fly.Position;
}

internal readonly record struct PagingDungeonCellCapture(
    uint CellId,
    bool IsSealedDungeon);

internal interface IPagingDungeonCellSource
{
    PagingDungeonCellCapture Capture();
}

internal sealed class KineticsPagingDungeonCellSource(KineticEngine physics)
        : IPagingDungeonCellSource
{
    private readonly KineticEngine _physics = physics ?? throw new ArgumentNullException(nameof(physics));

    public PagingDungeonCellCapture Capture()
    {
        ObjRefChamber? chamber = _physics.DataCache?.ChamberGraph.CurrChamber;
        return new PagingDungeonCellCapture(
            chamber?.Id ?? 0u,
            chamber is EnvCell environChamber && !environChamber.SeenOutside);
    }
}

internal interface IOnlineMirrorSalvageRebucketter
{
    void RebucketAll(int watcherCx, int watcherCy);
}

internal sealed class OnlineMirrorSalvageRebucketter(
    GpuRealmPhase worldState,
    OnlineActorCore liveEntities)
        : IOnlineMirrorSalvageRebucketter
{
    private readonly GpuRealmPhase _realmPhase = worldState ?? throw new ArgumentNullException(nameof(worldState));
    private readonly OnlineActorCore _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));

    public void RebucketAll(int watcherCx, int watcherCy)
    {
        var rescued = _realmPhase.DrainRescued();
        if (rescued.Count is 0)
            return;

        uint middleLb =
            ((uint)watcherCx << 24) | ((uint)watcherCy << 16) | 0xFFFFu;
        foreach (RealmActor actor in rescued)
            _onlineActors.RebucketLiveEntity(actor.ServerGuid, middleLb);
    }
}

internal sealed class PagingFrameDriver(
    bool onlineMannerTurnedOn,
    IAvatarModeSource playerMode,
    ISimAvatarDriverSource playerController,
    IOnlineInRealmSource session,
    OnlineRealmOriginLedger origin,
    IAvatarLandblockSource playerLandblock,
    IOfflinePagingWatcherSource offlineObserver,
    IPagingDungeonCellSource dungeonCell,
    IPagingOriginConvergence originConvergence,
    IPagingFrameBackend streaming,
    IOnlineMirrorSalvageRebucketter rescues) : IPagingFramePhase
{
    private const float LbLen = 192f;

    private readonly bool _onlineMannerTurnedOn = onlineMannerTurnedOn;
    private readonly IAvatarModeSource _avatarManner = playerMode ?? throw new ArgumentNullException(nameof(playerMode));
    private readonly ISimAvatarDriverSource _playerController = playerController
            ?? throw new ArgumentNullException(nameof(playerController));
    private readonly IOnlineInRealmSource _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly OnlineRealmOriginLedger _origin = origin ?? throw new ArgumentNullException(nameof(origin));
    private readonly IAvatarLandblockSource _avatarLb = playerLandblock
            ?? throw new ArgumentNullException(nameof(playerLandblock));
    private readonly IOfflinePagingWatcherSource _offlineWatcher = offlineObserver
            ?? throw new ArgumentNullException(nameof(offlineObserver));
    private readonly IPagingDungeonCellSource _dungeonChamber = dungeonCell ?? throw new ArgumentNullException(nameof(dungeonCell));
    private readonly IPagingOriginConvergence _originConvergence = originConvergence
            ?? throw new ArgumentNullException(nameof(originConvergence));
    private readonly IPagingFrameBackend _paging = streaming ?? throw new ArgumentNullException(nameof(streaming));
    private readonly IOnlineMirrorSalvageRebucketter _rescues = rescues ?? throw new ArgumentNullException(nameof(rescues));

    public void Tick()
    {
        _originConvergence.Advance();

        bool onlineInRealm = _session.IsInWorld;
        if (!StreamReadyGate.ShouldFlow(
                _onlineMannerTurnedOn,
                _avatarManner.PursueMannerEverEntered,
                onlineInRealm,
                _origin.IsKnown))

            return;

        (int watcherCx, int watcherCy) = PickWatcher(onlineInRealm);
        var driver = _playerController.Controller;
        bool isWarpGrip = driver is { State: AvatarPhase.PortalSpace };
        var latestChamber = _dungeonChamber.Capture();
        var latch = DungeonPagingTurnstile.Compute(
            isWarpGrip,
            latestChamber.IsSealedDungeon,
            latestChamber.CellId);
        if (latch.ObserverLandblockKey is { } chamberLb)
        {
            watcherCx = (int)((chamberLb >> 8) & 0xFFu);
            watcherCy = (int)(chamberLb & 0xFFu);
        }

        _paging.Tick(watcherCx, watcherCy, latch.InsideDungeon);
        _rescues.RebucketAll(watcherCx, watcherCy);
    }

    private (int X, int Y) PickWatcher(bool onlineInRealm)
    {
        int watcherCx = _origin.CenterX;
        int watcherCy = _origin.CenterY;
        var driver = _playerController.Controller;

        if (_avatarManner.IsPlayerMode
            && driver is { State: AvatarPhase.PortalSpace })

            return (watcherCx, watcherCy);

        if (_avatarManner.IsPlayerMode && driver is not null)
        {
            Vector3 locus = driver.Position;
            return (
                watcherCx + (int)Math.Floor(locus.X / LbLen),
                watcherCy + (int)Math.Floor(locus.Y / LbLen));
        }

        if (onlineInRealm)
        {
            if (_avatarLb.PreviousRecognizedLbIdent is { } lbIdent)
            {
                return (
                    (int)((lbIdent >> 24) & 0xFFu),
                    (int)((lbIdent >> 16) & 0xFFu));
            }

            return (watcherCx, watcherCy);
        }

        Vector3 camLocus = _offlineWatcher.Position;
        return (
            watcherCx + (int)Math.Floor(camLocus.X / LbLen),
            watcherCy + (int)Math.Floor(camLocus.Y / LbLen));
    }
}
