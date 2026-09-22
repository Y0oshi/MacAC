using System.Numerics;
using MacAC.Sim.Actors;

namespace MacAC.Client.Kinetics;

internal sealed class RemoteLocomotionObservationLedger
{
    private readonly Dictionary<SimActorKey, (Vector3 Pos, DateTime Time)>
        _lastMove = [];

    internal (Vector3 Pos, DateTime Time) this[SimActorKey key]
    {
        set => _lastMove[key] = value;
    }

    internal bool TryFetchVal(
        SimActorKey tag,
        out (Vector3 Pos, DateTime Time) observation) =>
        _lastMove.TryGetValue(tag, out observation);

    internal bool Drop(SimActorKey tag) => _lastMove.Remove(tag);

    internal void Clear() => _lastMove.Clear();
}
