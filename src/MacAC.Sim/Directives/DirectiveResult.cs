namespace MacAC.Sim;

public enum SimDirectiveStatus
{
    Accepted,
    Inactive,
    StaleGeneration,
    Unsupported,
    Rejected,
}

public readonly record struct SimDirectiveResult(SimDirectiveStatus Status, SimEpochTicket Generation, uint ResultObjectId = 0u)
{
    public bool Accepted => Status == SimDirectiveStatus.Accepted;
}

public enum SimSessionStartStatus
{
    Disabled,
    MissingCredentials,
    NoCharacters,
    Connected,
    Deferred,
    Failed,
    Inactive,
    StaleGeneration,
    ProbeComplete,
    AwaitingCharacterSelection,
}

public readonly record struct SimSessionStartResult(
    SimSessionStartStatus Status,
    SimEpochTicket Generation,
    uint CharacterId = 0u,
    string CharacterName = "",
    Exception? Error = null);
