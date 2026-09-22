namespace MacAC.Client.Realm;

public sealed class OnlineActorPartArrayEnterRealmPort(Action<uint> handleEnterWorld)
{
    private readonly Action<uint> _hndJoinRealm = handleEnterWorld
            ?? throw new ArgumentNullException(nameof(handleEnterWorld));

    public void HandleEnterWorld(uint ownActorIdent) =>
        _hndJoinRealm(ownActorIdent);
}
