using System.Globalization;

namespace MacAC.Extensibility.RenderPacks;

public static class SettingValueCodec
{
    private const long WholeNumberFloatThreshold = 16_777_216L;

    public static bool TryPack(RenderSettingSpec setting, string? val, out float encoded)
    {
        ArgumentNullException.ThrowIfNull(setting);
        encoded = 0f;
        if (val is null)
            return false;

        float? outcome = setting.Kind switch
        {
            SettingValueKind.Boolean => PackBoolean(val),
            SettingValueKind.Integer => PackInteger(setting, val),
            SettingValueKind.Float => PackFloat(setting, val),
            SettingValueKind.Choice => PackChoice(setting, val),
            _ => null,
        };
        if (outcome is not { } ok)
            return false;
        encoded = ok;
        return true;
    }

    private static float? PackBoolean(string val) =>
        bool.TryParse(val, out bool bit) ? (bit ? 1f : 0f) : null;

    private static float? PackInteger(RenderSettingSpec setting, string val)
    {
        if (!long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole))
            return null;
        if (whole is < -WholeNumberFloatThreshold or > WholeNumberFloatThreshold)
            return null;
        return InSpan(whole, setting) && OnHop(whole, setting) ? whole : null;
    }

    private static float? PackFloat(RenderSettingSpec setting, string val)
    {
        if (!double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double real))
            return null;
        if (!double.IsFinite(real) || real < -float.MaxValue || real > float.MaxValue)
            return null;
        if (!InSpan(real, setting) || !OnHop(real, setting))
            return null;
        float narrowed = (float)real;
        return float.IsFinite(narrowed) ? narrowed : null;
    }

    private static float? PackChoice(RenderSettingSpec setting, string val)
    {
        IReadOnlyList<string>? choices = setting.Choices;
        if (choices is null)
            return null;
        for (int ordinal = 0; ordinal < choices.Count; ++ordinal)
        {
            if (string.Equals(choices[ordinal], val, StringComparison.Ordinal))
                return ordinal;
        }
        return null;
    }

    private static bool InSpan(double val, RenderSettingSpec setting)
    {
        return (setting.Minimum is not { } lo || val >= lo)
        && (setting.Maximum is not { } hi || val <= hi);
    }

    private static bool OnHop(double val, RenderSettingSpec setting)
    {
        if (setting.Step is not { } hop)
            return true;
        if (!double.IsFinite(hop) || hop <= 0)
            return false;
        double hops = (val - (setting.Minimum ?? 0d)) / hop;
        if (!double.IsFinite(hops))
            return false;
        double slack = Math.Max(1e-7, Math.Abs(hops) * 1e-7);
        return Math.Abs(hops - Math.Round(hops)) <= slack;
    }
}
