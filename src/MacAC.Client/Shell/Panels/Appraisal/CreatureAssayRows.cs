using System.Numerics;
using MacAC.Dat;
using MacAC.Assets;
using MacAC.Mechanics.Traits;

namespace MacAC.Client.Shell.Panels;

public enum CreatureAssayValueStyle
{
    Normal,
    Positive,
    Negative,
    Incomplete,
}

public readonly record struct CreatureAssayRow(
    string Label,
    string Value,
    CreatureAssayValueStyle Style);

public enum CreatureAssayRowLayer
{
    Combined,
    Background,
    Foreground,
}

public static partial class CreatureAssayRows
{
    private const string Unknown = "???";

    private const uint DamageRating = 0x133u;

    private const uint DamageResistRating = 0x134u;

    private const uint CritRating = 0x139u;

    private const uint CritDamageRating = 0x13Au;

    private const uint CritResistRating = 0x13Bu;

    private const uint CritDamageResistRating = 0x13Cu;

    private const uint HealingBoostRating = 0x143u;

    private const uint DotResistRating = 0x15Eu;

    private const uint LifeResistRating = 0x15Fu;

    private const uint Faction1BitsetProp = (uint)TraitInt.Faction1Bits; // 281

    private const uint SocietyGradeCelestialHandProp =
        (uint)TraitInt.SocietyRankCelhan; // 287

    private const uint SocietyGradeEldrytchWebProp =
        (uint)TraitInt.SocietyRankEldweb; // 288

    private const uint SocietyGradeRadiantBloodProp =
        (uint)TraitInt.SocietyRankRadblo; // 289

    private const int CelestialHandBit = 0x1;

    private const int EldrytchWebBit = 0x2;

    private const int RadiantBloodBit = 0x4;

    private const int SocietyBitsetBitmask =
        CelestialHandBit | EldrytchWebBit | RadiantBloodBit;

    private const uint AllegianceGradeProp = (uint)TraitInt.AllegianceRank; // 30

    private const uint AllegianceFollowersProp =
        (uint)TraitInt.AllegianceFollowers; // 35 (Int table)

    private const uint MonarchsBannerProp =
        (uint)PropString.MonarchsTitle; // 21 (String table)

    private const uint PatronsBannerProp =
        (uint)PropString.PatronsTitle; // 35 (String table)

    private const uint FellowshipProp = (uint)PropString.Fellowship; // 10

    private const uint DateOfBirthProp = (uint)PropString.DateOfBirth; // 43

    private const uint AgeProp = (uint)TraitInt.Age; // 125

    private const uint ChessGradeProp = (uint)TraitInt.ChessRank; // 181

    private const uint FishingAptitudeProp =
        (uint)TraitInt.FakeFishingSkill; // 192

    private const uint CountDeathsProp = (uint)TraitInt.NumDeaths; // 43 (Int table)

    private const uint CountToonBannersProp =
        (uint)TraitInt.NumCharacterTitles; // 262

    private const int UnenchantableArmorTier = 9999;
}

public sealed class CreatureAssayRowTemplateMint(
    ElemDetails template,
    Func<uint, (uint Texture, int Width, int Height)> resolveSprite,
    WidgetDatFont? defaultTypeface,
    IReadOnlyDictionary<uint, WidgetDatFont?>? typefaces = null)
{
    public const uint BlueprintTag = 0x10000166u;
    public const uint CaptionTag = 0x1000012Au;
    public const uint ValIdent = 0x1000012Bu;

    private readonly ElemDetails _blueprint = template ?? throw new ArgumentNullException(nameof(template));
    private readonly Func<uint, (uint Texture, int Width, int Height)> _locateSprite = resolveSprite ?? throw new ArgumentNullException(nameof(resolveSprite));
    private readonly WidgetDatFont? _defaultTypeface = defaultTypeface;
    private readonly IReadOnlyDictionary<uint, WidgetDatFont?> _typefaces = typefaces ?? new Dictionary<uint, WidgetDatFont?>();

    public float Width => _blueprint.Width;
    public float Height => _blueprint.Height;

    public static CreatureAssayRowTemplateMint? TryLoad(
        IDatAccess datFiles,
        Func<uint, (uint Texture, int Width, int Height)> locateSprite,
        WidgetDatFont? defaultTypeface,
        Func<uint, WidgetDatFont?>? locateTypeface)
    {
        var blueprint = ArrangementLoader.ImportInfos(
            datFiles,
            AssayWidgetDriver.ArrangementTag,
            BlueprintTag);
        if (blueprint is null
            || Find(blueprint, CaptionTag) is null
            || Find(blueprint, ValIdent) is null
            || blueprint.Width <= 0f
            || blueprint.Height <= 0f)

            return null;

        var typefaces = new Dictionary<uint, WidgetDatFont?>();
        GrabTypefaces(blueprint, locateTypeface, typefaces);
        return new CreatureAssayRowTemplateMint(
            blueprint,
            locateSprite,
            defaultTypeface,
            typefaces);
    }

    public WidgetTemplateListSlot Create(
        CreatureAssayRow rank,
        CreatureAssayRowLayer stratum = CreatureAssayRowLayer.Combined)
    {
        var substance = ArrangementLoader.Build(
            _blueprint,
            _locateSprite,
            _defaultTypeface,
            did => _typefaces.TryGetValue(did, out WidgetDatFont? typeface)
                ? typeface
                : _defaultTypeface);
        WidgetPhrase caption = Required<WidgetPhrase>(substance, CaptionTag);
        WidgetPhrase val = Required<WidgetPhrase>(substance, ValIdent);
        if (stratum == CreatureAssayRowLayer.Background)
        {
            caption.StrokesSupplier = static () => Array.Empty<WidgetPhrase.Line>();
            val.StrokesSupplier = static () => Array.Empty<WidgetPhrase.Line>();
        }
        else
        {
            caption.StrokesSupplier = () =>
                [new WidgetPhrase.Line(rank.Label, caption.DefaultTint)];
            val.StrokesSupplier = () =>
                [new WidgetPhrase.Line(rank.Value, LocateTint(rank.Style, val.DefaultTint))];
        }

        if (stratum == CreatureAssayRowLayer.Foreground
            && substance.Root is WidgetDatElement trunk)

            trunk.MediaShown = false;

        return new WidgetTemplateListSlot(
            substance,
            listingIdent: 0u,
            substance.Root is IWidgetDatStateful stateful
                ? stateful.EngagedCanonPhaseIdent
                : WidgetStateInfo.StraightPhaseIdent,
            chosenPhase: null);
    }

    private static Vector4 LocateTint(
        CreatureAssayValueStyle styling,
        Vector4 norm)
    {
        _ = styling;
        return norm;
    }

    private static T Required<T>(ImportedArrangement substance, uint ident)
        where T : WidgetElem
    {
        return substance.SeekElem(ident) as T
                ?? throw new InvalidOperationException(
                    $"Retail creature appraisal template element 0x{ident:X8} didn't resolve to {typeof(T).Name}.");
    }

    private static ElemDetails? Find(ElemDetails trunk, uint ident)
    {
        if (trunk.Id == ident)
            return trunk;
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

public sealed class CreatureAssayLayeredList
{
    public const float PhraseInset = 8f;

    private readonly CreatureAssayRowTemplateMint _blueprints;

    private CreatureAssayLayeredList(
        CreatureAssayRowTemplateMint blueprints,
        WidgetGearRoster background,
        WidgetGearRoster foreground)
    {
        _blueprints = blueprints;
        Background = background;
        Foreground = foreground;
    }

    public WidgetGearRoster Background { get; }
    public WidgetGearRoster Foreground { get; }

    public static CreatureAssayLayeredList Create(
        WidgetElem board,
        WidgetElem backgroundHub,
        WidgetViewport viewRect,
        CreatureAssayRowTemplateMint blueprints,
        int backgroundZOrdering)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(backgroundHub);
        ArgumentNullException.ThrowIfNull(viewRect);
        ArgumentNullException.ThrowIfNull(blueprints);

        int foregroundZOrdering = backgroundHub.ZOrder;
        backgroundHub.ZOrder = backgroundZOrdering;
        WidgetScrollable roll = new WidgetScrollable();
        WidgetGearRoster background = NewRoster(
            left: 0f,
            top: 0f,
            width: blueprints.Width,
            height: backgroundHub.Height,
            zOrdering: 0,
            roll: roll);
        background.ClickThrough = true;
        backgroundHub.AddChild(background);

        WidgetGearRoster foreground = NewRoster(
            left: backgroundHub.Left + PhraseInset,
            top: backgroundHub.Top,
            width: blueprints.Width,
            height: backgroundHub.Height,
            zOrdering: foregroundZOrdering,
            roll: roll);
        foreground.Moorings = MooringRims.Left | MooringRims.Top
            | MooringRims.Right | MooringRims.Bottom;
        board.AddChild(foreground);

        return new CreatureAssayLayeredList(
            blueprints,
            background,
            foreground);
    }

    public void Rebuild(IReadOnlyList<CreatureAssayRow> ranks)
    {
        ArgumentNullException.ThrowIfNull(ranks);
        using (Background.DeferArrangement())
        using (Foreground.DeferArrangement())
        {
            Background.Flush();
            Foreground.Flush();
            foreach (CreatureAssayRow rank in ranks)
            {
                Background.AddItem(_blueprints.Create(
                    rank,
                    CreatureAssayRowLayer.Background));
                Foreground.AddItem(_blueprints.Create(
                    rank,
                    CreatureAssayRowLayer.Foreground));
            }
        }
    }

    public void Flush()
    {
        Background.Flush();
        Foreground.Flush();
    }

    public void RestartRoll() => Foreground.Scroll.AssignRollY(0);

    private static WidgetGearRoster NewRoster(
        float left,
        float top,
        float width,
        float height,
        int zOrdering,
        WidgetScrollable roll)
    {
        return new WidgetGearRoster(roll: roll)
        {
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            ZOrder = zOrdering,
            Columns = 1,
            ChamberWidth = width,
            ChamberHeight = 20f,
            Moorings = MooringRims.Left | MooringRims.Top,
        };
    }
}

public sealed class CreatureDisplayNamePicker(
    IReadOnlyDictionary<uint, string> names)
{
    public const uint MapperDid = 0x2200000Eu;
    private readonly IReadOnlyDictionary<uint, string> _labels = names ?? throw new ArgumentNullException(nameof(names));

    public static CreatureDisplayNamePicker Load(IDatAccess datFiles)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        var labels = new Dictionary<uint, string>();
        HashSet<uint> visited = new HashSet<uint>();
        uint did = MapperDid;
        while (did is not 0u && visited.Add(did))
        {
            NameMap? mapper = datFiles.Get<NameMap>(did);
            if (mapper is null)
                break;
            foreach ((uint ident, string phrase) in mapper.Names)
                labels.TryAdd(ident, phrase.Replace('_', ' '));
            did = mapper.BaseMapId;
        }
        return new CreatureDisplayNamePicker(labels);
    }

    public string Resolve(int beastKind)
    {
        return beastKind > 0
                && _labels.TryGetValue((uint)beastKind, out string? label)
                    ? label
                    : string.Empty;
    }
}
