using MacAC.Mechanics.Kinetics;

namespace MacAC.Sim;

public readonly record struct SimActorIdentity(uint ServerGuid, uint LocalEntityId, ushort Incarnation);

public readonly record struct SimActorCapture(SimActorIdentity Identity, uint CellId, uint PhysicsState, Locus? Position);

public interface ISimActorVisitor
{
    void Tour(in SimActorCapture actor);
}

public interface ISimActorLens
{
    int Count { get; }

    int MaterializedCount { get; }

    bool TryGet(uint srvOid, out SimActorCapture actor);

    void Visit(ISimActorVisitor visitor);
}
