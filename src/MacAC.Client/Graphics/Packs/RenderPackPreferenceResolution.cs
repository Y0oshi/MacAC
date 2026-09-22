using MacAC.Cockpit.Panels.Settings;
using MacAC.Extensibility.RenderPacks;

namespace MacAC.Client.Graphics.Packs;

internal static class RenderPackPreferenceResolution
{
    internal static RenderPackAuditResult VetUserSubstitutions(
        RenderPackCard descriptor,
        RenderPackOverrides substitutions)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (substitutions is null)
            return Invalid($"Render pack '{descriptor.Id}' has a null user-setting override map.");

        var prefs = descriptor.Settings
            .ToDictionary(setting => setting.Id, StringComparer.OrdinalIgnoreCase);
        foreach ((string ident, string val) in substitutions)
        {
            if (!prefs.TryGetValue(ident, out RenderSettingSpec? setting))
            {
                return Invalid(
                    $"Render pack '{descriptor.Id}' has a user override for unknown "
                    + $"setting '{ident}'.");
            }
            if (!SettingValueCodec.TryPack(setting, val, out _))
            {
                return Invalid(
                    $"Render pack '{descriptor.Id}' user override '{ident}' has invalid "
                    + $"{setting.Kind} value '{val}'.");
            }
        }
        return RenderPackAuditResult.Valid();
    }

    internal static string Resolve(
        RenderSettingSpec setting,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string>? userSubstitutions)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(preset);
        if (TryGet(userSubstitutions, setting.Id, out string? user))
            return user;
        var presetVal = preset.SettingOverrides
            .FirstOrDefault(val => string.Equals(
                val.SettingId,
                setting.Id,
                StringComparison.OrdinalIgnoreCase));
        return presetVal?.Value ?? setting.DefaultValue;
    }

    private static bool TryGet(
        IReadOnlyDictionary<string, string>? vals,
        string ident,
        out string val)
    {
        if (vals is not null && vals.TryGetValue(ident, out val!))
            return true;
        if (vals is not null)
        {
            foreach ((string tag, string contender) in vals)
            {
                if (string.Equals(tag, ident, StringComparison.OrdinalIgnoreCase))
                {
                    val = contender;
                    return true;
                }
            }
        }
        val = string.Empty;
        return false;
    }

    private static RenderPackAuditResult Invalid(string cause) =>
        RenderPackAuditResult.Invalid(cause);
}
