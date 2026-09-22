namespace MacAC.Cockpit;

/// <summary>Owns the registered panes and draws them each frame.</summary>
public interface IPaneHost
{
    void Register(IPane board);

    /// <summary>Drops the pane with this id; unknown ids are ignored.</summary>
    void Unregister(string boardIdent);

    void PaintAll(PaneContext cx);
}
