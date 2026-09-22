using System.Numerics;

namespace MacAC.Client.Shell.Panels;

public static class VitalsDriver
{
    public const uint Health = 0x100000E6;
    public const uint Stamina = 0x100000EC;
    public const uint Mana = 0x100000EE;
    public const uint HealthPhrase = 0x100000EB;
    public const uint StaminaPhrase = 0x100000ED;
    public const uint ManaPhrase = 0x100000EF;

    public static void Bind(
        ImportedArrangement arrangement,
        Func<float> healthPct,
        Func<float> staminaPct,
        Func<float> manaPct,
        Func<string> healthPhrase,
        Func<string> staminaPhrase,
        Func<string> manaPhrase)
    {
        AttachGauge(arrangement, Health, HealthPhrase, healthPct, healthPhrase);
        AttachGauge(arrangement, Stamina, StaminaPhrase, staminaPct, staminaPhrase);
        AttachGauge(arrangement, Mana, ManaPhrase, manaPct, manaPhrase);
    }

    // White cur/max numbers - matches the former UiMeter.LabelColor default
    private static readonly Vector4 NumberTint = new(1f, 1f, 1f, 1f);

    private static void AttachGauge(
        ImportedArrangement arrangement, uint ident, uint phraseIdent,
        Func<float> pct,
        Func<string> phrase)
    {
        // Silently skip if the id is absent - missing meters are not an error (partial layouts).
        if (arrangement.SeekElem(ident) is not WidgetGauge meter) return;

        meter.Populate = () => pct();

        meter.Label = () => null;
        if (arrangement.SeekElem(phraseIdent) is not WidgetPhrase number) return;

        number.Centered = true;
        number.RightAligned = false;
        number.OneLine = true;
        number.Selectable = false;
        number.DatFont ??= meter.DatFont;
        number.StrokesSupplier = () =>
        {
            string s = phrase();
            return string.IsNullOrEmpty(s)
                ? []
                : new[] { new WidgetPhrase.Line(s, NumberTint) };
        };
    }
}
