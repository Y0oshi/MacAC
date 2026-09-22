namespace MacAC.Client.Shell;

public readonly record struct RetainedWindowLedger(
    bool Collapsed = false,
    bool Maximized = false,
    float? PersistedTop = null,
    float? PersistedHeight = null,
    bool? RequestedVisible = null);

public interface IRetainedWindowStateDriver
{
    RetainedWindowLedger GrabPanePhase();
    void ReinstatePanePhase(RetainedWindowLedger phase);
}
