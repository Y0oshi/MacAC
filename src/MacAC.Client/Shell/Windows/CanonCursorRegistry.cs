namespace MacAC.Client.Shell;

internal static class CanonCursorRegistry
{
    public const uint CurEnumChart = 6;

    public static bool TryFetchGlobalCur(CanonGlobalCursorKind sort, out CanonCursorSpec spec)
    {
        spec = sort switch
        {
            CanonGlobalCursorKind.Default => new CanonCursorSpec(0x01u, 0, 0),
            CanonGlobalCursorKind.DefaultFound => new CanonCursorSpec(0x02u, 0, 0),
            CanonGlobalCursorKind.MeleeOrMissile => new CanonCursorSpec(0x03u, 0, 0),
            CanonGlobalCursorKind.MeleeOrMissileFound => new CanonCursorSpec(0x04u, 0, 0),
            CanonGlobalCursorKind.Magic => new CanonCursorSpec(0x05u, 0, 0),
            CanonGlobalCursorKind.MagicFound => new CanonCursorSpec(0x06u, 0, 0),
            CanonGlobalCursorKind.Examine => new CanonCursorSpec(0x0Au, 0, 0),
            CanonGlobalCursorKind.ExamineFound => new CanonCursorSpec(0x0Bu, 0, 0),
            CanonGlobalCursorKind.Use => new CanonCursorSpec(0x0Cu, 14, 14),
            CanonGlobalCursorKind.UseFound => new CanonCursorSpec(0x0Du, 14, 14),
            CanonGlobalCursorKind.Busy => new CanonCursorSpec(0x0Eu, 0, 0),
            CanonGlobalCursorKind.BusyFound => new CanonCursorSpec(0x0Fu, 0, 0),
            CanonGlobalCursorKind.TargetPending => new CanonCursorSpec(0x27u, 14, 14),
            CanonGlobalCursorKind.TargetValid => new CanonCursorSpec(0x28u, 14, 14),
            CanonGlobalCursorKind.TargetInvalid => new CanonCursorSpec(0x29u, 14, 14),
            _ => default,
        };

        return spec.IsValid;
    }

    public static bool TryFetchPaneControlCur(
        CursorFeedbackFlavor sort,
        out WidgetCursorMedia cur)
    {
        cur = sort switch
        {
            CursorFeedbackFlavor.WindowMove => new WidgetCursorMedia(0x06006119u, 16, 16),
            CursorFeedbackFlavor.ResizeHorizontal => new WidgetCursorMedia(0x06006128u, 16, 16),
            CursorFeedbackFlavor.ResizeVertical => new WidgetCursorMedia(0x06005E66u, 16, 16),
            CursorFeedbackFlavor.ResizeDiagonalNwse => new WidgetCursorMedia(0x06006126u, 16, 16),
            CursorFeedbackFlavor.ResizeDiagonalNesw => new WidgetCursorMedia(0x06006127u, 16, 16),
            _ => default,
        };

        return cur.IsValid;
    }
}

internal readonly record struct CanonCursorSpec(uint EnumId, int HotspotX, int HotspotY)
{
    public bool IsValid => EnumId is not 0;
}
