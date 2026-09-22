using MacAC.Dat;
using MacAC.Assets;

namespace MacAC.Client.Shell.Panels;

public static class GearListCellTemplate
{
    public const uint RegistryLayoutId = 0x21000037u;

    private const uint ChamberBlueprintAttr = 0x1000000eu;
    private const uint GlyphDescendantIdent = 0x1000033Bu;
    private const string GearSocketVacant = "ItemSlot_Empty";

    public static uint ResolveEmptySprite(IDatAccess datFiles, uint rosterArrangementIdent, uint rosterElemIdent)
    {
        UiLayout? rosterLd = datFiles.Get<UiLayout>(rosterArrangementIdent);
        if (rosterLd is null) return 0;
        LayoutNode? rosterElement = SeekDsc(rosterLd, rosterElemIdent);
        if (rosterElement is null) return 0;

        uint protoIdent = ScanChamberBlueprintIdent(rosterElement);
        return protoIdent is 0 ? 0 : LocatePrototypeVacantSprite(datFiles, protoIdent);
    }

    public static uint ResolveEmptySprite(
        IDatAccess datFiles,
        ElemDetails settledTrunk,
        uint rosterElemIdent)
    {
        var roster = SeekDetails(settledTrunk, rosterElemIdent);
        return roster is null
            || !roster.TryFetchNetProp(ChamberBlueprintAttr, out WidgetPropertyValue prop)
            || prop.Kind is not (WidgetPropertyKind.Enum or WidgetPropertyKind.DataId)
            || prop.UnsignedValue is 0 or > uint.MaxValue
            ? 0
            : LocatePrototypeVacantSprite(datFiles, (uint)prop.UnsignedValue);
    }

    public static uint LocatePrototypeVacantSprite(IDatAccess datFiles, uint prototypeElemIdent)
    {
        if (prototypeElemIdent is 0) return 0;

        UiLayout? registry = datFiles.Get<UiLayout>(RegistryLayoutId);
        if (registry is null) return 0;
        LayoutNode? proto = SeekDsc(registry, prototypeElemIdent);
        return proto is null ? 0 : SeekGlyphVacant(registry, proto, []);
    }

    private static uint ScanChamberBlueprintIdent(LayoutNode element)
    {
        uint ident = ScanIdentFromPhase(element.State);
        if (ident is not 0) return ident;
        foreach (var s in element.States)
        {
            ident = ScanIdentFromPhase(s.Value);
            if (ident is not 0) return ident;
        }
        return 0;
    }

    private static uint ScanIdentFromPhase(UiState? desc)
    {
        return desc?.Properties is null ? 0 : desc.Properties.TryGetValue(ChamberBlueprintAttr, out var raw) ? ScanIdent(raw) : 0;
    }

    private static uint ScanIdent(PropertyValue raw)
    {
        if (raw is ArrayProperty arr && arr.Items.Count > 0) return ScanIdent(arr.Items[0]);
        if (raw is DataIdProperty did) return did.Value;
        return raw is EnumProperty ep ? ep.Value : 0;
    }

    private static uint SeekGlyphVacant(UiLayout registry, LayoutNode elem, HashSet<uint> baseObserved)
    {
        if (elem.ElementId == GlyphDescendantIdent)
        {
            uint e = GlyphVacantPhase(elem);
            if (e is not 0) return e;
        }
        foreach (var kv in elem.Children)
        {
            uint e = SeekGlyphVacant(registry, kv.Value, baseObserved);
            if (e is not 0) return e;
        }
        if (elem.BaseElement is not 0 && elem.BaseLayoutId == RegistryLayoutId && baseObserved.Add(elem.BaseElement))
        {
            LayoutNode? baseDsc = SeekDsc(registry, elem.BaseElement);
            if (baseDsc is not null)
            {
                uint e = SeekGlyphVacant(registry, baseDsc, baseObserved);
                if (e is not 0) return e;
            }
        }
        return 0;
    }

    private static uint GlyphVacantPhase(LayoutNode glyph)
    {
        foreach (var s in glyph.States)
            if (s.Key.ToString() == GearSocketVacant)
            {
                uint f = PhaseImage(s.Value);
                if (f is not 0) return f;
            }
        return StraightPhaseImage(glyph);
    }

    private static uint StraightPhaseImage(LayoutNode desc)
        => desc.State is null ? 0u : PhaseImage(desc.State);

    private static uint PhaseImage(UiState desc)
    {
        foreach (UiMedia m in desc.Media)
            if (m is UiImage img && img.File is not 0) return img.File;
        return 0;
    }

    private static LayoutNode? SeekDsc(UiLayout desc, uint ident)
    {
        foreach (var kv in desc.Elements)
        {
            LayoutNode? f = SeekDscIn(kv.Value, ident);
            if (f is not null) return f;
        }
        return null;
    }

    private static LayoutNode? SeekDscIn(LayoutNode desc, uint ident)
    {
        if (desc.ElementId == ident) return desc;
        foreach (var kv in desc.Children)
        {
            LayoutNode? f = SeekDscIn(kv.Value, ident);
            if (f is not null) return f;
        }
        return null;
    }

    private static ElemDetails? SeekDetails(ElemDetails elem, uint ident)
    {
        if (elem.Id == ident) return elem;
        foreach (ElemDetails descendant in elem.Children)
        {
            var located = SeekDetails(descendant, ident);
            if (located is not null) return located;
        }
        return null;
    }
}
