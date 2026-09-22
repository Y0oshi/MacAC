using System.Numerics;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Shell.Panels;

public sealed partial class StashDriver : IGearListDragHandler, IRetainedPaneDriver
{
    public const uint InsidesGridIdent = 0x100001C6u;

    public const uint VesselRosterTag = 0x100001CAu;

    public const uint TopVesselTag = 0x100001C9u;

    public const uint BurdenGaugeIdent = 0x100001D9u;

    public const uint BurdenPhraseIdent = 0x100001D8u;

    public const uint BurdenLegendIdent = 0x100001D7u;  // "Burden"

    public const uint InsidesLegendIdent = 0x100001C5u;

    public const uint BannerPhraseIdent = 0x100001D3u;  // "Inventory of <name>"

    public const uint InsidesScrollerTag = 0x100001C7u;

    private const uint BackdropIdent = 0x100001D0u;

    private const uint InsidesPaneIdent = 0x100001CFu;

    private const uint PaperdollPaneIdent = 0x100001CDu;

    private const uint BackpackPaneIdent = 0x100001CEu;

    private const int InsidesColumns = 6;

    private const float InsidesChamberPx = 32f;

    private const float BackpackChamberPx = 36f;

    private const int FlankBagSockets = 7;

    private const int PrimaryBundleSockets = 102;

    private const int FlankBundleSockets = 24;

    private readonly ClientThingChart _objects;

    private readonly Func<uint> _avatarOid;

    private readonly Func<GearKind, uint, uint, uint, uint, uint> _glyphIdents;

    private readonly Func<GearKind, uint, uint, uint, uint, uint>? _pullGlyphIdents;

    private readonly Func<int?> _strength;

    private readonly Grimoire? _burdenGrimoire;

    private readonly Func<string>? _holderLabel;

    private readonly WidgetGearRoster? _insidesGrid;

    private readonly WidgetGearRoster? _vesselRoster;

    private readonly WidgetGearRoster? _topVessel;

    private readonly WidgetGauge? _burdenGauge;

    private float _burdenPopulate;

    private int _burdenPct;

    private static readonly Vector4 LegendTint = new(1f, 1f, 1f, 1f);

    private uint _openVessel;   // 0 = the main pack (the player); else the open side bag's guid

    private readonly PickPhase _pick;

    private readonly Action<uint>? _transmitUse;

    private readonly Action<uint, uint, int>? _transmitPutGearInVessel;

    private readonly Action<uint, uint, uint, uint>? _transmitStackableDivideToVessel;

    private readonly Action<uint, uint, uint>? _transmitStackableCombine;

    private readonly Action<uint, uint>? _alertCombineAttempt;

    private readonly GearDealingDriver? _gearDealing;

    private readonly StackSplitGauge? _pileDivideQty;

    private QueuedRosterStance? _queuedRosterStance;

    private bool _destroyed;

    private readonly record struct QueuedRosterStance(
        ulong Token,
        uint ItemId,
        uint ContainerId,
        int Placement);

    private const uint EncumbranceValueProp = 5u;

    private const uint EncumbranceAugProp = 0xE6u;

    private StashDriver(
        ImportedArrangement arrangement,
        ClientThingChart objects,
        Func<uint> avatarOid,
        Func<GearKind, uint, uint, uint, uint, uint> glyphIdents,
        Func<GearKind, uint, uint, uint, uint, uint>? pullGlyphIdents,
        Func<int?> strength,
        PickPhase selection,
        Func<string>? holderLabel,
        WidgetDatFont? datTypeface,
        uint insidesVacantSprite,
        uint flankBagVacantSprite,
        uint primaryBundleVacantSprite,
        Action<uint>? transmitUse,
        Action<uint, uint, int>? transmitPutGearInVessel,
        Action<uint, uint, uint, uint>? transmitStackableDivideToVessel,
        Action<uint, uint, uint>? transmitStackableCombine,
        Action<uint, uint>? alertCombineAttempt,
        GearDealingDriver? gearDealing,
        Action? onShut,
        StackSplitGauge? pileDivideQty,
        Grimoire? burdenGrimoire)
    {
        _objects = objects;
        _avatarOid = avatarOid;
        _glyphIdents = glyphIdents;
        _pullGlyphIdents = pullGlyphIdents;
        _strength = strength;
        _burdenGrimoire = burdenGrimoire;
        _holderLabel = holderLabel;
        _transmitUse = transmitUse;
        _transmitPutGearInVessel = transmitPutGearInVessel;
        _transmitStackableDivideToVessel = transmitStackableDivideToVessel;
        _transmitStackableCombine = transmitStackableCombine;
        _alertCombineAttempt = alertCombineAttempt;
        _gearDealing = gearDealing;
        _pileDivideQty = pileDivideQty;
        _pick = selection ?? throw new ArgumentNullException(nameof(selection));
        _gearDealing?.MergeAttempted += OnCombineAttempted;

        WindowChromeDriver.WireShutBtn(arrangement, onShut);

        _insidesGrid = arrangement.SeekElem(InsidesGridIdent) as WidgetGearRoster;
        _vesselRoster = arrangement.SeekElem(VesselRosterTag) as WidgetGearRoster;
        _topVessel = arrangement.SeekElem(TopVesselTag) as WidgetGearRoster;

        ConfigureRescaleArrangement(arrangement);

        if (_insidesGrid is not null)
        {
            _insidesGrid.Columns = InsidesColumns;
            _insidesGrid.ChamberWidth = InsidesChamberPx;
            _insidesGrid.ChamberHeight = InsidesChamberPx;
        }

        if (_insidesGrid is not null
            && arrangement.SeekElem(InsidesScrollerTag) is WidgetScroller bar)
        {
            bar.Model = _insidesGrid.Scroll;
            bar.SpriteResolve ??= _insidesGrid.SpriteResolve;
        }
        if (_vesselRoster is not null)
        {
            _vesselRoster.Columns = 1;
            _vesselRoster.ChamberWidth = BackpackChamberPx;
            _vesselRoster.ChamberHeight = BackpackChamberPx;
        }

        _insidesGrid?.ChamberVacantSprite = insidesVacantSprite;
        _vesselRoster?.ChamberVacantSprite = flankBagVacantSprite;
        _topVessel?.ChamberVacantSprite = primaryBundleVacantSprite;

        _insidesGrid?.EnrollPullHandler(this);
        _vesselRoster?.EnrollPullHandler(this);
        _topVessel?.EnrollPullHandler(this);
        if (_insidesGrid is not null)
        {
            _insidesGrid.PrimaryGearPressed = PressGear;
            _insidesGrid.ExamineGearAsked = StudyGear;
        }
        if (_vesselRoster is not null)
        {
            _vesselRoster.PrimaryGearPressed = PressGear;
            _vesselRoster.ExamineGearAsked = StudyGear;
        }
        if (_topVessel is not null)
        {
            _topVessel.PrimaryGearPressed = PressSelfGear;
            _topVessel.ExamineGearAsked = StudyGear;
        }

        _burdenGauge = arrangement.SeekElem(BurdenGaugeIdent) as WidgetGauge;
        if (_burdenGauge is not null)
        {
            _burdenGauge.Vertical = true;            // 11x58 vertical bar
            _burdenGauge.PopulateFromBottom = true;
            _burdenGauge.Populate = () => _burdenPopulate;
        }

        FastenLegend(arrangement.SeekElem(BannerPhraseIdent), () => "Inventory of " + HolderLabel(), datTypeface);
        FastenLegend(arrangement.SeekElem(BurdenLegendIdent), () => "Burden", datTypeface);
        FastenLegend(arrangement.SeekElem(InsidesLegendIdent), () => "Contents of " + OpenVesselLabel(), datTypeface);
        FastenLegend(arrangement.SeekElem(BurdenPhraseIdent), () => _burdenPct + "%", datTypeface);

        _objects.ObjectAdded += OnObjectAltered;
        _objects.ObjectMoved += OnObjectMoved;
        _objects.MoveRequestFailed += OnRelocateReqFailed;
        _objects.ContainerContentsReplaced += OnVesselInsidesReplaced;
        _objects.ObjectRemoved += OnObjectRemoved;
        _objects.ObjectUpdated += OnObjectAltered;
        _objects.Cleared += OnObjectsCleared;
        _pick.Changed += OnPickAltered;
        _burdenGrimoire?.EnchantmentsChanged += RenewBurden;
        if (_gearDealing is not null)
        {
            _gearDealing.StateChanged += OnDealingPhaseAltered;
            _gearDealing.PendingBackpackPlacementRequested += OnQueuedBackpackStanceAsked;
            _gearDealing.PendingBackpackPlacementCancelled += OnQueuedBackpackStanceCancelled;
            _gearDealing.PendingBackpackPlacementResolved += OnQueuedBackpackStanceSettled;
        }

        Populate();
    }
}
