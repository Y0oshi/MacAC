namespace MacAC.Extensibility.Automation;

public enum SelectionVerb
{
    PreviousSelection = 0,
    PreviousPlayer,
    NextPlayer,
}

public interface ISelectionControls
{
    bool Execute(SelectionVerb act) => false;
}
