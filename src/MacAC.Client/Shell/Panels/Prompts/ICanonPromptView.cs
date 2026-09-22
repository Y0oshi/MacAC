namespace MacAC.Client.Shell.Panels;

internal interface ICanonPromptView
{
    WidgetPopupTrunk Root { get; }

    void Tick();

    void AssignQueuedTally(int tally);

    void UnfastenHandlers();
}
