using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Mechanics.Drawing;

/// <summary>Routes TransparentPart animation hooks into the fade keeper.</summary>
public sealed class SeeThroughHookTap(SeeThroughFadeKeeper fades) : IAnimHookTap
{
    private readonly SeeThroughFadeKeeper _fades = fades ?? throw new ArgumentNullException(nameof(fades));

    public void OnTap(uint actorIdent, Vector3 actorRealmLocus, Cue tap)
    {
        if (tap is TransparentPartCue piece)
            _fades.BeginPieceFade(actorIdent, piece.PartIndex, piece.Start, piece.End, piece.Time);
    }
}
