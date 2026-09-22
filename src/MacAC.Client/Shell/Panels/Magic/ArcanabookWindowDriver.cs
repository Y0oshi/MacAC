using MacAC.Assets;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Shell.Panels;

public enum ArcanabookWindowPage { Spells, Components }

public sealed partial class ArcanabookWindowDriver : IRetainedPaneDriver
{
    public const uint ArrangementIdent = 0x21000034u;

    public const uint TrunkIdent = 0x100002A8u;

    public const uint ArcanumTabIdent = 0x100002A9u;

    public const uint ModuleTabIdent = 0x100002AAu;

    public const uint ShutIdent = 0x100002ABu;

    public const uint ArcanumSheetIdent = 0x100002ACu;

    public const uint ModuleSheetIdent = 0x100002ADu;

    public const uint ArcanumRosterIdent = 0x10000295u;

    public const uint ArcanumScrollerIdent = 0x10000296u;

    public const uint EraseBtnIdent = 0x100002A5u;

    public const uint ModuleRosterIdent = 0x10000464u;

    public const uint ModuleScrollerIdent = 0x10000465u;

    private static readonly (uint Id, uint Mask)[] SiftBtns =
    [
        (0x10000298u, 0x0001u), (0x10000299u, 0x0002u),
        (0x1000029Au, 0x0004u), (0x1000029Bu, 0x0008u),
        (0x100005C0u, 0x2000u),
        (0x1000029Cu, 0x0010u), (0x1000029Du, 0x0020u),
        (0x1000029Eu, 0x0040u), (0x1000029Fu, 0x0080u),
        (0x100002A0u, 0x0100u), (0x100002A1u, 0x0200u),
        (0x100002A2u, 0x0400u), (0x1000054Eu, 0x0800u),
    ];

    private readonly Grimoire _grimoire;

    private readonly ClientThingChart _objects;

    private readonly Func<uint> _avatarOid;

    private readonly IReadOnlyDictionary<uint, SpellComponentCard> _modules;

    private readonly PickPhase _pick;

    private readonly Func<uint, uint> _locateArcanumGlyph;

    private readonly Func<uint, uint> _locateModuleGlyph;

    private readonly Func<uint, int> _arcanumTier;

    private readonly Action<uint> _pickObject;

    private readonly Action<uint> _appendFavorite;

    private readonly Action<uint> _transmitSift;

    private readonly Action<uint> _dropArcanum;

    private readonly Action<uint>? _examineArcanum;

    private readonly Action<string, Action<bool>> _unhideAck;

    private readonly Action<uint, uint> _setWantedModule;

    private readonly Action _shut;

    private readonly WidgetElem _arcanumSheet;

    private readonly WidgetElem _moduleSheet;

    private readonly WidgetElem _arcanumTab;

    private readonly WidgetElem _moduleTab;

    private readonly WidgetBtn _shutBtn;

    private readonly WidgetBtn _eraseBtn;

    private readonly WidgetGearRoster _arcanumRoster;

    private readonly WidgetGearRoster _moduleRoster;

    private readonly ComponentBookTemplateMint _moduleBlueprints;

    private readonly List<(WidgetBtn Button, uint Mask)> _filters = [];

    private readonly ArcanabookRowStyle _rankStyling;

    private readonly WidgetDatFont? _rankTypeface;

    private uint? _chosenArcanum;

    private uint? _chosenModule;

    private bool _arcanaStale;

    private bool _modulesStale;

    private bool _moduleEditEngaged;

    private bool _destroyed;

    private ArcanabookWindowDriver(
        ImportedArrangement arrangement,
        Grimoire grimoire,
        ClientThingChart objects,
        Func<uint> avatarOid,
        IReadOnlyDictionary<uint, SpellComponentCard> modules,
        PickPhase pick,
        Func<uint, uint> locateArcanumGlyph,
        Func<uint, uint> locateModuleGlyph,
        Func<uint, int> arcanumTier,
        Action<uint> pickObject,
        Action<uint> appendFavorite,
        Action<uint> transmitSift,
        Action<uint> dropArcanum,
        Action<uint>? examineArcanum,
        Action<string, Action<bool>> unhideAck,
        Action<uint, uint> setWantedModule,
        Action shut,
        WidgetElem arcanumSheet,
        WidgetElem moduleSheet,
        WidgetElem arcanumTab,
        WidgetElem moduleTab,
        WidgetBtn shutBtn,
        WidgetBtn eraseBtn,
        WidgetGearRoster arcanumRoster,
        WidgetGearRoster moduleRoster,
        ComponentBookTemplateMint moduleBlueprints,
        ArcanabookRowStyle rankStyling,
        WidgetDatFont? rankTypeface)
    {
        _grimoire = grimoire;
        _objects = objects;
        _avatarOid = avatarOid;
        _modules = modules;
        _pick = pick;
        _locateArcanumGlyph = locateArcanumGlyph;
        _locateModuleGlyph = locateModuleGlyph;
        _arcanumTier = arcanumTier;
        _pickObject = pickObject;
        _appendFavorite = appendFavorite;
        _transmitSift = transmitSift;
        _dropArcanum = dropArcanum;
        _examineArcanum = examineArcanum;
        _unhideAck = unhideAck;
        _setWantedModule = setWantedModule;
        _shut = shut;
        _arcanumSheet = arcanumSheet;
        _moduleSheet = moduleSheet;
        _arcanumTab = arcanumTab;
        _moduleTab = moduleTab;
        _shutBtn = shutBtn;
        _eraseBtn = eraseBtn;
        _arcanumRoster = arcanumRoster;
        _moduleRoster = moduleRoster;
        _moduleBlueprints = moduleBlueprints;
        _rankStyling = rankStyling;
        _rankTypeface = rankTypeface;

        CanonTabWiring.AssignPress(arcanumTab, () => RevealSheet(ArcanabookWindowPage.Spells));
        CanonTabWiring.AssignPress(moduleTab, () => RevealSheet(ArcanabookWindowPage.Components));
        shutBtn.OnClick = shut;
        eraseBtn.OnClick = ReqEraseChosen;
        foreach ((uint ident, uint bitmask) in SiftBtns)
        {
            if (arrangement.SeekElem(ident) is not WidgetBtn btn) continue;
            uint siftBitmask = bitmask;
            btn.OnClick = () => FlipSift(siftBitmask);
            _filters.Add((btn, bitmask));
        }

        ConfigureArcanumRoster(arrangement);
        ConfigureModuleRoster(arrangement);
        _grimoire.SpellbookChanged += OnGrimoireAltered;
        _grimoire.DesiredComponentsChanged += OnWantedModulesAltered;
        _objects.ObjectAdded += OnObjectAltered;
        _objects.ObjectUpdated += OnObjectAltered;
        _objects.ObjectRemoved += OnObjectRemoved;
        _objects.ObjectMoved += OnObjectMoved;
        _objects.ContainerContentsReplaced += OnVesselInsidesReplaced;
        _pick.Changed += OnPickAltered;
        RevealSheet(ArcanabookWindowPage.Spells);
        ReassembleAll();
    }

    public static ArcanabookWindowDriver? Bind(
        ImportedArrangement arrangement,
        Grimoire grimoire,
        ClientThingChart objects,
        Func<uint> avatarOid,
        IReadOnlyDictionary<uint, SpellComponentCard> modules,
        PickPhase pick,
        Func<uint, uint> locateArcanumGlyph,
        Func<uint, uint> locateModuleGlyph,
        Func<uint, int> arcanumTier,
        Action<uint> pickObject,
        Action<uint> appendFavorite,
        Action<uint> transmitSift,
        Action<uint> dropArcanum,
        Action<uint>? examineArcanum,
        Action<string, Action<bool>> unhideAck,
        Action<uint, uint> setWantedModule,
        Action shut,
        ComponentBookTemplateMint moduleBlueprints,
        ArcanabookRowStyle rankStyling,
        WidgetDatFont? rankTypeface)
    {
        if (arrangement.SeekElem(ArcanumSheetIdent) is not { } arcanumSheet
            || arrangement.SeekElem(ModuleSheetIdent) is not { } moduleSheet
            || arrangement.SeekElem(ArcanumTabIdent) is not { } arcanumTab
            || arrangement.SeekElem(ModuleTabIdent) is not { } moduleTab
            || arrangement.SeekElem(ShutIdent) is not WidgetBtn shutBtn
            || arrangement.SeekElem(EraseBtnIdent) is not WidgetBtn eraseBtn
            || arrangement.SeekElem(ArcanumRosterIdent) is not WidgetGearRoster arcanumRoster)
            return null;

        WidgetElem? moduleHub = arrangement.SeekElem(ModuleRosterIdent);
        if (moduleHub is null) return null;
        WidgetGearRoster moduleRoster;
        if (moduleHub is WidgetGearRoster gearRoster)
            moduleRoster = gearRoster;
        else
        {
            moduleRoster = new WidgetGearRoster(arcanumRoster.SpriteResolve)
            {
                Width = moduleHub.Width,
                Height = moduleHub.Height,
                Moorings = MooringRims.Left | MooringRims.Top | MooringRims.Right | MooringRims.Bottom,
            };
            moduleHub.AddChild(moduleRoster);
            moduleRoster.GrabLatestMooringBaseline();
        }

        return new ArcanabookWindowDriver(
            arrangement, grimoire, objects, avatarOid, modules, pick,
            locateArcanumGlyph, locateModuleGlyph,
            arcanumTier, pickObject,
            appendFavorite, transmitSift, dropArcanum, examineArcanum, unhideAck,
            setWantedModule, shut,
            arcanumSheet, moduleSheet, arcanumTab, moduleTab, shutBtn,
            eraseBtn, arcanumRoster, moduleRoster, moduleBlueprints,
            rankStyling, rankTypeface);
    }
}
