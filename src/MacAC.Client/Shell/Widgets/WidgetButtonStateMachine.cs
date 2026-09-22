namespace MacAC.Client.Shell;

public readonly record struct WidgetButtonVisualInput(
    bool Disabled,
    bool Selected,
    bool RolloverEnabled,
    bool Pressed,
    bool PointerOver);

public static class WidgetButtonStateMachine
{
    public const uint Normal = 1u;
    public const uint NormRollover = 2u;
    public const uint NormPressed = 3u;
    public const uint Highlight = 6u;
    public const uint HighlightRollover = 7u;
    public const uint HighlightPressed = 8u;
    public const uint Ghosted = 13u;

    public static uint AskedPhase(WidgetButtonVisualInput feed)
    {
        if (feed.Disabled)
            return Ghosted;

        uint asked = feed.Selected ? Highlight : Normal;
        if (feed.Pressed || (feed.RolloverEnabled && feed.PointerOver))
            asked = feed.Selected ? HighlightRollover : NormRollover;
        if (feed.Pressed && feed.PointerOver)
            asked = feed.Selected ? HighlightPressed : NormPressed;
        return asked;
    }

    public static uint LocatePhase(
        uint latestPhase,
        WidgetButtonVisualInput feed,
        IReadOnlySet<uint> onHandPhases)
    {
        uint asked = AskedPhase(feed);
        return onHandPhases.Contains(asked) ? asked : latestPhase;
    }

    public static string PhaseMoniker(uint phaseIdent)
    {
        return phaseIdent switch
        {
            Normal => "Normal",
            NormRollover => "Normal_rollover",
            NormPressed => "Normal_pressed",
            Highlight => "Highlight",
            HighlightRollover => "Highlight_rollover",
            HighlightPressed => "Highlight_pressed",
            Ghosted => "Ghosted",
            _ => "",
        };
    }

    public static bool TryPhaseTag(string phaseLabel, out uint phaseIdent)
    {
        phaseIdent = phaseLabel switch
        {
            "Normal" => Normal,
            "Normal_rollover" => NormRollover,
            "Normal_pressed" => NormPressed,
            "Highlight" => Highlight,
            "Highlight_rollover" => HighlightRollover,
            "Highlight_pressed" => HighlightPressed,
            "Ghosted" => Ghosted,
            _ => 0u,
        };
        return phaseIdent is not 0;
    }
}
