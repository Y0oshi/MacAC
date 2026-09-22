using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal interface IOnlineStaticPartFrameSource
{
    bool TryGrabOnlinePieceCycles(
        OnlineActorRecord capture,
        RealmActor actor,
        OnlineActorMotionLedger anim,
        ulong objectTimerEpoch,
        ulong projAlterationVer,
        ulong exhibitRev,
        out IReadOnlyList<PieceTransform> cycles);
}
