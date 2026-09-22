using MacAC.Client.Paging;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Batching;

internal sealed class CompositeWarmupActorSource(GpuRealmPhase world)
{
    private readonly GpuRealmPhase _world = world ?? throw new ArgumentNullException(nameof(world));
    private readonly List<RealmActor> _actors = [];
    private bool _initialized;
    private uint _destChamber;
    private int _radius;

    public IReadOnlyList<RealmActor> Entities => _actors;
    public ulong Generation { get; private set; }

    public void Refresh(uint destChamber, int radius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        ulong realmGen = _world.PlanarLensGen;
        if (_initialized
            && Generation == realmGen
            && _destChamber == destChamber
            && _radius == radius)

            return;

        _world.DuplicatePublishedActorsNearbyLbForReadiness(
            destChamber,
            radius,
            _actors);
        Generation = realmGen;
        _destChamber = destChamber;
        _radius = radius;
        _initialized = true;
    }

    public void Reset()
    {
        _actors.Clear();
        _initialized = false;
        Generation = 0;
        _destChamber = 0;
        _radius = 0;
    }
}
