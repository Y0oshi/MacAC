using MacAC.Assets;

namespace MacAC.Client.Shell.Panels;

public sealed class ComponentBookTemplateMint(
    ElemDetails bucketBlueprint,
    ElemDetails moduleBlueprint,
    Func<uint, (uint tex, int w, int h)> locateSprite,
    WidgetDatFont? defaultTypeface,
    IReadOnlyDictionary<uint, WidgetDatFont?>? typefaces = null,
    IReadOnlyList<string>? bucketLabels = null)
{
    public const uint LayoutId = 0x21000033u;
    public const uint BucketBlueprintIdent = 0x10000466u;
    public const uint ModuleBlueprintIdent = 0x10000467u;
    public const uint IconId = 0x10000468u;
    public const uint LabelIdent = 0x10000469u;
    public const uint PossessedTallyIdent = 0x1000046Au;
    public const uint WantedTallyIdent = 0x1000046Bu;

    private const uint OwnStringChartIdent = 0x23000001u;
    private const uint HighlightPhase = WidgetButtonStateMachine.Highlight;

    private static readonly (string Key, string Fallback)[] BucketTexts =
    [
        ("ID_SpellComp_Category_Scarabs", "SCARABS"),
        ("ID_SpellComp_Category_Herbs", "HERBS"),
        ("ID_SpellComp_Category_Gems", "POWDERED GEMS"),
        ("ID_SpellComp_Category_Alchemical", "ALCHEMICAL SUBSTANCES"),
        ("ID_SpellComp_Category_Talismans", "TALISMANS"),
        ("ID_SpellComp_Category_Tapers", "TAPERS"),
        ("ID_SpellComp_Category_Peas", "PEAS"),
    ];

    private readonly ElemDetails _bucketBlueprint = bucketBlueprint;
    private readonly ElemDetails _moduleBlueprint = moduleBlueprint;
    private readonly Func<uint, (uint tex, int w, int h)> _locateSprite = locateSprite;
    private readonly WidgetDatFont? _defaultTypeface = defaultTypeface;
    private readonly IReadOnlyDictionary<uint, WidgetDatFont?> _typefaces = typefaces ?? new Dictionary<uint, WidgetDatFont?>();
    private readonly string[] _bucketLabels = bucketLabels?.ToArray()
            ?? [.. BucketTexts.Select(val => val.Fallback)];

    public static ComponentBookTemplateMint? TryLoad(
        IDatAccess datFiles,
        Func<uint, (uint tex, int w, int h)> locateSprite,
        WidgetDatFont? defaultTypeface,
        Func<uint, WidgetDatFont?>? locateTypeface)
    {
        var bucket = ArrangementLoader.ImportInfos(
            datFiles, LayoutId, BucketBlueprintIdent);
        var component = ArrangementLoader.ImportInfos(
            datFiles, LayoutId, ModuleBlueprintIdent);
        if (bucket is null || component is null)
            return null;

        var typefaces = new Dictionary<uint, WidgetDatFont?>();
        GrabTypefaces(bucket, locateTypeface, typefaces);
        GrabTypefaces(component, locateTypeface, typefaces);

        DatStringPicker texts = new DatStringPicker(datFiles);
        string[] bucketLabels = [.. BucketTexts
            .Select(val => texts.Resolve(
                    OwnStringChartIdent,
                    DatStringPicker.CalculateDigest(val.Key))
                ?? val.Fallback)];

        return new ComponentBookTemplateMint(
            bucket, component, locateSprite, defaultTypeface, typefaces, bucketLabels);
    }

    public WidgetTemplateListSlot BuildBucketRank(uint bucket)
    {
        var substance = Build(_bucketBlueprint);
        if (substance.Root is not WidgetPhrase banner)
            throw new InvalidOperationException(
                "Retail component category template didn't resolve to UIElement_Text");

        string caption = bucket < _bucketLabels.Length
            ? _bucketLabels[bucket]
            : "OTHER COMPONENTS";
        banner.StrokesSupplier = () => [new WidgetPhrase.Line(caption, banner.DefaultTint)];

        return new WidgetTemplateListSlot(
            substance, uint.MaxValue, substance.Root is IWidgetDatStateful stateful
                ? stateful.EngagedCanonPhaseIdent
                : WidgetStateInfo.StraightPhaseIdent, chosenPhase: null);
    }

    public ModuleRank BuildModuleRank(
        uint moduleIdent,
        uint glyphTexture,
        string label,
        int possessedTally,
        uint wantedTally)
    {
        var substance = Build(_moduleBlueprint);
        WidgetElem glyphHub = Required(substance, IconId);
        WidgetPhrase labelPhrase = Required<WidgetPhrase>(substance, LabelIdent);
        WidgetPhrase possessedPhrase = Required<WidgetPhrase>(substance, PossessedTallyIdent);
        WidgetField wantedField = Required<WidgetField>(substance, WantedTallyIdent);

        WidgetTextureElement glyph = new WidgetTextureElement
        {
            Width = glyphHub.Width,
            Height = glyphHub.Height,
            Moorings = MooringRims.Left | MooringRims.Top
                | MooringRims.Right | MooringRims.Bottom,
            Texture = glyphTexture,
        };
        glyphHub.AddChild(glyph);

        labelPhrase.StrokesSupplier = () => [new WidgetPhrase.Line(label, labelPhrase.DefaultTint)];
        possessedPhrase.StrokesSupplier = () =>
            [new WidgetPhrase.Line(possessedTally.ToString(System.Globalization.CultureInfo.InvariantCulture),
                possessedPhrase.DefaultTint)];

        wantedField.WipeOnSubmit = false;
        wantedField.CaptureHistory = false;
        wantedField.PickAllOnFocus = true;
        wantedField.ToonSift = char.IsAsciiDigit;
        wantedField.AssignPhrase(wantedTally.ToString(
            System.Globalization.CultureInfo.InvariantCulture));

        uint normPhase = substance.Root is IWidgetDatStateful stateful
            ? stateful.EngagedCanonPhaseIdent
            : WidgetStateInfo.StraightPhaseIdent;
        WidgetTemplateListSlot socket = new WidgetTemplateListSlot(
            substance, moduleIdent, normPhase, HighlightPhase);
        return new ModuleRank(socket, wantedField);
    }

    private ImportedArrangement Build(ElemDetails blueprint)
    {
        return ArrangementLoader.Build(
                blueprint,
                _locateSprite,
                _defaultTypeface,
                did => _typefaces.TryGetValue(did, out WidgetDatFont? typeface) ? typeface : _defaultTypeface);
    }

    private static T Required<T>(ImportedArrangement substance, uint ident)
        where T : WidgetElem
    {
        return substance.SeekElem(ident) as T
                ?? throw new InvalidOperationException(
                    $"Retail component template element 0x{ident:X8} didn't resolve to {typeof(T).Name}.");
    }

    private static WidgetElem Required(ImportedArrangement substance, uint ident)
    {
        return substance.SeekElem(ident)
                ?? throw new InvalidOperationException(
                    $"Retail component template element 0x{ident:X8} is absent");
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

    public readonly record struct ModuleRank(
        WidgetTemplateListSlot Slot,
        WidgetField DesiredField);
}
