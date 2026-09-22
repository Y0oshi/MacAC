using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Graphics;

internal interface IOnlineMotionDisplayScope
{
    uint OwnAvatarOid { get; }
    MotionUnpacker? LocateLocomotionInterpreter(OnlineActorRecord capture);
}
