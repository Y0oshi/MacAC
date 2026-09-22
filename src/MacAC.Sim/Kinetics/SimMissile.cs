using MacAC.Mechanics.Kinetics;

namespace MacAC.Sim.Kinetics;

public interface ISimMissile
{
    KineticBody Body { get; }

    MissileContactSphere ImpactOrb { get; }

    ulong PredictionArbiterVer { get; }
}

internal sealed class SimMissile : ISimMissile
{
    internal SimMissile(KineticBody body, MissileContactSphere collisionSphere)
    {
        Body = body ?? throw new ArgumentNullException(nameof(body));
        if (!collisionSphere.IsValid)
            throw new ArgumentOutOfRangeException(nameof(collisionSphere), "A Runtime projectile needs one valid prepared Setup sphere");
        ImpactOrb = collisionSphere;
    }

    public KineticBody Body { get; }
    public MissileContactSphere ImpactOrb { get; }
    public ulong PredictionArbiterVer { get; private set; }

    internal void DirtyPrediction() => PredictionArbiterVer++;
}
