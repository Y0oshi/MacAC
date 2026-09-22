namespace MacAC.Client.Shell.Panels;

public sealed class VitalsSideBySideDriver(
    WidgetTrunk root,
    Func<bool> sideBySideVitals,
    string stackedPane,
    string flankPane)
{
    private readonly WidgetTrunk _trunk = root ?? throw new ArgumentNullException(nameof(root));
    private readonly Func<bool> _flankByFlankVitals = sideBySideVitals
            ?? throw new ArgumentNullException(nameof(sideBySideVitals));
    private readonly string _stackedPane = stackedPane;
    private readonly string _flankPane = flankPane;

    public bool? Applied { get; private set; }

    public void Tick()
    {
        bool flankByFlank = _flankByFlankVitals();
        if (Applied == flankByFlank) return;
        Applied = flankByFlank;

        if (flankByFlank)
        {
            _trunk.MaskPane(_stackedPane);
            _trunk.DisplayPane(_flankPane);
        }
        else
        {
            _trunk.DisplayPane(_stackedPane);
            _trunk.MaskPane(_flankPane);
        }
    }
}
