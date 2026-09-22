using MacAC.Client.Graphics;
using MacAC.Client.Link;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Controls;

internal interface IAvatarDisplayEngine
{
    bool CanPresentAvatar { get; }
    AvatarLocomotionDriver? Controller { get; }
}

internal interface IAvatarFrameEngine :
    IAvatarDisplayEngine,
    ISimAvatarFrameHarbor
{
    bool ISimAvatarFrameHarbor.CanAdvancePlayer =>
        CanPresentAvatar;
}

internal sealed class OnlineAvatarFrameEngine(
    CameraDriver camera,
    IAvatarModeSource mode,
    ISimAvatarDriverSource controller,
    IFollowCameraSource chase,
    RouterLocomotionInputSource input,
    IFeedGrabOrigin capture,
    OnlineActorCore liveEntities,
    IAvatarIdentitySource identity,
    IAvatarKineticsHostSource physicsHost,
    AvatarMirrorDriver projection,
    AvatarOutboundDriver outbound,
    IOnlineRealmSessionSource session) : IAvatarFrameEngine
{
    private readonly CameraDriver _cam = camera ?? throw new ArgumentNullException(nameof(camera));
    private readonly IAvatarModeSource _mode = mode ?? throw new ArgumentNullException(nameof(mode));
    private readonly ISimAvatarDriverSource _driver = controller ?? throw new ArgumentNullException(nameof(controller));
    private readonly IFollowCameraSource _pursue = chase ?? throw new ArgumentNullException(nameof(chase));
    private readonly RouterLocomotionInputSource _feed = input ?? throw new ArgumentNullException(nameof(input));
    private readonly IFeedGrabOrigin _grab = capture ?? throw new ArgumentNullException(nameof(capture));
    private readonly OnlineActorCore _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
    private readonly IAvatarIdentitySource _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    private readonly IAvatarKineticsHostSource _kineticsHub = physicsHost ?? throw new ArgumentNullException(nameof(physicsHost));
    private readonly AvatarMirrorDriver _proj = projection ?? throw new ArgumentNullException(nameof(projection));
    private readonly AvatarOutboundDriver _outgoing = outbound ?? throw new ArgumentNullException(nameof(outbound));
    private readonly IOnlineRealmSessionSource _session = session ?? throw new ArgumentNullException(nameof(session));

    public bool CanPresentAvatar
    {
        get
        {
            return !_cam.IsFlyManner
        && _mode.IsPlayerMode
        && _driver.Controller is not null
        && _pursue.Legacy is not null
        && _feed.IsAvailable
        && !_grab.DevToolsWantGrabKeyboard;
        }
    }

    public AvatarLocomotionDriver? Controller => _driver.Controller;

    public uint LocateOwnActorIdent()
    {
        return _onlineActors.TryFetchRealmActor(_identity.SrvOid, out var actor)
            ? actor.Id
            : 0u;
    }

    public void ProcessTargeting() => _kineticsHub.Host?.ServiceTargetting();

    public bool IsConcealed => _onlineActors.IsHidden(_identity.SrvOid);

    public CanonClockVerdict ObjectTimerDisposition =>
        _onlineActors.FetchTrunkObjectTimerDisposition(_identity.SrvOid);

    public void Project(
        AvatarLocomotionDriver driver,
        LocomotionResult travel,
        bool concealed) =>
        _proj.Project(driver, travel, concealed);

    public void TransmitPreNetwork(
        AvatarLocomotionDriver driver,
        LocomotionResult travel,
        bool concealed)
    {
        _outgoing.TransmitPreNetworkActs(
            _session.LatestSess,
            driver,
            travel,
            concealed);
    }

    public void TransmitPostNetwork(
        AvatarLocomotionDriver driver,
        bool concealed)
    {
        _outgoing.TransmitPostNetworkLocus(
            _session.LatestSess,
            driver,
            concealed);
    }
}
