namespace MacAC.Cockpit;

/// <summary>What a pane gets per frame: the elapsed time and the bus it posts commands on.</summary>
public readonly record struct PaneContext(float DeltaSeconds, IDirectiveBus Commands);
