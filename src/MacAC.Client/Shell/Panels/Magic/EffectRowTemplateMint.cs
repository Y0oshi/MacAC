using MacAC.Assets;

namespace MacAC.Client.Shell.Panels;

public sealed class EffectRowTemplateMint(
    ElemDetails template,
    Func<uint, (uint tex, int w, int h)> resolveSprite,
    WidgetDatFont? defaultTypeface,
    IReadOnlyDictionary<uint, WidgetDatFont?>? typefaces = null)
{
    private readonly ElemDetails _blueprint = template ?? throw new ArgumentNullException(nameof(template));
    private readonly Func<uint, (uint tex, int w, int h)> _locateSprite = resolveSprite ?? throw new ArgumentNullException(nameof(resolveSprite));
    private readonly WidgetDatFont? _defaultTypeface = defaultTypeface;
    private readonly IReadOnlyDictionary<uint, WidgetDatFont?> _typefaces = typefaces ?? new Dictionary<uint, WidgetDatFont?>();

    public float Width => _blueprint.Width;
    public float Height => _blueprint.Height;

    public static EffectRowTemplateMint? TryLoad(
        IDatAccess datFiles,
        Func<uint, (uint tex, int w, int h)> locateSprite,
        WidgetDatFont? defaultTypeface,
        Func<uint, WidgetDatFont?>? locateTypeface)
    {
        var blueprint = ArrangementLoader.ImportInfos(
            datFiles,
            EffectsWidgetDriver.LayoutId,
            EffectsWidgetDriver.RankBlueprintIdent);
        if (blueprint is null
            || Find(blueprint, EffectsWidgetDriver.RankGlyphIdent) is null
            || Find(blueprint, EffectsWidgetDriver.RankCaptionIdent) is null
            || Find(blueprint, EffectsWidgetDriver.RankIntervalIdent) is null
            || blueprint.Width <= 0f
            || blueprint.Height <= 0f)
            return null;

        var typefaces = new Dictionary<uint, WidgetDatFont?>();
        GrabTypefaces(blueprint, locateTypeface, typefaces);
        return new EffectRowTemplateMint(
            blueprint, locateSprite, defaultTypeface, typefaces);
    }

    public FxRank Create(
        uint arcanumIdent,
        uint glyphTexture,
        string moniker,
        string leftover)
    {
        var substance = ArrangementLoader.Build(
            _blueprint,
            _locateSprite,
            _defaultTypeface,
            did => _typefaces.TryGetValue(did, out WidgetDatFont? typeface)
                ? typeface
                : _defaultTypeface);
        WidgetElem glyphHub = Required(substance, EffectsWidgetDriver.RankGlyphIdent);
        WidgetPhrase caption = Required<WidgetPhrase>(substance, EffectsWidgetDriver.RankCaptionIdent);
        WidgetPhrase interval = Required<WidgetPhrase>(substance, EffectsWidgetDriver.RankIntervalIdent);

        glyphHub.AddChild(new WidgetTextureElement
        {
            Width = glyphHub.Width,
            Height = glyphHub.Height,
            Moorings = MooringRims.Left | MooringRims.Top
                | MooringRims.Right | MooringRims.Bottom,
            Texture = glyphTexture,
        });
        caption.StrokesSupplier = () => [new WidgetPhrase.Line(moniker, caption.DefaultTint)];

        WidgetTemplateListSlot socket = new WidgetTemplateListSlot(
            substance,
            arcanumIdent,
            WidgetButtonStateMachine.Normal,
            WidgetButtonStateMachine.Highlight);
        return new FxRank(socket, interval, leftover);
    }

    public sealed class FxRank
    {
        private readonly WidgetPhrase _interval;

        internal FxRank(
            WidgetTemplateListSlot socket,
            WidgetPhrase interval,
            string leftover)
        {
            Slot = socket;
            _interval = interval;
            Remaining = leftover;
            _interval.StrokesSupplier = () =>
                [new WidgetPhrase.Line(Remaining, _interval.DefaultTint)];
        }

        public WidgetTemplateListSlot Slot { get; }

        public string Remaining { get; set; }
    }

    private static T Required<T>(ImportedArrangement substance, uint ident)
        where T : WidgetElem
    {
        return substance.SeekElem(ident) as T
                ?? throw new InvalidOperationException(
                    $"Retail effect template element 0x{ident:X8} didn't resolve to {typeof(T).Name}.");
    }

    private static WidgetElem Required(ImportedArrangement substance, uint ident)
    {
        return substance.SeekElem(ident)
                ?? throw new InvalidOperationException(
                    $"Retail effect template element 0x{ident:X8} is absent");
    }

    private static ElemDetails? Find(ElemDetails trunk, uint ident)
    {
        if (trunk.Id == ident) return trunk;
        foreach (ElemDetails descendant in trunk.Children)
            if (Find(descendant, ident) is { } located)
                return located;
        return null;
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
