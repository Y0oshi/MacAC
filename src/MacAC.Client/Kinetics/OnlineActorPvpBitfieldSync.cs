using MacAC.Client.Realm;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Kinetics;

internal sealed class OnlineActorPvpBitfieldSync : IDisposable
{
    private readonly ClientThingChart _objects;
    private readonly OnlineActorCore _onlineActors;
    private readonly ProxyRegistry _shades;
    private bool _destroyed;

    public OnlineActorPvpBitfieldSync(
        ClientThingChart objects,
        OnlineActorCore liveEntities,
        ProxyRegistry shadows)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
        _shades = shadows ?? throw new ArgumentNullException(nameof(shadows));
        _objects.ObjectUpdated += OnObjectUpdated;
    }

    private void OnObjectUpdated(ClientThing gear)
    {
        if (gear.PublicWeenieBitfield is not { } bitfield)
            return;
        if (!_onlineActors.TryFetchRecord(gear.ObjectId, out OnlineActorRecord capture)
            || capture.WorldEntity is not { } actor)

            return;
        _shades.RefreshPwdBitfieldFlagSet(actor.Id, bitfield);
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _objects.ObjectUpdated -= OnObjectUpdated;
    }
}
