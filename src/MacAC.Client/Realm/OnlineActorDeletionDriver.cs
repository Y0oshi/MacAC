using MacAC.Client.Controls;
using MacAC.Sim.Actors;
using MacAC.Wire.Messages;

namespace MacAC.Client.Realm;

internal interface IOnlineActorPruneSink
{
    bool Prune(OnlineActorPruneCandidate contender);
}

// Owns authoritative and expiry-driven live-object deletion through one generation-safe runtime
// transaction
internal sealed class OnlineActorDeletionDriver : IOnlineActorPruneSink
{
    private readonly OnlineActorCore _runtime;
    private readonly IOnlineActorTeardownMarshal _teardown;
    private readonly IAvatarIdentitySource _identity;

    public OnlineActorDeletionDriver(
        OnlineActorCore runtime,
        SimActorObjectLifetime actorObjects,
        IOnlineActorTeardownMarshal teardown,
        IAvatarIdentitySource identity)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        ArgumentNullException.ThrowIfNull(actorObjects);
        _teardown = teardown ?? throw new ArgumentNullException(nameof(teardown));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    public bool Delete(ObjectDeletion.Parsed erase)
    {
        if (erase.Guid == _identity.SrvOid)
            return false;

        bool hasEngagedCapture = _runtime.TryFetchRecord(erase.Guid, out _);
        if (!hasEngagedCapture)
            _teardown.DiscardUnknownHolder(erase.Guid);

        return _runtime.WithdrawOnlineActor(
            erase,
            isOwnAvatar: false,
            dropKeptObject: true);
    }

    public bool EraseClientGhost(uint srvOid)
    {
        return srvOid is 0u
            || srvOid == _identity.SrvOid
            || !_runtime.TryFetchRecord(srvOid, out OnlineActorRecord capture)
            ? false
            : Delete(new ObjectDeletion.Parsed(srvOid, capture.Generation));
    }

    // Destroys an object whose 25-second out-of-visibility deadline expired
    public bool Prune(OnlineActorPruneCandidate contender)
    {
        return !_runtime.TryFetchCapture(
                contender.Key,
                out OnlineActorRecord capture)
            || capture.ServerOid != contender.ServerGuid
            ? false
            : _runtime.WithdrawOnlineActor(
            new ObjectDeletion.Parsed(
                contender.ServerGuid,
                contender.Generation),
            isOwnAvatar: false,
            dropKeptObject: true);
    }
}
