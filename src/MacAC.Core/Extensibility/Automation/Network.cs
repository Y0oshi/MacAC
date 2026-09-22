namespace MacAC.Extensibility.Automation;

/// <summary>Another MacAC client this process knows about.</summary>
public readonly record struct PeerEntry(
    uint ClientId,
    uint PlayerId,
    string Name,
    string WorldName,
    NavigationFix Position,
    IReadOnlyList<string> Tags,
    uint CurrentHealth,
    uint CurrentMana,
    uint CurrentStamina,
    uint MaxHealth,
    uint MaxMana,
    uint MaxStamina,
    float Heading);

public interface INetworkControls
{
    bool IsAvailable => false;

    IReadOnlyList<PeerEntry> CaptureClients() => Array.Empty<PeerEntry>();
}
