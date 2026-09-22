using MacAC.Assets;

namespace MacAC.Client.Shell.Panels;

public sealed class ArcanaExamineComponentTemplateMint(
    ElemDetails template,
    Func<uint, (uint tex, int w, int h)> resolveSprite,
    WidgetDatFont? defaultTypeface,
    IReadOnlyDictionary<uint, WidgetDatFont?>? typefaces = null)
{
    public const uint BlueprintIdent = 0x1000032Eu;
    public const uint AbsentTopLayerIdent = 0x10000330u;

    private readonly ElemDetails _blueprint = template ?? throw new ArgumentNullException(nameof(template));
    private readonly Func<uint, (uint tex, int w, int h)> _locateSprite = resolveSprite ?? throw new ArgumentNullException(nameof(resolveSprite));
    private readonly WidgetDatFont? _defaultTypeface = defaultTypeface;
    private readonly IReadOnlyDictionary<uint, WidgetDatFont?> _typefaces = typefaces ?? new Dictionary<uint, WidgetDatFont?>();

    public static ArcanaExamineComponentTemplateMint? TryLoad(
        IDatAccess datFiles,
        Func<uint, (uint tex, int w, int h)> locateSprite,
        WidgetDatFont? defaultTypeface,
        Func<uint, WidgetDatFont?>? locateTypeface)
    {
        var blueprint = ArrangementLoader.ImportInfos(
            datFiles,
            AssayWidgetDriver.ArrangementTag,
            BlueprintIdent);
        if (blueprint is null)
            return null;

        var typefaces = new Dictionary<uint, WidgetDatFont?>();
        GrabTypefaces(blueprint, locateTypeface, typefaces);
        return new ArcanaExamineComponentTemplateMint(
            blueprint,
            locateSprite,
            defaultTypeface,
            typefaces);
    }

    public WidgetElem Create(uint glyphTexture, bool possessed)
    {
        var substance = ArrangementLoader.Build(
            _blueprint,
            _locateSprite,
            _defaultTypeface,
            did => _typefaces.TryGetValue(did, out WidgetDatFont? typeface)
                ? typeface
                : _defaultTypeface);
        if (substance.Root is not WidgetDatElement trunk)
        {
            throw new InvalidOperationException(
                $"Retail spell-component template 0x{BlueprintIdent:X8} "
                + "must resolve to a UIRegion-compatible element");
        }
        trunk.CoreImageTexture = glyphTexture;

        if (substance.SeekElem(AbsentTopLayerIdent) is { } absent)
            absent.Visible = !possessed;
        return trunk;
    }

    private static void GrabTypefaces(
        ElemDetails details,
        Func<uint, WidgetDatFont?>? locateTypeface,
        Dictionary<uint, WidgetDatFont?> typefaces)
    {
        if (details.FontDid is not 0u && !typefaces.ContainsKey(details.FontDid))
            typefaces[details.FontDid] = locateTypeface?.Invoke(details.FontDid);
        foreach (ElemDetails descendant in details.Children)
            GrabTypefaces(descendant, locateTypeface, typefaces);
    }
}
