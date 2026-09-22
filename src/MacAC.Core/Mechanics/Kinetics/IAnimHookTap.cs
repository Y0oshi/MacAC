using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Receives animation hooks (sounds, particles, attacks…) as frames play.</summary>
public interface IAnimHookTap
{
    void OnTap(uint actorIdent, Vector3 actorRealmLocus, Cue tap);
}

/// <summary>A tap that swallows every hook.</summary>
public sealed class SilentAnimHookTap : IAnimHookTap
{
    public static readonly SilentAnimHookTap Instance = new();

    private SilentAnimHookTap() { }

    public void OnTap(uint actorIdent, Vector3 actorRealmLocus, Cue tap)
    {
    }
}
