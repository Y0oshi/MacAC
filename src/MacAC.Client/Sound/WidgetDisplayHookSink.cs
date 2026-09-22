using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Sound;

public sealed class WidgetDisplayHookSink(IAnimHookTap router, SoundTapDrain? sound) : IAnimHookTap
{
    private readonly IAnimHookTap _router = router ?? throw new ArgumentNullException(nameof(router));
    private readonly SoundTapDrain? _sound = sound;

    public void OnTap(uint actorIdent, Vector3 actorRealmLocus, Cue tap)
    {
        if (tap is SoundCue or SoundTableCue or SoundTweakedCue)
        {
            _sound?.OnWidgetTap(actorIdent, tap);
            return;
        }

        _router.OnTap(actorIdent, actorRealmLocus, tap);
    }
}
