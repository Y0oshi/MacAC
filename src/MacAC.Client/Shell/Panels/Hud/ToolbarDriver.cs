using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Shell.Panels;

public sealed partial class ToolbarDriver : IGearListDragHandler, IRetainedPaneDriver
{
    private static readonly uint[] SocketIdents =
    [
        0x100001A7, 0x100001A8, 0x100001A9, 0x100001AA, 0x100001AB,
        0x100001AC, 0x100001AD, 0x100001AE, 0x100001AF,
        0x100006B7, 0x100006B8, 0x100006B9, 0x100006BA, 0x100006BB,
        0x100006BC, 0x100006BD, 0x100006BE, 0x100006BF,
    ];

    private static readonly uint[] FightingIndicatorIdents =
        [0x10000192u, 0x10000193u, 0x10000194u, 0x10000195u];

    private const uint BoardIdentAttr = 0x10000029u;

    private const uint UseBtnIdent = 0x1000019Du;

    private const uint ExamineBtnIdent = 0x100001A5u;

    private const uint AmmoIndicatorIdent = 0x10000194u;

    private const uint SatchelBtnIdent = 0x100001B1u;

    private static readonly uint[] BoardBtnIdents =
        [0x10000197u, 0x10000198u, 0x10000199u, 0x1000055Au, 0x1000019Au, 0x1000019Bu, SatchelBtnIdent];

    private readonly WidgetGearRoster?[] _sockets = new WidgetGearRoster?[SocketIdents.Length];

    private readonly WidgetElem?[] _fightingIndicators = new WidgetElem?[FightingIndicatorIdents.Length];

    private readonly WidgetBtn? _satchelBtn;

    private readonly WidgetBtn? _useBtn;

    private readonly WidgetBtn? _examineBtn;

    private readonly WidgetBtn? _ammoIndicator;

    private readonly List<(uint PanelId, WidgetBtn Button)> _boardBtns = [];

    private readonly ClientThingChart _repo;

    private readonly FightingPhase? _fightingPhase;

    private readonly HotbarStore _store;

    private readonly Func<GearKind, uint, uint, uint, uint, uint> _glyphIdents;  // (itemType, icon, underlay, overlay, effects) → GL tex

    private readonly Func<GearKind, uint, uint, uint, uint, uint>? _pullGlyphIdents;

    private readonly Action<uint> _useGear;                   // guid → fire UseObject

    private readonly Action<HotbarSlot>? _transmitAppendShortcut;

    private readonly Action<uint>? _transmitDropShortcut;      // (index)

    private readonly GearDealingDriver? _gearDealing;

    private readonly Action<uint>? _pickGear;

    private readonly Func<uint> _chosenObjectIdent;

    private readonly PickPhase? _pick;

    private readonly Func<uint>? _avatarOid;

    private readonly Action<uint, uint, int>? _transmitPutGearInVessel;

    private uint _ammoObjectIdent;

    private bool _destroyed;

    private uint[]? _regularDigits;

    private uint[]? _ghostedDigits;

    private uint[]? _vacantDigits;

    private bool _shortcutsGhosted;

    private ToolbarDriver(
        ImportedArrangement arrangement,
        ClientThingChart repo,
        HotbarStore shortcuts,
        Func<GearKind, uint, uint, uint, uint, uint> glyphIdents,
        Action<uint> useGear,
        FightingPhase? fightingPhase,
        uint[]? regularDigits,
        uint[]? ghostedDigits,
        uint[]? vacantDigits,
        GearDealingDriver? gearDealing = null,
        Action<HotbarSlot>? transmitAppendShortcut = null,
        Action<uint>? transmitDropShortcut = null,
        Action? flipFighting = null,
        Action<uint>? pickGear = null,
        Func<uint>? chosenObjectIdent = null,
        PickPhase? pick = null,
        Func<uint>? avatarOid = null,
        Action<uint, uint, int>? transmitPutGearInVessel = null,
        WidgetDatFont? ammoTypeface = null,
        Func<GearKind, uint, uint, uint, uint, uint>? pullGlyphIdents = null)
    {
        _repo = repo;
        _fightingPhase = fightingPhase;
        _store = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        _glyphIdents = glyphIdents;
        _pullGlyphIdents = pullGlyphIdents;
        _useGear = useGear;
        _regularDigits = regularDigits;
        _ghostedDigits = ghostedDigits;
        _vacantDigits = vacantDigits;
        _gearDealing = gearDealing;
        _transmitAppendShortcut = transmitAppendShortcut;
        _transmitDropShortcut = transmitDropShortcut;
        _pickGear = pickGear;
        _chosenObjectIdent = chosenObjectIdent ?? (() => 0u);
        _pick = pick;
        _avatarOid = avatarOid;
        _transmitPutGearInVessel = transmitPutGearInVessel;

        for (int idx = 0; idx < SocketIdents.Length; ++idx)
        {
            _sockets[idx] = arrangement.SeekElem(SocketIdents[idx]) as WidgetGearRoster;
            if (_sockets[idx] is { } roster)
            {
                WirePress(roster);
                roster.PrimaryGearPressed = PressGear;
                roster.ExamineGearAsked = StudyGear;
                roster.EnrollPullHandler(this);
                roster.Cell.SocketIdx = idx;
                roster.Cell.SrcSort = GearDragSource.ShortcutBar;
                roster.Cell.PullAdmitSprite = 0x060011FAu;
                roster.Cell.HintPhraseLocate = g => _repo.Get(g)?.FetchHintReadoutLabel();
            }
        }

        for (int idx = 0; idx < FightingIndicatorIdents.Length; ++idx)
        {
            _fightingIndicators[idx] = arrangement.SeekElem(FightingIndicatorIdents[idx]);
            if (_fightingIndicators[idx] is WidgetBtn btn)
                btn.OnClick = flipFighting;
        }

        _ammoIndicator = arrangement.SeekElem(AmmoIndicatorIdent) as WidgetBtn;
        if (_ammoIndicator is not null)
        {
            _ammoIndicator.LabelFont = ammoTypeface;
            _ammoIndicator.CaptionColor = System.Numerics.Vector4.One;
        }

        foreach (uint elemIdent in BoardBtnIdents)
        {
            if (arrangement.SeekElem(elemIdent) is WidgetBtn boardBtn
                && boardBtn.TryFetchEnumAttr(BoardIdentAttr, out uint boardIdent))
                _boardBtns.Add((boardIdent, boardBtn));
        }

        _satchelBtn = arrangement.SeekElem(SatchelBtnIdent) as WidgetBtn;
        if (_satchelBtn is not null)
        {
            _satchelBtn.LocatedObjectOidSupplier = () =>
                _avatarOid?.Invoke() ?? _gearDealing?.PlayerOid ?? 0u;
            _satchelBtn.GearPullAdmitSprite = 0x060011F7u;
            _satchelBtn.OnGearPullOver = SatchelBtnPullOver;
            _satchelBtn.OnGearDiscard = ProcessSatchelBtnDiscard;
        }

        _useBtn = arrangement.SeekElem(UseBtnIdent) as WidgetBtn;
        _examineBtn = arrangement.SeekElem(ExamineBtnIdent) as WidgetBtn;
        _useBtn?.OnClick = () => _gearDealing?.EmployChosenOrJoinManner(_chosenObjectIdent());
        _examineBtn?.OnClick = () => _gearDealing?.StudyChosenOrJoinManner(_chosenObjectIdent());

        AssignFightingManner(FightingManner.NonCombat);

        _fightingPhase?.CombatModeChanged += AssignFightingManner;
        _pick?.Changed += OnPickAltered;

        repo.ObjectAdded += OnRepositoryObjectAltered;
        repo.ObjectUpdated += OnRepositoryObjectAltered;
        repo.ObjectRemoved += OnRepositoryObjectAltered;
        repo.ObjectMoved += OnRepositoryObjectMoved;
        repo.Cleared += OnRepositoryCleared;
        _store.Changed += Populate;
        RenewAmmo();
        RenewUseBtn();
    }
}
