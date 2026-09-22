using MacAC.Wire;

namespace MacAC.Sim.Presence;

/// <summary>Which character to pick after login: by roster slot, id or name.</summary>
public sealed record OnlineSessionToonSelector(int? ActiveIndex = null, uint? CharacterId = null, string? CharacterName = null);

public sealed record OnlineSessionConnectOptions(
    bool Enabled,
    string Host,
    int Port,
    string User,
    string Password,
    OnlineSessionToonSelector? Character = null,
    bool Probe = false,
    bool AwaitCharacterSelection = false,
    bool PollConnectionDuringTicks = false);

public interface ISimOnlineSessionFramePhase
{
    void Tick();
}

public interface IOnlineSessionEventRouting : IDisposable
{
    void Attach();
}

public interface IOnlineSessionDirectiveRouting : IDisposable
{
    void Arm();
}
