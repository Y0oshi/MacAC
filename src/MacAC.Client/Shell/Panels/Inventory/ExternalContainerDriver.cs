using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Shell.Panels;

public sealed partial class ExternalContainerDriver : IGearListDragHandler, IRetainedPaneDriver
{
    public const uint LayoutId = 0x21000008u;

    public const uint TrunkId = 0x10000063u;

    public const uint TopVesselIdent = 0x10000064u;

    public const uint VesselRosterIdent = 0x10000067u;

    public const uint ShutBtnIdent = 0x10000068u;

    public const uint InsidesRosterIdent = 0x1000006Au;

    public const uint InsidesScrollerIdent = 0x1000006Bu;

    private const float GearChamberDims = 32f;

    private const float VesselChamberDims = 36f;

    internal const float DefaultSubstanceWidth = 700f;

    internal const float FloorSubstanceWidth = 160f;

    private readonly OpenContainerState _phase;

    private readonly ClientThingChart _objects;

    private readonly PickPhase _pick;

    private readonly GearDealingDriver _gearDealing;

    private readonly StackSplitGauge _pileDivideQty;

    private readonly Func<GearKind, uint, uint, uint, uint, uint> _locateGlyph;

    private readonly Func<GearKind, uint, uint, uint, uint, uint> _locatePullGlyph;

    private readonly Action<uint> _transmitUse;

    private readonly Action<uint, uint, int> _transmitPutGearInVessel;

    private readonly Action<uint, uint, uint, uint> _transmitDivideToVessel;

    private readonly Func<uint, bool> _isWithinUseSpan;

    private readonly CanonWindowHandle _window;

    private readonly WidgetGearRoster _topVessel;

    private readonly WidgetGearRoster _vesselRoster;

    private readonly WidgetGearRoster _insidesRoster;

    private uint _openVessel;

    private QueuedBackpackStance? _queuedStance;

    private bool _shutAsked;

    private bool _destroyed;

    private ExternalContainerDriver(
        ImportedArrangement arrangement,
        OpenContainerState phase,
        ClientThingChart objects,
        PickPhase pick,
        GearDealingDriver gearDealing,
        StackSplitGauge pileDivideQty,
        Func<GearKind, uint, uint, uint, uint, uint> locateGlyph,
        Func<GearKind, uint, uint, uint, uint, uint> locatePullGlyph,
        Action<uint> transmitUse,
        Action<uint, uint, int> transmitPutGearInVessel,
        Action<uint, uint, uint, uint> transmitDivideToVessel,
        Func<uint, bool> isWithinUseSpan,
        CanonWindowHandle pane,
        uint insidesVacantSprite,
        uint vesselVacantSprite)
    {
        _phase = phase;
        _objects = objects;
        _pick = pick;
        _gearDealing = gearDealing;
        _pileDivideQty = pileDivideQty;
        _locateGlyph = locateGlyph;
        _locatePullGlyph = locatePullGlyph;
        _transmitUse = transmitUse;
        _transmitPutGearInVessel = transmitPutGearInVessel;
        _transmitDivideToVessel = transmitDivideToVessel;
        _isWithinUseSpan = isWithinUseSpan;
        _window = pane;

        _topVessel = NeededRoster(arrangement, TopVesselIdent);
        _vesselRoster = NeededRoster(arrangement, VesselRosterIdent);
        _insidesRoster = NeededRoster(arrangement, InsidesRosterIdent);

        ConfigureRoster(_topVessel, VesselChamberDims, horizontalRoll: false, vesselVacantSprite);
        ConfigureRoster(_vesselRoster, VesselChamberDims, horizontalRoll: true, vesselVacantSprite);
        ConfigureRoster(_insidesRoster, GearChamberDims, horizontalRoll: true, insidesVacantSprite);
        _topVessel.PrimaryGearPressed = PressGear;
        _vesselRoster.PrimaryGearPressed = PressGear;
        _insidesRoster.PrimaryGearPressed = PressGear;
        _topVessel.ExamineGearAsked = StudyGear;
        _vesselRoster.ExamineGearAsked = StudyGear;
        _insidesRoster.ExamineGearAsked = StudyGear;
        _insidesRoster.EnrollPullHandler(this);

        if (arrangement.SeekElem(InsidesScrollerIdent) is WidgetScroller scroller)
        {
            scroller.Model = _insidesRoster.Scroll;
            scroller.Horizontal = true;
            scroller.SpriteResolve ??= _insidesRoster.SpriteResolve;
            CanonScrollbarChrome.ImposeHorizontal(scroller);
        }

        AttachShut(arrangement, ReqShut);

        _phase.Changed += OnExternalVesselAltered;
        _objects.ObjectAdded += OnObjectAltered;
        _objects.ObjectUpdated += OnObjectAltered;
        _objects.ObjectMoved += OnObjectMoved;
        _objects.ObjectRemoved += OnObjectRemoved;
        _objects.ContainerContentsReplaced += OnInsidesReplaced;
        _objects.Cleared += OnObjectsCleared;
        _pick.Changed += OnPickAltered;
        _gearDealing.StateChanged += OnDealingPhaseAltered;
        _gearDealing.PendingBackpackPlacementRequested += OnQueuedStanceAsked;
        _gearDealing.PendingBackpackPlacementCancelled += OnQueuedStanceCancelled;
        _gearDealing.PendingBackpackPlacementResolved += OnQueuedStanceSettled;
        WipeRosters();
    }
}
