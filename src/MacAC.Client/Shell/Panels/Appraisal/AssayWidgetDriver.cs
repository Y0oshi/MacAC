using MacAC.Client.Arcana;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Shell.Panels;

public sealed partial class AssayWidgetDriver : IRetainedPaneDriver
{
    public const uint ArrangementTag = 0x2100006Bu;

    public const uint TrunkTag = 0x100005F2u;

    public const uint ShutTag = 0x100005F3u;

    public const uint BannerIdent = 0x1000012Du;

    public const uint GearBoardIdent = 0x1000012Eu;

    public const uint GearPhraseIdent = 0x1000013Cu;

    public const uint GearScrollerIdent = 0x1000013Du;

    public const uint InscriptionBackgroundIdent = 0x10000137u;

    public const uint InscriptionPhraseIdent = 0x1000013Eu;

    public const uint SignaturePhraseIdent = 0x1000013Fu;

    public const uint InscriptionScrollerIdent = 0x1000046Eu;

    public const uint BeastBoardIdent = 0x10000140u;

    public const uint BeastViewRectIdent = 0x10000148u;

    public const uint BeastStatsRosterIdent = 0x10000149u;

    public const uint BeastExtraRosterIdent = 0x10000335u;

    public const uint BeastReadoutLabelIdent = 0x1000014Eu;

    public const uint BeastTierValIdent = 0x1000014Cu;

    public const uint ArcanumBoardIdent = 0x10000153u;

    public const uint ArcanumSchoolPhraseIdent = 0x1000015Eu;

    public const uint ArcanumGlyphIdent = 0x1000015Fu;

    public const uint ArcanumManaPhraseIdent = 0x10000160u;

    public const uint ArcanumIntervalPhraseIdent = 0x10000161u;

    public const uint ArcanumSpanPhraseIdent = 0x10000162u;

    public const uint ArcanumReadoutPhraseIdent = 0x10000163u;

    public const uint ArcanumEquationRosterIdent = 0x1000032Du;

    private const uint BlueprintStringProp = 5u;

    private const uint ToonMarkerIntProp = 0x105u;

    private const uint DisplayedLabelStringProp = 0x34u;

    private const double BeastRenewSecs = 0.75;

    private readonly ImportedArrangement _arrangement;

    private readonly ClientThingChart _objects;

    private readonly GearDealingDriver _dealing;

    private readonly PickPhase _pick;

    private readonly FightingPhase _fighting;

    private readonly Grimoire _grimoire;

    private readonly Func<string> _avatarLabel;

    private readonly Action<uint, string> _transmitSetInscription;

    private readonly Action<string> _sysMsg;

    private readonly Action _unhide;

    private readonly Action _shutPane;

    private readonly WidgetElem _gearBoard;

    private readonly WidgetElem _beastBoard;

    private readonly WidgetElem _arcanumBoard;

    private readonly WidgetPhrase _banner;

    private readonly WidgetPhrase _gearPhrase;

    private readonly WidgetPhrase? _inscriptionPhrase;

    private readonly WidgetField? _inscriptionField;

    private readonly WidgetPhrase? _signature;

    private readonly WidgetDatElement? _inscriptionBackground;

    private readonly WidgetBtn? _shut;

    private readonly CreatureAssayLayeredList? _beastStats;

    private readonly CreatureAssayLayeredList? _beastExtra;

    private readonly CreatureAssayRowTemplateMint? _beastRankBlueprints;

    private readonly CreatureDisplayNamePicker _beastLabels;

    private readonly CanonAssayNamePicker _gearLabels;

    private readonly Func<uint, uint> _locateArcanumGlyph;

    private readonly Func<uint, uint> _locateModuleGlyph;

    private readonly Func<uint, IReadOnlyList<ArcanaExamineComponent>> _arcanumModules;

    private readonly Func<MechMagicSchool, uint> _magicAptitude;

    private readonly Func<uint, string?> _locateToonBanner;

    private readonly Func<int> _ownFactionBitset;

    private readonly ArcanaExamineComponentTemplateMint? _arcanumModuleBlueprints;

    private readonly WidgetPhrase _arcanumSchool;

    private readonly WidgetPhrase _arcanumMana;

    private readonly WidgetPhrase _arcanumInterval;

    private readonly WidgetPhrase _arcanumSpan;

    private readonly WidgetPhrase _arcanumReadout;

    private readonly WidgetElem _arcanumGlyphHub;

    private readonly WidgetElem _arcanumEquationHub;

    private readonly WidgetTextureElement _arcanumGlyph;

    private readonly List<WidgetElem> _arcanumEquationChambers = [];

    private readonly Dictionary<WidgetPhrase, WidgetTextArrangementShelf<string>>
        _phraseArrangements = [];

    private WidgetTextArrangementShelf<GearAssayDigest> _gearPhraseArrangement = null!;

    private readonly float _arcanumEquationOriginX;

    private readonly float _arcanumEquationChamberWidth;
    private uint _gearObjectIdent;

    private uint _beastObjectIdent;

    private uint _toonObjectIdent;

    private string _bannerVal = string.Empty;

    private GearAssayDigest _gearDossier = GearAssayDigest.Empty;

    private string _inscriptionVal = string.Empty;

    private string _signatureVal = string.Empty;

    private string _scribeLabel = string.Empty;

    private string _formerInscription = string.Empty;

    private bool _exhibitInscribable;

    private uint _arcanumIdent;

    private bool _paneShown;

    private double _renewPassed;

    private bool _destroyed;

    private AssayWidgetDriver(
        ImportedArrangement arrangement,
        ClientThingChart objects,
        GearDealingDriver dealing,
        PickPhase pick,
        FightingPhase fighting,
        Grimoire grimoire,
        Func<string> avatarLabel,
        Action<uint, string> transmitSetInscription,
        Action<string> sysMsg,
        Action unhide,
        Action shut,
        WidgetElem gearBoard,
        WidgetElem beastBoard,
        WidgetElem arcanumBoard,
        WidgetPhrase banner,
        WidgetPhrase gearPhrase,
        CreatureAssayRowTemplateMint? beastRankBlueprints,
        CreatureDisplayNamePicker? beastLabels,
        CanonAssayNamePicker? gearLabels,
        Func<uint, uint>? locateArcanumGlyph,
        Func<uint, uint>? locateModuleGlyph,
        Func<uint, IReadOnlyList<ArcanaExamineComponent>>? arcanumModules,
        Func<MechMagicSchool, uint>? magicAptitude,
        ArcanaExamineComponentTemplateMint? arcanumModuleBlueprints,
        Func<uint, string?>? locateToonBanner,
        Func<int>? ownFactionBitset)
    {
        _arrangement = arrangement;
        _objects = objects;
        _dealing = dealing;
        _pick = pick;
        _fighting = fighting;
        _grimoire = grimoire;
        _avatarLabel = avatarLabel;
        _transmitSetInscription = transmitSetInscription;
        _sysMsg = sysMsg;
        _unhide = unhide;
        _shutPane = shut;
        _gearBoard = gearBoard;
        _beastBoard = beastBoard;
        _arcanumBoard = arcanumBoard;
        _banner = banner;
        _gearPhrase = gearPhrase;
        _beastRankBlueprints = beastRankBlueprints;
        _beastLabels = beastLabels
            ?? new CreatureDisplayNamePicker(
                new Dictionary<uint, string>());
        _gearLabels = gearLabels ?? CanonAssayNamePicker.Empty;
        _locateArcanumGlyph = locateArcanumGlyph ?? (_ => 0u);
        _locateModuleGlyph = locateModuleGlyph ?? (_ => 0u);
        _arcanumModules = arcanumModules ?? (_ => []);
        _magicAptitude = magicAptitude ?? (_ => 0u);
        _locateToonBanner = locateToonBanner ?? (_ => null);
        _ownFactionBitset = ownFactionBitset ?? (() => 0);
        _arcanumModuleBlueprints = arcanumModuleBlueprints;
        _arcanumSchool = (WidgetPhrase)arrangement.SeekElem(ArcanumSchoolPhraseIdent)!;
        _arcanumMana = (WidgetPhrase)arrangement.SeekElem(ArcanumManaPhraseIdent)!;
        _arcanumInterval = (WidgetPhrase)arrangement.SeekElem(ArcanumIntervalPhraseIdent)!;
        _arcanumSpan = (WidgetPhrase)arrangement.SeekElem(ArcanumSpanPhraseIdent)!;
        _arcanumReadout = (WidgetPhrase)arrangement.SeekElem(ArcanumReadoutPhraseIdent)!;
        _arcanumGlyphHub = arrangement.SeekElem(ArcanumGlyphIdent)!;
        _arcanumEquationHub = arrangement.SeekElem(ArcanumEquationRosterIdent)!;
        _arcanumEquationOriginX = _arcanumEquationHub.Left;
        _arcanumEquationChamberWidth = _arcanumEquationHub.Width;
        _inscriptionPhrase = arrangement.SeekElem(InscriptionPhraseIdent) as WidgetPhrase;
        _inscriptionField = arrangement.SeekElem(InscriptionPhraseIdent) as WidgetField;
        _signature = arrangement.SeekElem(SignaturePhraseIdent) as WidgetPhrase;
        _inscriptionBackground =
            arrangement.SeekElem(InscriptionBackgroundIdent) as WidgetDatElement;
        _shut = arrangement.SeekElem(ShutTag) as WidgetBtn;

        AttachPhraseSrc(
            _banner,
            () => _bannerVal,
            static (mark, val) =>
                [new WidgetPhrase.Line(val, mark.DefaultTint)]);
        _gearPhrase.VerticalJustify = ClientVJustify.Top;
        ConfigureScrollableGearPhrase(_gearPhrase, GearScrollerIdent);
        if (_inscriptionPhrase is not null)
            ConfigureScrollablePhrase(
                _inscriptionPhrase,
                InscriptionScrollerIdent,
                () => _inscriptionVal);
        if (_inscriptionField is not null)
        {
            _inscriptionField.AssignPhrase(string.Empty);
            _inscriptionField.WipeOnSubmit = false;
            _inscriptionField.CaptureHistory = false;
            _inscriptionField.Editable = false;
            _inscriptionField.Selectable = false;
            _inscriptionField.OnFocusGained = ProcessInscriptionGainingFocus;
            _inscriptionField.OnFocusLost = ProcessInscriptionLosingFocus;
            _inscriptionField.OnScanSolePress = AnnounceInscriptionUnavailable;
            if (arrangement.SeekElem(InscriptionScrollerIdent) is WidgetScroller scroller)
                scroller.Model = _inscriptionField.Scroll;
        }
        if (_signature is not null)
        {
            AttachPhraseSrc(
                _signature,
                () => _signatureVal,
                static (mark, val) =>
                    [new WidgetPhrase.Line(val, mark.DefaultTint)]);
        }
        _shut?.OnClick = shut;
        if (_inscriptionBackground is not null)
        {
            _inscriptionBackground.OnClick = AnnounceInscriptionUnavailable;
            _inscriptionBackground.ClickThrough = false;
        }

        if (_arcanumGlyphHub is WidgetDatElement arcanumGlyphDat)
            arcanumGlyphDat.MediaShown = false;
        _arcanumGlyph = new WidgetTextureElement
        {
            Width = _arcanumGlyphHub.Width,
            Height = _arcanumGlyphHub.Height,
            Moorings = MooringRims.Left | MooringRims.Top
                | MooringRims.Right | MooringRims.Bottom,
        };
        _arcanumGlyphHub.AddChild(_arcanumGlyph);
        ConfigureArcanumPhrase(_arcanumSchool);
        ConfigureArcanumPhrase(_arcanumMana);
        ConfigureArcanumPhrase(_arcanumInterval);
        ConfigureArcanumPhrase(_arcanumSpan);
        ConfigureArcanumPhrase(_arcanumReadout, enclose: true);

        if (beastRankBlueprints is not null
            && arrangement.SeekElem(BeastStatsRosterIdent) is { } statsHub
            && arrangement.SeekElem(BeastExtraRosterIdent) is { } extraHub
            && arrangement.SeekElem(BeastViewRectIdent) is WidgetViewport viewRect)
        {
            _beastStats = CreatureAssayLayeredList.Create(
                _beastBoard,
                statsHub,
                viewRect,
                beastRankBlueprints,
                backgroundZOrdering: viewRect.ZOrder - 2);
            _beastExtra = CreatureAssayLayeredList.Create(
                _beastBoard,
                extraHub,
                viewRect,
                beastRankBlueprints,
                backgroundZOrdering: viewRect.ZOrder - 1);
        }

        _pick.Changed += ProcessPickAltered;
        _objects.ObjectAdded += ProcessArcanumModuleObjectAltered;
        _objects.ObjectMoved += ProcessArcanumModuleObjectMoved;
        _objects.ObjectRemoved += ProcessArcanumModuleObjectAltered;
        _objects.StackSizeUpdated += ProcessArcanumModuleObjectAltered;
        _objects.Cleared += ProcessArcanumModuleObjectsCleared;
        AssignEngagedLens(AssayView.Item);
    }
}

public enum AssayView
{
    Item,
    Creature,
    Character,
    Spell,
}
