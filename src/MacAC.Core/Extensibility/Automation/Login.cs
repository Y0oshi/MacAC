namespace MacAC.Extensibility.Automation;

public readonly record struct RosterEntry(
    uint ObjectId,
    string Name,
    int ActiveIndex,
    bool IsPendingDelete);

public interface ILoginControls
{
    bool IsAvailable => false;

    uint NextLoginObjectId => 0u;

    IReadOnlyList<RosterEntry> CaptureRoster() => Array.Empty<RosterEntry>();

    bool SetNextLogin(uint toonObjectIdent) => false;

    bool ClearNextLogin() => false;
}
