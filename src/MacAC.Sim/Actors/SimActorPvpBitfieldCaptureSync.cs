using MacAC.Mechanics.Gear;
using MacAC.Wire;

namespace MacAC.Sim.Actors;

// Keeps an entity's PvP/description bitfield in step with its object-table record whenever the
// object changes
internal sealed class SimActorPvpBitfieldCaptureSync : IDisposable
{
    private readonly SimActorIndex _actors;
    private readonly ClientThingChart _objects;
    private bool _destroyed;

    public SimActorPvpBitfieldCaptureSync(SimActorIndex entities, ClientThingChart objects)
    {
        _actors = entities ?? throw new ArgumentNullException(nameof(entities));
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _objects.ObjectUpdated += OnObjectShift;
    }

    private void OnObjectShift(ClientThing gear)
    {
        if (gear.PublicWeenieBitfield is not { } bitset)
            return;
        if (_actors.TryRenewObjectBlurbFlagSet(gear.ObjectId, bitset, out RealmSession.MoverSpawn merged)
            && _actors.TryFetchEngaged(gear.ObjectId, out SimActorRecord capture))

            _actors.RenewCapture(capture, merged, renewLocus: false);
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _objects.ObjectUpdated -= OnObjectShift;
    }
}
