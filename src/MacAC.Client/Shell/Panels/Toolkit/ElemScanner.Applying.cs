using System.Numerics;

namespace MacAC.Client.Shell.Panels;

public static partial class ElemScanner
{
    internal static void ImposeCanonLegacyProj(ElemDetails details)
    {
        if (details.TryFetchNetProp(0x1Au, out var typeface)
            && typeface.Kind == WidgetPropertyKind.Array
            && typeface.ArrayValue.Count > 0
            && typeface.ArrayValue[0].Kind == WidgetPropertyKind.DataId)
        {
            details.FontDid = checked((uint)typeface.ArrayValue[0].UnsignedValue);
        }

        if (details.TryFetchNetProp(0x14u, out var horizontal)
            && horizontal.Kind == WidgetPropertyKind.Enum)
        {
            details.HJustify = ChartHorizontalJustification(horizontal.UnsignedValue);
        }

        if (details.TryFetchNetProp(0x15u, out var vertical)
            && vertical.Kind == WidgetPropertyKind.Enum)
        {
            details.VJustify = ChartVerticalJustification(vertical.UnsignedValue);
        }

        ImposeCanonLegacyProjRest(details);
    }

    private static void ImposeCanonLegacyProjRest(ElemDetails details)
    {
        if (details.TryFetchNetProp(0x1Bu, out var tint))
        {
            WidgetPropertyValue? tintVal = tint.Kind == WidgetPropertyKind.Color
                ? tint
                : tint.Kind == WidgetPropertyKind.Array
                    && tint.ArrayValue.Count > 0
                    && tint.ArrayValue[0].Kind == WidgetPropertyKind.Color
                        ? tint.ArrayValue[0]
                        : null;
            if (tintVal is not null)
            {
                WidgetColorValue c = tintVal.ColorValue;
                float alpha = c.Alpha is 0 ? 1f : c.Alpha / 255f;
                details.FontColor = new Vector4(c.Red / 255f, c.Green / 255f, c.Blue / 255f, alpha);
            }
        }
        if (details.TryFetchNetProp(0x1Du, out var tagTint))
        {
            WidgetPropertyValue? tagVal = tagTint.Kind == WidgetPropertyKind.Color
                ? tagTint
                : tagTint.Kind == WidgetPropertyKind.Array
                    && tagTint.ArrayValue.Count > 0
                    && tagTint.ArrayValue[0].Kind == WidgetPropertyKind.Color
                        ? tagTint.ArrayValue[0]
                        : null;
            if (tagVal is not null)
            {
                WidgetColorValue t = tagVal.ColorValue;
                float alpha = t.Alpha is 0 ? 1f : t.Alpha / 255f;
                details.TagFontColor =
                    new Vector4(t.Red / 255f, t.Green / 255f, t.Blue / 255f, alpha);
            }
        }
        ImposeCanonLegacyProjTail(details);
    }

    private static void ImposeCanonLegacyProjTail(ElemDetails details)
    {
        if (details.TryFetchNetProp(0x21u, out var outline)
                && outline.Kind == WidgetPropertyKind.Bool)

            details.Outline = outline.BoolValue;
        if (details.TryFetchNetProp(0x22u, out var outlineTint))
        {
            WidgetPropertyValue? outlineTintVal = outlineTint.Kind == WidgetPropertyKind.Color
                ? outlineTint
                : outlineTint.Kind == WidgetPropertyKind.Array
                    && outlineTint.ArrayValue.Count > 0
                    && outlineTint.ArrayValue[0].Kind == WidgetPropertyKind.Color
                        ? outlineTint.ArrayValue[0]
                        : null;
            if (outlineTintVal is not null)
            {
                WidgetColorValue c = outlineTintVal.ColorValue;
                float alpha = c.Alpha is 0 ? 1f : c.Alpha / 255f;
                details.OutlineColor = new Vector4(c.Red / 255f, c.Green / 255f, c.Blue / 255f, alpha);
            }
        }
        if (details.TryFetchNetInteger(0x23u, out int marginLeft))
            details.MarginLeft = marginLeft;
        ImposeCanonLegacyProjCoda(details);
    }

    private static void ImposeCanonLegacyProjCoda(ElemDetails details)
    {
        if (details.TryFetchNetInteger(0x24u, out int marginRight))
            details.MarginRight = marginRight;
        if (details.TryFetchNetInteger(0x25u, out int marginTop))
            details.MarginTop = marginTop;
        if (details.TryFetchNetInteger(0x26u, out int marginBottom))
            details.MarginBottom = marginBottom;
        details.TabTable = ScanTabChart(details);
        ImposeCanonLegacyProjCoda2(details);
    }

    private static void ImposeCanonLegacyProjCoda2(ElemDetails details)
    {
        details.TemplateList = ScanBlueprintRoster(details);
        details.ScrollbarElementId = ScanReferencedElemIdent(details, 0x72u);
        ImposeCanonLegacyProjCoda3(details);
    }

    private static void ImposeCanonLegacyProjCoda3(ElemDetails details)
    {
        details.LedCheckedSprite = ScanReferencedElemIdent(details, 0x10000082u);
        details.LedUncheckedSprite = ScanReferencedElemIdent(details, 0x10000083u);
        if (details.TryFetchNetBool(0x3Bu, out bool invisible))

            details.Invisible = invisible;
        if (details.TryFetchNetBool(0x4Bu, out bool hintOn))

            details.TooltipEnabled = hintOn;
        ImposeCanonLegacyProjCoda4(details);
    }

    private static void ImposeCanonLegacyProjCoda4(ElemDetails details)
    {
        if (details.TryFetchNetProp(0x49u, out var hintPhrase)
                        && hintPhrase.Kind == WidgetPropertyKind.StringInfo)

            details.TooltipText = hintPhrase.StringInfoValue;
        details.TooltipRootElementId = ScanReferencedElemIdent(details, 0x47u);
        details.TooltipLayoutDid = ScanReferencedElemIdent(details, 0x48u);
        FinishFinishScanReferencedElemIdentPart(details);
    }

    private static void FinishFinishScanReferencedElemIdentPart(ElemDetails details)
    {
        details.TooltipTextChildElementId = ScanReferencedElemIdent(details, 0x4Au);
        if (details.TryFetchNetFloat(0x50u, out float hintDelay))

            details.TooltipDelaySeconds = hintDelay;
        if (details.TryFetchNetInteger(0x3Du, out int upperWidth))
            details.MaxWidth = upperWidth;
        FinishTryFetchNetInteger2(details);
    }

    private static void FinishTryFetchNetInteger2(ElemDetails details)
    {
        if (details.TryFetchNetInteger(0x3Fu, out int lowerWidth))
            details.MinWidth = lowerWidth;
        if (details.TryFetchNetInteger(0x3Cu, out int upperHeight))
            details.MaxHeight = upperHeight;
        if (details.TryFetchNetInteger(0x3Eu, out int lowerHeight))
            details.MinHeight = lowerHeight;
    }
}
