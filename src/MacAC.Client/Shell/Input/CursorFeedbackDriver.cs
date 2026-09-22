using MacAC.Mechanics.Fighting;

namespace MacAC.Client.Shell;

public enum CursorFeedbackFlavor
{
    Default,
    Combat,
    Use,
    Examine,
    Busy,
    Text,
    WindowMove,
    ResizeHorizontal,
    ResizeVertical,
    ResizeDiagonalNwse,
    ResizeDiagonalNesw,
    Drag,
    DragAccept,
    DragReject,
    TargetPending,
    TargetValid,
    TargetInvalid,
}

public enum CanonCursorTargetMode
{
    None = 0,
    Use = 1,
    Examine = 2,
    UseTarget = 3,
}

public enum CanonGlobalCursorKind
{
    Default,
    DefaultFound,
    MeleeOrMissile,
    MeleeOrMissileFound,
    Magic,
    MagicFound,
    Examine,
    ExamineFound,
    Use,
    UseFound,
    Busy,
    BusyFound,
    TargetPending,
    TargetValid,
    TargetInvalid,
}

public readonly record struct ClientCursorFeedback(
    CursorFeedbackFlavor Kind,
    WidgetCursorMedia Cursor = default,
    CanonGlobalCursorKind GlobalKind = CanonGlobalCursorKind.Default)
{
    public static ClientCursorFeedback Default { get; } = new(CursorFeedbackFlavor.Default);
}

public readonly record struct CursorFeedbackCapture(
    object? DragPayload = null,
    WidgetGearSlot.DragAcceptPhase DragAccept = WidgetGearSlot.DragAcceptPhase.None,
    RescaleRims ActiveResizeEdges = RescaleRims.None,
    RescaleRims HoverResizeEdges = RescaleRims.None,
    bool WindowMoveActive = false,
    bool HoverWindowMove = false,
    bool HoverUi = false,
    bool HoverTextEdit = false,
    uint HoverTargetGuid = 0,
    bool? HoverTargetCompatible = null,
    int BusyCount = 0,
    CanonCursorTargetMode TargetMode = CanonCursorTargetMode.None,
    FightingManner CombatMode = FightingManner.NonCombat);

public sealed class CursorFeedbackDriver(
    GearDealingDriver? gearDealing = null,
    Func<uint>? realmMarkSupplier = null,
    Func<FightingManner>? fightingMannerSupplier = null)
{
    private readonly GearDealingDriver? _gearDealing = gearDealing;
    private readonly Func<uint>? _realmMarkSupplier = realmMarkSupplier;
    private readonly Func<FightingManner> _fightingMannerSupplier = fightingMannerSupplier ?? (() => FightingManner.NonCombat);

    public ClientCursorFeedback Current { get; private set; } = ClientCursorFeedback.Default;

    public ClientCursorFeedback Update(WidgetTrunk trunk)
    {
        ArgumentNullException.ThrowIfNull(trunk);

        WidgetElem? hover = trunk.Pick(trunk.PointerX, trunk.PointerY);

        var markManner = MannerFromDealing(_gearDealing);
        var hoveredGear = SeekHoveredGearSocket(hover);
        uint hoverMark = hoveredGear is not null
            ? hoveredGear.GearIdent
            : SeekRepresentedObject(hover) is { } represented
                ? represented
                : _realmMarkSupplier?.Invoke() ?? 0u;
        bool? hoverMarkCompatible = markManner == CanonCursorTargetMode.UseTarget
            && hoverMark is not 0
                ? _gearDealing?.IsLatestMarkCompatible(hoverMark)
                : null;

        CursorFeedbackCapture capture = new CursorFeedbackCapture(
            DragPayload: trunk.PullCargo,
            DragAccept: SeekHoveredGearSocket(hover)?.PullAdmitVisual ?? WidgetGearSlot.DragAcceptPhase.None,
            ActiveResizeEdges: trunk.EngagedRescaleRims,
            HoverResizeEdges: trunk.HoverRescaleRims,
            WindowMoveActive: trunk.IsPaneRelocateEngaged,
            HoverWindowMove: trunk.HoverPaneRelocate,
            HoverUi: hover is not null,
            HoverTextEdit: hover?.IsEditControl == true,
            HoverTargetGuid: hoverMark,
            HoverTargetCompatible: hoverMarkCompatible,
            BusyCount: _gearDealing?.OccupiedTally ?? 0,
            TargetMode: markManner,
            CombatMode: _fightingMannerSupplier());

        CursorFeedbackFlavor sort = LocateSort(capture);
        var authoredCur = LocateCur(trunk.Captured, hover, trunk.WidgetBolted);
        var cur = LocateNetCur(sort, capture, authoredCur);
        Current = new ClientCursorFeedback(sort, cur, LocateGlobalSort(capture));
        return Current;
    }

    public ClientCursorFeedback Update(CursorFeedbackCapture capture)
    {
        Current = Resolve(capture);
        return Current;
    }

    public ClientCursorFeedback Resolve(CursorFeedbackCapture capture)
    {
        var sort = LocateSort(capture);
        return new(
            sort,
            LocateNetCur(sort, capture, authoredCur: default),
            LocateGlobalSort(capture));
    }

    internal CanonGlobalCursorKind LocateGlobalSort(CursorFeedbackCapture snapshot)
    {
        bool located = snapshot.HoverTargetGuid is not 0;

        if (snapshot.BusyCount > 0)
            return located ? CanonGlobalCursorKind.BusyFound : CanonGlobalCursorKind.Busy;

        switch (NetMarkManner(snapshot))
        {
            case CanonCursorTargetMode.Use:
                return located ? CanonGlobalCursorKind.UseFound : CanonGlobalCursorKind.Use;
            case CanonCursorTargetMode.Examine:
                return located ? CanonGlobalCursorKind.ExamineFound : CanonGlobalCursorKind.Examine;
            case CanonCursorTargetMode.UseTarget:
                if (!located)
                    return CanonGlobalCursorKind.TargetPending;
                bool compatible = snapshot.HoverTargetCompatible
                    ?? _gearDealing?.IsLatestMarkCompatible(snapshot.HoverTargetGuid)
                    ?? false;
                return compatible
                    ? CanonGlobalCursorKind.TargetValid
                    : CanonGlobalCursorKind.TargetInvalid;
            case CanonCursorTargetMode.None:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot.TargetMode, "Unrecognized retail target mode");
        }

        return snapshot.CombatMode switch
        {
            FightingManner.Melee or FightingManner.Missile => located
                ? CanonGlobalCursorKind.MeleeOrMissileFound
                : CanonGlobalCursorKind.MeleeOrMissile,
            FightingManner.Magic => located
                ? CanonGlobalCursorKind.MagicFound
                : CanonGlobalCursorKind.Magic,
            _ => located ? CanonGlobalCursorKind.DefaultFound : CanonGlobalCursorKind.Default,
        };
    }

    private CursorFeedbackFlavor LocateSort(CursorFeedbackCapture capture)
    {
        if (capture.DragPayload is not null)
        {
            return capture.DragAccept switch
            {
                WidgetGearSlot.DragAcceptPhase.Accept => CursorFeedbackFlavor.DragAccept,
                WidgetGearSlot.DragAcceptPhase.Reject => CursorFeedbackFlavor.DragReject,
                _ => CursorFeedbackFlavor.Drag,
            };
        }

        RescaleRims rescaleRims = capture.ActiveResizeEdges != RescaleRims.None
            ? capture.ActiveResizeEdges
            : capture.HoverResizeEdges;
        if (rescaleRims != RescaleRims.None)
            return SortForRescale(rescaleRims);

        if (capture.WindowMoveActive)
            return CursorFeedbackFlavor.WindowMove;

        var markManner = NetMarkManner(capture);
        if (markManner == CanonCursorTargetMode.UseTarget)
        {
            if (capture.HoverTargetGuid is not 0)
            {
                bool compatible = capture.HoverTargetCompatible
                    ?? _gearDealing?.IsLatestMarkCompatible(capture.HoverTargetGuid)
                    ?? false;
                return compatible
                        ? CursorFeedbackFlavor.TargetValid
                        : CursorFeedbackFlavor.TargetInvalid;
            }

            return CursorFeedbackFlavor.TargetPending;
        }

        if (capture.BusyCount > 0)
            return CursorFeedbackFlavor.Busy;

        if (markManner == CanonCursorTargetMode.Use)
            return CursorFeedbackFlavor.Use;

        if (markManner == CanonCursorTargetMode.Examine)
            return CursorFeedbackFlavor.Examine;

        if (capture.HoverWindowMove)
            return CursorFeedbackFlavor.WindowMove;

        if (capture.HoverTextEdit)
            return CursorFeedbackFlavor.Text;

        return capture.CombatMode is FightingManner.Melee or FightingManner.Missile or FightingManner.Magic
            ? CursorFeedbackFlavor.Combat
            : CursorFeedbackFlavor.Default;
    }

    private CanonCursorTargetMode NetMarkManner(CursorFeedbackCapture capture)
    {
        return capture.TargetMode == CanonCursorTargetMode.None
                ? MannerFromDealing(_gearDealing)
                : capture.TargetMode;
    }

    private static CanonCursorTargetMode MannerFromDealing(
        GearDealingDriver? dealing)
    {
        return dealing?.DealingLedger.Current.Kind switch
        {
            DealingModeKind.Use => CanonCursorTargetMode.Use,
            DealingModeKind.Examine => CanonCursorTargetMode.Examine,
            DealingModeKind.UseItemOnTarget => CanonCursorTargetMode.UseTarget,
            _ => CanonCursorTargetMode.None,
        };
    }

    private static CursorFeedbackFlavor SortForRescale(RescaleRims rims)
    {
        bool horizontal = (rims & (RescaleRims.Left | RescaleRims.Right)) != 0;
        bool vertical = (rims & (RescaleRims.Top | RescaleRims.Bottom)) != 0;

        if (horizontal && vertical)
        {
            bool nwse =
                ((rims & RescaleRims.Left) != 0 && (rims & RescaleRims.Top) != 0)
                || ((rims & RescaleRims.Right) != 0 && (rims & RescaleRims.Bottom) != 0);
            return nwse ? CursorFeedbackFlavor.ResizeDiagonalNwse : CursorFeedbackFlavor.ResizeDiagonalNesw;
        }

        if (horizontal) return CursorFeedbackFlavor.ResizeHorizontal;
        return vertical ? CursorFeedbackFlavor.ResizeVertical : CursorFeedbackFlavor.Default;
    }

    private static WidgetCursorMedia LocateCur(WidgetElem? grabbed, WidgetElem? hover, bool widgetBolted)
    {
        WidgetCursorMedia grabbedCur = AuthoredCur(grabbed, widgetBolted);
        return grabbedCur.IsValid ? grabbedCur : AuthoredCur(hover, widgetBolted);

        static WidgetCursorMedia AuthoredCur(WidgetElem? elem, bool bolted)
            => elem is null || (bolted && elem.PaneRelocateHnd)
                ? default
                : elem.EngagedCur();
    }

    private static WidgetCursorMedia LocateNetCur(
        CursorFeedbackFlavor sort,
        CursorFeedbackCapture capture,
        WidgetCursorMedia authoredCur)
    {
        bool syntheticControlOwnsPtr = capture.ActiveResizeEdges != RescaleRims.None
            || capture.HoverResizeEdges != RescaleRims.None
            || capture.WindowMoveActive;

        if (syntheticControlOwnsPtr
            && CanonCursorRegistry.TryFetchPaneControlCur(sort, out WidgetCursorMedia grabbedControl))
            return grabbedControl;

        return authoredCur.IsValid
            ? authoredCur
            : CanonCursorRegistry.TryFetchPaneControlCur(sort, out WidgetCursorMedia backup)
            ? backup
            : default;
    }

    private static WidgetGearSlot? SeekHoveredGearSocket(WidgetElem? elem)
    {
        while (elem is not null)
        {
            if (elem is WidgetGearSlot socket)
                return socket;
            elem = elem.Ancestor;
        }
        return null;
    }

    private static uint? SeekRepresentedObject(WidgetElem? elem)
    {
        while (elem is not null)
        {
            if (elem.LocatedObjectOidSupplier is { } supplier)
            {
                uint oid = supplier();
                if (oid is not 0u)
                    return oid;
            }
            elem = elem.Ancestor;
        }
        return null;
    }
}
