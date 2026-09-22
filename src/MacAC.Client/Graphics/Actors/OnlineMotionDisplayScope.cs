using MacAC.Client.Controls;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Graphics;

internal sealed class OnlineMotionDisplayScope(
    OnlineActorCore runtime,
    IAvatarIdentitySource localPlayer,
    ILocalPlayerMotionSource playerMotion)
        : IOnlineMotionDisplayScope
{
    private readonly OnlineActorCore _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    private readonly IAvatarIdentitySource _ownAvatar = localPlayer ?? throw new ArgumentNullException(nameof(localPlayer));
    private readonly ILocalPlayerMotionSource _avatarLocomotion = playerMotion
            ?? throw new ArgumentNullException(nameof(playerMotion));

    public uint OwnAvatarOid => _ownAvatar.SrvOid;

    public MotionUnpacker? LocateLocomotionInterpreter(OnlineActorRecord capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!_runtime.TryFetchRecord(capture.ServerOid, out OnlineActorRecord latest)
            || !ReferenceEquals(latest, capture))

            return null;

        return capture.ServerOid == _ownAvatar.SrvOid
            ? _avatarLocomotion.Motion
            : capture.RemoteMotionRuntime is PeerMotion distant
            ? distant.Motion
            : null;
    }
}
