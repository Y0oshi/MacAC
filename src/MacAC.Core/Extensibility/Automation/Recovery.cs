namespace MacAC.Extensibility.Automation;

/// <summary>Result of one explicit operator recovery step.</summary>
public readonly record struct RecoveryVerdict(
    bool Accepted,
    int PreviousCount = 0,
    int CurrentCount = 0,
    string Message = "");

public interface IRecoveryControls
{
    RecoveryVerdict ClearOneBusyReference()
    {
        return new(Accepted: false, Message: "Action recovery is unavailable on this host.");
    }
}
