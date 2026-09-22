using MacAC.Client.Realm;

namespace MacAC.Client.Graphics;

internal sealed class OnlineActorPartArrayLifespan(
    OnlineActorMotionEngineView<OnlineActorMotionLedger> animations)
{
    private readonly OnlineActorMotionEngineView<OnlineActorMotionLedger> _anims = animations ?? throw new ArgumentNullException(nameof(animations));

    public void HandleEnterWorld(uint ownActorIdent)
    {
        if (_anims.TryGetValue(ownActorIdent, out OnlineActorMotionLedger? anim))
            anim.Sequencer?.Manager.HandleEnterWorld();
    }
}
