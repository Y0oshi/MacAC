namespace MacAC.Extensibility.Panels;

/// <summary>Windows an extension asks the host to draw from markup.</summary>
public interface IPanelRegistry
{
    /// <param name="markupTrail">Absolute path to the panel markup file.</param>
    /// <param name="mapping">Object whose members the markup's {Bindings} read.</param>
    void AppendMarkupBoard(string markupTrail, object mapping);

    void AppendBoard(PanelBlueprint descriptor, string markupTrail, object mapping) =>
        AppendMarkupBoard(markupTrail, mapping);

    IDisposable RegisterPanel(PanelBlueprint descriptor, string markupTrail, object mapping)
    {
        AppendBoard(descriptor, markupTrail, mapping);
        return MutePanelLease.Instance;
    }

    IDisposable RegisterPanelContent(
        PanelBlueprint descriptor,
        string markupSubstance,
        object mapping) => MutePanelLease.Instance;

    /// <summary>Queries this extension's own view by title or stable id.</summary>
    bool ViewExists(string lensLabel) => false;

    bool IsViewVisible(string lensLabel) => false;

    bool ControlExists(string lensLabel, string controlLabel) => false;

    bool SetControlLabel(string lensLabel, string controlLabel, string caption) => false;

    bool SetControlVisible(string lensLabel, string controlLabel, bool shown) => false;
}

/// <summary>The host-side registry, which tracks which owner a window belongs to.</summary>
public interface IOwnedPanelRegistry : IPanelRegistry
{
    IDisposable EnrollMarkupBoard(string markupTrail, object mapping);

    IDisposable RegisterPanel(
        PanelOwner holder,
        PanelBlueprint descriptor,
        string markupTrail,
        object mapping) => EnrollMarkupBoard(markupTrail, mapping);

    /// <summary>Host-owned registration for markup held in memory.</summary>
    IDisposable RegisterPanelContent(
        PanelOwner holder,
        PanelBlueprint descriptor,
        string markupSubstance,
        object mapping) => MutePanelLease.Instance;

    bool ViewExists(PanelOwner holder, string lensLabel) => false;

    bool IsViewVisible(PanelOwner holder, string lensLabel) => false;

    bool ControlExists(PanelOwner holder, string lensLabel, string controlLabel) => false;

    bool SetControlLabel(
        PanelOwner holder,
        string lensLabel,
        string controlLabel,
        string caption) => false;

    bool SetControlVisible(
        PanelOwner holder,
        string lensLabel,
        string controlLabel,
        bool shown) => false;
}

/// <summary>The lease handed out by hosts that never actually draw anything.</summary>
public sealed class MutePanelLease : IDisposable
{
    public static MutePanelLease Instance { get; } = new();

    private MutePanelLease()
    {
    }

    public void Dispose()
    {
    }
}

/// <summary>A registry for hosts without a window.</summary>
public sealed class MutePanelRegistry : IOwnedPanelRegistry
{
    public static MutePanelRegistry Instance { get; } = new();

    private MutePanelRegistry()
    {
    }

    public void AppendMarkupBoard(string markupTrail, object mapping)
    {
    }

    public void AppendBoard(PanelBlueprint descriptor, string markupTrail, object mapping)
    {
    }

    public IDisposable RegisterPanel(
        PanelBlueprint descriptor,
        string markupTrail,
        object mapping) => MutePanelLease.Instance;

    public IDisposable RegisterPanelContent(
        PanelBlueprint descriptor,
        string markupSubstance,
        object mapping) => MutePanelLease.Instance;

    public IDisposable EnrollMarkupBoard(string markupTrail, object mapping) =>
        MutePanelLease.Instance;

    public IDisposable RegisterPanel(
        PanelOwner holder,
        PanelBlueprint descriptor,
        string markupTrail,
        object mapping) => MutePanelLease.Instance;

    public IDisposable RegisterPanelContent(
        PanelOwner holder,
        PanelBlueprint descriptor,
        string markupSubstance,
        object mapping) => MutePanelLease.Instance;
}
