using System.Numerics;
using MacAC.Client.Controls;
using MacAC.Client.Graphics;
using MacAC.Client.Link;
using MacAC.Client.Pulse;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Mechanics.Sound;
using MacAC.Sim;
using MacAC.Sim.Presence;
using MacAC.Sim.Realm;
using MacAC.Wire.Messages;

namespace MacAC.Client.Paging;

internal interface IAvatarWarpWireSink
{
    void OnWarpBegun(uint series);

    void OfferDest(
        SimWarpDestination dest,
        bool warpStampAdvanced);

    void OnOwnAvatarLeadListingFinished();

    void ArmSigninTunnel();

    void ReqSignout();

    void ResetSession();

    void RestartGenExhibit();
}

internal sealed class DeferredAvatarWarpWireSink
    : IAvatarWarpWireSink
{
    private IAvatarWarpWireSink? _interior;

    public void Bind(IAvatarWarpWireSink interior)
    {
        ArgumentNullException.ThrowIfNull(interior);
        if (Interlocked.CompareExchange(ref _interior, interior, null) is not null)
            throw new InvalidOperationException("The local teleport sink is by now bound");
    }

    public IDisposable BindOwned(IAvatarWarpWireSink interior)
    {
        Bind(interior);
        return new Binding(this, interior);
    }

    public void OnWarpBegun(uint series) => Required().OnWarpBegun(series);

    public void OfferDest(
        SimWarpDestination dest,
        bool warpStampAdvanced) =>
        Required().OfferDest(dest, warpStampAdvanced);

    public void OnOwnAvatarLeadListingFinished() =>
        Required().OnOwnAvatarLeadListingFinished();

    public void ArmSigninTunnel() => Required().ArmSigninTunnel();

    public void ReqSignout() => Required().ReqSignout();

    public void ResetSession() => Required().ResetSession();

    public void RestartGenExhibit() =>
        Required().RestartGenExhibit();

    private void Loosen(IAvatarWarpWireSink anticipated) => _ = Interlocked.CompareExchange(ref _interior, null, anticipated);

    private IAvatarWarpWireSink Required()
    {
        return _interior ?? throw new InvalidOperationException(
            "The local teleport sink was used prior to composition completed");
    }

    private sealed class Binding(
        DeferredAvatarWarpWireSink holder,
        IAvatarWarpWireSink anticipated) : IDisposable
    {
        private DeferredAvatarWarpWireSink? _holder = holder;
        private readonly IAvatarWarpWireSink _anticipated = anticipated;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Loosen(_anticipated);
    }
}

internal interface IAvatarWarpInputLifetime
{
    void EndMouseLook();
}

internal interface IAvatarWarpModeOperations
{
    AvatarLocomotionDriver? Driver { get; }
    Matrix4x4 Projection { get; }
    bool TryJoinGatewaySpace();

    bool TryJoinGatewaySpaceForSignin();
    void EnterWorld();
}

internal interface IAvatarWarpAuthority
{
    bool IsFreshBegin(ushort series);
}

internal interface IAvatarSignInLifespanSource
{
    SimToonPickLifespan PickLifecycle { get; }
}

internal sealed class EngineSignInLifespanSource(SimCore runtime)
        : IAvatarSignInLifespanSource
{
    private readonly SimCore _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public SimToonPickLifespan PickLifecycle =>
        _runtime.CharacterSelection.Snapshot.Lifecycle;
}

internal interface IAvatarLogoutOperations
{
    bool IsOwnAvatarKiller { get; }

    bool CommenceToonLogOff();

    // The server's opcode-only 0xF653 echo has landed
    bool IsToonLogOffConfirmed { get; }

    bool ConcludeToonLogOff();
}

internal sealed class EngineAvatarLogoutOperations(
    SimCore runtime,
    SimAvatarLocomotionLedger movement,
    IOnlineRealmSessionSource session,
    MacAC.Mechanics.Gear.ClientThingChart objects,
    IAvatarIdentitySource identity)
        : IAvatarLogoutOperations
{
    private readonly SimCore _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    private readonly SimAvatarLocomotionLedger _movement = movement ?? throw new ArgumentNullException(nameof(movement));
    private readonly IOnlineRealmSessionSource _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly MacAC.Mechanics.Gear.ClientThingChart _objects = objects ?? throw new ArgumentNullException(nameof(objects));
    private readonly IAvatarIdentitySource _identity = identity ?? throw new ArgumentNullException(nameof(identity));

    public bool IsOwnAvatarKiller
    {
        get
        {
            var bitfield = (MacAC.Mechanics.Gear.PublicWeenieBits)(_objects.Get(_identity.SrvOid)
                ?.PublicWeenieBitfield ?? 0u);
            return (bitfield & MacAC.Mechanics.Gear.PublicWeenieBits.PlayerKiller) != 0
                || (bitfield & MacAC.Mechanics.Gear.PublicWeenieBits.PlayerKillerLite) != 0;
        }
    }

    public bool CommenceToonLogOff()
    {
        if (!_runtime.Session.OpenToonLogOff(_runtime.Generation)
                .Accepted)

            return false;

        _runtime.CommunicationHolder.AddText(
            "Logging off...",
            MacAC.Mechanics.Comms.CanonLogTextType.Default);
        _movement.DeactivateDirectiveInterpreter();
        return true;
    }

    public bool IsToonLogOffConfirmed =>
        _session.LatestSess?.IsToonLogOffConfirmed == true;

    public bool ConcludeToonLogOff()
    {
        return _runtime.Session.FinishToonLogOff(_runtime.Generation)
            .Accepted;
    }
}

internal sealed class OnlineAvatarWarpAuthority(
    OnlineActorCore liveEntities,
    IAvatarIdentitySource identity)
        : IAvatarWarpAuthority
{
    private readonly OnlineActorCore _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
    private readonly IAvatarIdentitySource _identity = identity ?? throw new ArgumentNullException(nameof(identity));

    public bool IsFreshBegin(ushort series) =>
        _onlineActors.IsFreshWarpBegin(_identity.SrvOid, series);
}

internal interface IAvatarWarpPagingOperations
{
    int MiddleX { get; }
    int MiddleY { get; }
    bool IsRecenterQueued { get; }
    bool CommenceRecenter(int x, int y, bool isSealedDungeon);
    bool RestartRecenter(bool sessEnding);
    bool IsSealedDungeon(uint chamberIdent);
}

internal sealed class AvatarWarpPagingOperations(
    OnlineRealmOriginLedger origin,
    PagingOriginRecenterMarshal recenter,
    PagingDriver streaming,
    ISealedDungeonChamberClassifier sealedDungeonCells)
        : IAvatarWarpPagingOperations
{
    private readonly OnlineRealmOriginLedger _origin = origin ?? throw new ArgumentNullException(nameof(origin));
    private readonly PagingOriginRecenterMarshal _recenter = recenter ?? throw new ArgumentNullException(nameof(recenter));
    private readonly PagingDriver _paging = streaming ?? throw new ArgumentNullException(nameof(streaming));
    private readonly ISealedDungeonChamberClassifier _sealedDungeonChambers = sealedDungeonCells
            ?? throw new ArgumentNullException(nameof(sealedDungeonCells));

    public int MiddleX => _origin.CenterX;
    public int MiddleY => _origin.CenterY;
    public bool IsRecenterQueued => _recenter.IsPending;

    public bool CommenceRecenter(int x, int y, bool isSealedDungeon) =>
        _recenter.Begin(x, y, isSealedDungeon);

    public bool RestartRecenter(bool sessEnding) =>
        _recenter.Reset(sessEnding);

    public bool IsSealedDungeon(uint chamberIdent) =>
        _sealedDungeonChambers.IsSealedDungeon(chamberIdent);

}

internal interface IAvatarWarpPlacement
{
    void Place(Quaternion spin);
}

internal sealed class AvatarWarpPlacement(
    OnlineActorCore liveEntities,
    IAvatarIdentitySource identity,
    ISimAvatarDriverSource controller,
    IAvatarKineticsHostSource host,
    FollowCameraInputLedger cameras,
    IOnlineSpatialReconcilePhase spatial) : IAvatarWarpPlacement
{
    private readonly OnlineActorCore _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
    private readonly IAvatarIdentitySource _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    private readonly ISimAvatarDriverSource _driver = controller ?? throw new ArgumentNullException(nameof(controller));
    private readonly IAvatarKineticsHostSource _hub = host ?? throw new ArgumentNullException(nameof(host));
    private readonly FollowCameraInputLedger _cameras = cameras ?? throw new ArgumentNullException(nameof(cameras));
    private readonly IOnlineSpatialReconcilePhase _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));

    public void Place(Quaternion spin)
    {
        AvatarLocomotionDriver driver = _driver.Controller
            ?? throw new InvalidOperationException(
                "Teleport Place ran without the local player controller");

        uint avatarOid = _identity.SrvOid;
        if (_onlineActors.TryFetchRealmActor(
                avatarOid,
                out RealmActor? actor))
        {
            actor.SetPosition(driver.Position);
            actor.ParentCellId = driver.CellId;
            actor.Rotation = spin;

            if (!_onlineActors.RebucketLiveEntity(avatarOid, driver.CellId))
            {
                throw new InvalidOperationException(
                    $"Teleport Place could not commit local player 0x{avatarOid:X8} "
                    + $"to destination cell 0x{driver.CellId:X8}.");
            }
        }

        _hub.Host?.AlertTeleported();

        _cameras.Legacy?.Update(driver.Position, driver.Yaw);
        _cameras.Retail?.RestartBeholderToAvatar(driver.Position, driver.Yaw);
        _spatial.Reconcile();

        KineticTelemetry.TraceWarp(
            "PLACED",
            driver.CellId,
            "readiness=complete");
        Console.WriteLine(
            $"live: teleport materialized - snapped to {driver.Position} "
            + $"cell=0x{driver.CellId:X8}");
    }
}

internal interface IAvatarWarpSession
{
    void TransmitSigninDone();
}

internal sealed class AvatarWarpSession(IOnlineRealmSessionSource session) : IAvatarWarpSession
{
    private readonly IOnlineRealmSessionSource _session = session ?? throw new ArgumentNullException(nameof(session));

    public void TransmitSigninDone()
    {
        _session.LatestSess?.TransmitPlayAct(
            LoginCompleteAction.Build());
    }
}

internal interface IAvatarWarpDisplay : IDisposable
{
    bool IsGatewayViewportVisible { get; }
    int LatestTunnelCycle { get; }
    void Begin(Matrix4x4 proj);

    void CommenceSignout(Matrix4x4 proj);
    (PortalAnimFrame Snapshot, IReadOnlyList<PortalAnimEvent> Events)
        Tick(float diffSecs, bool realmPrimed);
    void PulseTunnel(float diffSecs);
    void PlayJoinCue();
    void PlayQuitCue();
    void EnterTunnel();
    void QuitTunnel();
    void ApplyPauseCue(bool shown);
    void Reset();
    Matrix4x4 ApplyViewPlane(Matrix4x4 proj);
    IClientCamera ApplyViewPlane(IClientCamera cam);
    void PaintGatewayViewRect(int width, int height, Matrix4x4 proj);
}

internal sealed class AvatarWarpDisplay(PortalTunnelDisplay tunnel)
        : IAvatarWarpDisplay
{
    private readonly WarpAnimScheduler _anim = new();
    private readonly WarpViewPlaneDriver _lensPlane = new();
    private readonly PortalTunnelDisplay _tunnel = tunnel ?? throw new ArgumentNullException(nameof(tunnel));

    public bool IsGatewayViewportVisible => _tunnel.IsVisible;
    public int LatestTunnelCycle => _tunnel.LatestAnimCycle;

    public void Begin(Matrix4x4 proj)
    {
        _lensPlane.Begin(proj);
        _anim.Begin(PortalEntryKind.Portal);
    }

    public void CommenceSignout(Matrix4x4 proj)
    {
        _lensPlane.Begin(proj);
        _anim.Begin(PortalEntryKind.Logout);
    }

    public (PortalAnimFrame Snapshot, IReadOnlyList<PortalAnimEvent> Events)
        Tick(float diffSecs, bool realmPrimed)
    {
        var (capture, signals) = _anim.Tick(
            diffSecs,
            realmPrimed,
            LatestTunnelCycle,
            gripInTunnel: PagingTelemetry.TunnelFreezeCycle.HasValue);

        if (!capture.ShowTunnel && _tunnel.IsVisible)
            _tunnel.Exit();

        _lensPlane.Update(capture);
        return (capture, signals);
    }

    public void PulseTunnel(float diffSecs) => _tunnel.Tick(diffSecs);

    public Action<SfxId>? WidgetSfxDrain { get; set; }

    public void PlayJoinCue() => WidgetSfxDrain?.Invoke(SfxId.UI_EnterPortal);

    public void PlayQuitCue() => WidgetSfxDrain?.Invoke(SfxId.UI_ExitPortal);

    public void EnterTunnel() => _tunnel.Enter();

    public void QuitTunnel() => _tunnel.Exit();
    public void ApplyPauseCue(bool shown) => _tunnel.AssignPauseCue(shown);

    public void Reset()
    {
        _anim.Reset();
        _lensPlane.Reset();
        _tunnel.Exit();
    }

    public Matrix4x4 ApplyViewPlane(Matrix4x4 proj) =>
        _lensPlane.Apply(proj);

    public IClientCamera ApplyViewPlane(IClientCamera cam) => _lensPlane.ImposeTo(cam);

    public void PaintGatewayViewRect(int width, int height, Matrix4x4 proj) =>
        _tunnel.Draw(width, height, ApplyViewPlane(proj));

    public void Dispose() => _tunnel.Dispose();
}

internal sealed partial class AvatarWarpDriver(
    IAvatarWarpAuthority authority,
    IAvatarWarpInputLifetime input,
    IAvatarWarpModeOperations mode,
    IAvatarWarpPagingOperations streaming,
    SimRealmCrossingLedger transit,
    RealmRevealMarshal worldReveal,
    IAvatarWarpPlacement placement,
    IAvatarWarpSession session,
    IAvatarWarpDisplay presentation,
    SimGrantedPositionPilot acceptedPositionDrive,
    IAvatarSignInLifespanSource loginLifecycle,
    IAvatarLogoutOperations logout)
        : IAvatarWarpFramePhase,
      IAvatarWarpWireSink,
      MacAC.Client.Dealing.IPickingViewPlaneSource,
      IDisposable
{
    private readonly SimRealmCrossingLedger _passage = transit ?? throw new ArgumentNullException(nameof(transit));

    private readonly IAvatarWarpAuthority _arbiter = authority ?? throw new ArgumentNullException(nameof(authority));

    private readonly IAvatarWarpInputLifetime _feed = input ?? throw new ArgumentNullException(nameof(input));

    private readonly IAvatarWarpModeOperations _mode = mode ?? throw new ArgumentNullException(nameof(mode));

    private readonly IAvatarWarpPagingOperations _paging = streaming ?? throw new ArgumentNullException(nameof(streaming));

    private readonly RealmRevealMarshal _worldReveal = worldReveal ?? throw new ArgumentNullException(nameof(worldReveal));

    private readonly IAvatarWarpPlacement _stance = placement ?? throw new ArgumentNullException(nameof(placement));

    private readonly IAvatarWarpSession _session = session ?? throw new ArgumentNullException(nameof(session));

    private readonly IAvatarWarpDisplay _exhibit = presentation ?? throw new ArgumentNullException(nameof(presentation));

    private readonly SimGrantedPositionPilot _approvedLocusSteer = acceptedPositionDrive
            ?? throw new ArgumentNullException(nameof(acceptedPositionDrive));

    private uint _queuedChamber;

    private Quaternion _queuedSpin = Quaternion.Identity;

    private long _queuedUnveilGen;

    private SimWarpDestination _queuedDest;

    private bool _hasQueuedDest;

    private bool _stanceSealed;

    private bool _expectingPostponedWake;

    private float _gripSecs;

    private long _lifespanGen;

    private bool _destroyed;

    private long _signinUnveilGen;

    private bool _signinExhibitEngaged;

    private bool _signinTunnelLoaded;

    private bool _signinStanceFinished;

    private float _signinGripSecs;

    private bool _signinMannerEntered;

    private readonly IAvatarSignInLifespanSource _signinLifecycle = loginLifecycle
            ?? throw new ArgumentNullException(nameof(loginLifecycle));

    private readonly IAvatarLogoutOperations _signout = logout ?? throw new ArgumentNullException(nameof(logout));

    private bool _signoutPagingSunsetReadied;
}
