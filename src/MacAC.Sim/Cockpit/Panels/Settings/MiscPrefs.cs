namespace MacAC.Cockpit.Panels.Settings;

public sealed record MiscPrefs(bool TooltipEnable, float TooltipDelaySeconds)
{
    public static MiscPrefs Default { get; } = new(TooltipEnable: true, TooltipDelaySeconds: 0.25f);
}
