using MacAC.Client.Shell;
using MacAC.Extensibility.Panels;

namespace MacAC.Client.Extensions;

public sealed class BufferedWidgetRegistry : IOwnedPanelRegistry
{
    public readonly record struct ClientPending(
        PanelOwner Owner,
        PanelBlueprint Descriptor,
        string MarkupPath,
        object Binding)
    {
        internal long EnrollmentIdent { get; init; }
        internal string? MarkupSubstance { get; init; }

        public string PaneLabel =>
            $"plugin:{Owner.Id}:{Descriptor.WindowId}";
    }

    private sealed class ClientRegistration(
        PanelOwner holder,
        PanelBlueprint descriptor,
        string markupTrail,
        object mapping,
        string? markupSubstance = null)
    {
        internal PanelOwner Owner { get; } = holder;
        internal PanelBlueprint Descriptor { get; } = descriptor;
        internal string MarkupTrail { get; } = markupTrail;
        internal object Mapping { get; } = mapping;
        internal string? MarkupSubstance { get; } = markupSubstance;
        internal bool Drained { get; set; }
        internal WidgetTrunk? Root { get; set; }
        internal WidgetElem? Element { get; set; }
        internal Action? PaneTidy { get; set; }
    }

    private readonly object _latch = new();
    private readonly Dictionary<long, ClientRegistration> _registrations = [];
    private long _upcomingEnrollmentIdent;

    public void AppendMarkupBoard(string markupTrail, object mapping)
        => _ = EnrollMarkupBoard(markupTrail, mapping);

    public void AppendBoard(
        PanelBlueprint descriptor,
        string markupTrail,
        object mapping)
    {
        _ = RegisterPanel(
                new PanelOwner("unscoped", descriptor.Title),
                descriptor,
                markupTrail,
                mapping);
    }

    public IDisposable RegisterPanel(
        PanelBlueprint descriptor,
        string markupTrail,
        object mapping)
    {
        return RegisterPanel(
            new PanelOwner("unscoped", descriptor.Title),
            descriptor,
            markupTrail,
            mapping);
    }

    public IDisposable RegisterPanelContent(
        PanelBlueprint descriptor,
        string markupSubstance,
        object mapping)
    {
        return RegisterPanelContent(
            new PanelOwner("unscoped", descriptor.Title),
            descriptor,
            markupSubstance,
            mapping);
    }

    public bool ViewExists(string lensLabel) =>
        ViewExists(new PanelOwner("unscoped", "Plugin"), lensLabel);

    public bool IsViewVisible(string lensLabel) =>
        IsViewVisible(new PanelOwner("unscoped", "Plugin"), lensLabel);

    public bool ControlExists(string lensLabel, string controlLabel)
    {
        return ControlExists(
            new PanelOwner("unscoped", "Plugin"), lensLabel, controlLabel);
    }

    public bool SetControlLabel(
        string lensLabel,
        string controlLabel,
        string caption)
    {
        return SetControlLabel(
            new PanelOwner("unscoped", "Plugin"), lensLabel, controlLabel, caption);
    }

    public bool SetControlVisible(
        string lensLabel,
        string controlLabel,
        bool shown)
    {
        return SetControlVisible(
            new PanelOwner("unscoped", "Plugin"), lensLabel, controlLabel, shown);
    }

    public IDisposable EnrollMarkupBoard(string markupTrail, object mapping)
    {
        return RegisterPanel(
                new PanelOwner("legacy", "Plugin"),
                new PanelBlueprint(
                    Path.GetFileNameWithoutExtension(markupTrail),
                    Path.GetFileNameWithoutExtension(markupTrail)),
                markupTrail,
                mapping);
    }

    public IDisposable RegisterPanel(
        PanelOwner holder,
        PanelBlueprint descriptor,
        string markupTrail,
        object mapping)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(holder.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(holder.DisplayName);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.WindowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(markupTrail);
        ArgumentNullException.ThrowIfNull(mapping);
        long ident;
        lock (_latch)
        {
            ident = checked(++_upcomingEnrollmentIdent);
            _registrations.Add(
                ident,
                new ClientRegistration(holder, descriptor, markupTrail, mapping));
        }
        return new EnrollmentTicket(this, ident);
    }

    public IDisposable RegisterPanelContent(
        PanelOwner holder,
        PanelBlueprint descriptor,
        string markupSubstance,
        object mapping)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(holder.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(holder.DisplayName);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.WindowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(markupSubstance);
        ArgumentNullException.ThrowIfNull(mapping);
        long ident;
        lock (_latch)
        {
            ident = checked(++_upcomingEnrollmentIdent);
            _registrations.Add(
                ident,
                new ClientRegistration(
                    holder,
                    descriptor,
                    $"<inline:{descriptor.WindowId}>",
                    mapping,
                    markupSubstance));
        }
        return new EnrollmentTicket(this, ident);
    }

    public IReadOnlyList<ClientPending> Drain()
    {
        lock (_latch)
        {
            List<ClientPending> queued = new List<ClientPending>(_registrations.Count);
            foreach ((long ident, ClientRegistration enrollment) in _registrations)
            {
                if (enrollment.Drained)
                    continue;
                enrollment.Drained = true;
                queued.Add(new ClientPending(
                    enrollment.Owner,
                    enrollment.Descriptor,
                    enrollment.MarkupTrail,
                    enrollment.Mapping)
                {
                    EnrollmentIdent = ident,
                    MarkupSubstance = enrollment.MarkupSubstance,
                });
            }
            return queued;
        }
    }

    public bool ViewExists(PanelOwner holder, string lensLabel)
    {
        lock (_latch)
            return SeekEnrollmentBolted(holder, lensLabel) is not null;
    }

    public bool IsViewVisible(PanelOwner holder, string lensLabel)
    {
        WidgetElem? lens;
        lock (_latch)
            lens = SeekEnrollmentBolted(holder, lensLabel)?.Element;
        return lens?.Visible == true;
    }

    public bool ControlExists(
        PanelOwner holder,
        string lensLabel,
        string controlLabel) =>
        SeekControl(holder, lensLabel, controlLabel) is not null;

    internal int EnrollmentTally
    {
        get
        {
            lock (_latch)
                return _registrations.Count;
        }
    }

    public bool SetControlLabel(
        PanelOwner holder,
        string lensLabel,
        string controlLabel,
        string caption)
    {
        WidgetElem? control = SeekControl(holder, lensLabel, controlLabel);
        switch (control)
        {
            case WidgetSimpleButton btn:
                btn.WordingSrc = null;
                btn.Text = caption;
                return true;
            case WidgetMarkupToggle flip:
                flip.TextSrc = null;
                flip.Text = caption;
                return true;
            case WidgetCaption phrase:
                phrase.PhraseSrc = null;
                phrase.Text = caption;
                return true;
            default:
                return false;
        }
    }

    public bool SetControlVisible(
        PanelOwner holder,
        string lensLabel,
        string controlLabel,
        bool shown)
    {
        WidgetElem? control = SeekControl(holder, lensLabel, controlLabel);
        if (control is null)
            return false;
        control.ShownSrc = null;
        control.Visible = shown;
        return true;
    }

    internal void ConcludeMount(ClientPending queued, WidgetTrunk trunk, WidgetElem elem)
    {
        bool stillRegistered;
        lock (_latch)
        {
            stillRegistered = _registrations.TryGetValue(
                queued.EnrollmentIdent,
                out ClientRegistration? enrollment);
            if (stillRegistered)
            {
                enrollment!.Root = trunk;
                enrollment.Element = elem;
            }
        }

        if (!stillRegistered)
            trunk.DropDescendant(elem);
    }

    internal void ConcludePaneMount(ClientPending queued, Action tidy)
    {
        ArgumentNullException.ThrowIfNull(tidy);
        bool stillRegistered;
        lock (_latch)
        {
            stillRegistered = _registrations.TryGetValue(
                queued.EnrollmentIdent,
                out ClientRegistration? enrollment);
            if (stillRegistered)
                enrollment!.PaneTidy = tidy;
        }
        if (!stillRegistered)
            tidy();
    }

    internal void FailMount(ClientPending queued) => Drop(queued.EnrollmentIdent);

    private WidgetElem? SeekControl(
        PanelOwner holder,
        string lensLabel,
        string controlLabel)
    {
        WidgetElem? lens;
        lock (_latch)
            lens = SeekEnrollmentBolted(holder, lensLabel)?.Element;
        return lens is null ? null : SeekByLabel(lens, controlLabel);
    }

    private ClientRegistration? SeekEnrollmentBolted(
        PanelOwner holder,
        string lensLabel)
    {
        return _registrations.Values.FirstOrDefault(enrollment =>
            enrollment.Owner == holder
            && (enrollment.Descriptor.WindowId.Equals(
                    lensLabel, StringComparison.Ordinal)
                || enrollment.Descriptor.Title.Equals(
                    lensLabel, StringComparison.Ordinal)));
    }

    private static WidgetElem? SeekByLabel(WidgetElem trunk, string label)
    {
        if (trunk.Name?.Equals(label, StringComparison.Ordinal) == true)
            return trunk;
        foreach (WidgetElem descendant in trunk.Children)
        {
            WidgetElem? located = SeekByLabel(descendant, label);
            if (located is not null)
                return located;
        }
        return null;
    }

    private void Drop(long ident)
    {
        WidgetTrunk? trunk;
        WidgetElem? elem;
        Action? paneTidy;
        lock (_latch)
        {
            if (!_registrations.Remove(ident, out ClientRegistration? enrollment))
                return;
            trunk = enrollment.Root;
            elem = enrollment.Element;
            paneTidy = enrollment.PaneTidy;
        }

        paneTidy?.Invoke();
        if (trunk is not null && elem is not null)
            trunk.DropDescendant(elem);
    }

    private sealed class EnrollmentTicket(
        BufferedWidgetRegistry holder,
        long enrollmentIdent) : IDisposable
    {
        private BufferedWidgetRegistry? _holder = holder;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Drop(enrollmentIdent);
    }
}
