namespace MacAC.Cockpit.Panels.Vitals;

public sealed class VitalsPane(VitalsModel vm) : IPane
{
    private const float BarWidth = 200f;

    private readonly VitalsModel _vm = vm ?? throw new ArgumentNullException(nameof(vm));

    public string Id => "macac.vitals";

    public string Title => "Vitals";

    public bool IsVisible { get; set; } = true;

    public void Render(PaneContext cx, IPaneRenderer painter)
    {
        if (painter.Begin(Title))
        {
            Bar(painter, "HP", _vm.HealthPercent, _vm.HealthCurrent, _vm.HealthMax);
            if (_vm.StaminaPct is { } stamina)
                Bar(painter, "Stam", stamina, _vm.StaminaCurrent, _vm.StaminaMax);
            if (_vm.ManaPercent is { } mana)
                Bar(painter, "Mana", mana, _vm.ManaCurrent, _vm.ManaMax);
        }
        painter.End();
    }

    private static void Bar(IPaneRenderer painter, string caption, float pct, uint? latest, uint? upper)
    {
        painter.Text(caption);
        painter.SameLine();
        string topLayer = latest is { } c && upper is { } m && m > 0
            ? $"{c} / {m} ({pct * 100f:F0}%)"
            : $"{pct * 100f:F0}%";
        painter.ProgressBar(pct, BarWidth, topLayer: topLayer);
    }
}
