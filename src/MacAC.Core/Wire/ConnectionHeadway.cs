namespace MacAC.Wire;

public enum LinkPhase
{
    Inactive,
    Connecting,
    CheckingData,
    Ready,
    Unsupported,
    Failed,
}

/// <summary>Where the connect sequence is, with the failure text once it has failed.</summary>
public readonly record struct ConnectionHeadway(LinkPhase Phase, string? Error = null);

/// <summary>Raised when the server demands a DAT update the client cannot fetch.</summary>
public sealed class UnknownDataUpdateException()
    : NotSupportedException("This server requires a game-data update. MacAC does not support downloading DAT updates yet. Install the server's required data files before connecting.");
