using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Shell.Panels;

public sealed class EffigyDriver : IGearListDragHandler, IRetainedPaneDriver
{
    public const uint DollViewRectIdent = 0x100001D5u;
    public const uint DollPullBitmaskIdent = 0x100001D6u;

    public static readonly uint[] ArmorSocketElemIdents =
    [
        0x100005abu, 0x100005acu, 0x100005adu, 0x100005aeu, 0x100005afu,
        0x100005b0u, 0x100005b1u, 0x100005b2u, 0x100005b3u,
    ];

    public sealed class EffigyViewLedger
    {
        public bool SocketLens { get; private set; }          // false = doll-view (default)
        public bool DollShown => !SocketLens;
        public bool ArmorSocketsShown => SocketLens;
        public void Toggle() => SocketLens = !SocketLens;
    }

    private readonly ClientThingChart _objects;
    private readonly Func<uint> _avatarOid;
    private readonly Func<GearKind, uint, uint, uint, uint, uint> _glyphIdents;
    private readonly Func<GearKind, uint, uint, uint, uint, uint>? _pullGlyphIdents;
    private readonly GearDealingDriver _gearDealing;
    private readonly bool _ownsGearDealing;
    private readonly PickPhase _pick;
    private readonly EffigyClickMap? _pressLookup;
    private readonly List<(WieldBitmask Mask, WidgetGearRoster List)> _sockets = [];
    private readonly List<(AetheriaSlotState Bit, WidgetGearRoster List)> _aetheriaSockets = [];

    private readonly EffigyViewLedger _lensPhase = new();
    private readonly List<WidgetGearRoster> _armorSockets = [];
    private WidgetElem? _dollViewRect;
    private WidgetElem? _dollPullBitmask;
    private bool _destroyed;

    private EffigyDriver(
        ImportedArrangement arrangement, ClientThingChart objects, Func<uint> avatarOid,
        Func<GearKind, uint, uint, uint, uint, uint> glyphIdents, PickPhase selection,
        GearDealingDriver itemInteraction,
        uint vacantSocketSprite, WidgetDatFont? datTypeface,
        EffigyClickMap? pressLookup,
        Func<GearKind, uint, uint, uint, uint, uint>? pullGlyphIdents,
        IReadOnlyDictionary<uint, uint>? vacantSocketSprites,
        bool ownsGearDealing)
    {
        _objects = objects; _avatarOid = avatarOid; _glyphIdents = glyphIdents;
        _pullGlyphIdents = pullGlyphIdents;
        _gearDealing = itemInteraction ?? throw new ArgumentNullException(nameof(itemInteraction));
        _ownsGearDealing = ownsGearDealing;
        _pick = selection ?? throw new ArgumentNullException(nameof(selection));
        _pressLookup = pressLookup;

        for (int idx = 0; idx < EffigySlotBackgrounds.Definitions.Length; ++idx)
        {
            var (elem, bitmask, _, unlockBit) = EffigySlotBackgrounds.Definitions[idx];
            if (arrangement.SeekElem(elem) is not WidgetGearRoster roster) continue;
            roster.EnrollPullHandler(this);
            roster.PrimaryGearPressed = PressGear;
            roster.ExamineGearAsked = StudyGear;
            roster.Cell.SrcSort = GearDragSource.Equipment;
            roster.Cell.SocketIdx = idx;              // definition position = equipped drag-payload SourceSlot
            roster.Cell.HintPhraseLocate = g => _objects.Get(g)?.FetchHintReadoutLabel();
            roster.Cell.VacantSprite = vacantSocketSprites is not null
                && vacantSocketSprites.TryGetValue(elem, out uint authoredSprite)
                    ? authoredSprite
                    : vacantSocketSprite;
            roster.Cell.DoubleClicked = () =>
            {
                if (roster.Cell.GearIdent is not 0)
                    _gearDealing?.EngageGear(roster.Cell.GearIdent);
            };
            _sockets.Add((bitmask, roster));
            if (unlockBit != AetheriaSlotState.None)
                _aetheriaSockets.Add((unlockBit, roster));
        }

        _objects.ObjectAdded += OnObjectAltered;
        _objects.ObjectMoved += OnObjectMoved;
        _objects.ObjectRemoved += OnObjectAltered;
        _objects.ObjectUpdated += OnObjectAltered;
        _objects.Cleared += OnObjectsCleared;
        _pick.Changed += OnPickAltered;
        _gearDealing.StateChanged += OnDealingPhaseAltered;

        foreach (var ident in ArmorSocketElemIdents)
            if (arrangement.SeekElem(ident) is WidgetGearRoster armor) _armorSockets.Add(armor);

        _dollViewRect = arrangement.SeekElem(DollViewRectIdent);
        Action<int, int> pressDoll = ProcessDollPress;
        if (_dollViewRect is WidgetViewport doll)
            doll.ClickedAt = pressDoll;

        switch (arrangement.SeekElem(DollPullBitmaskIdent))
        {
            case WidgetBtn pullBitmaskBtn:
                _dollPullBitmask = pullBitmaskBtn;
                pullBitmaskBtn.OnClickAt = pressDoll;
                break;
            case WidgetDatElement pullBitmaskElem:
                _dollPullBitmask = pullBitmaskElem;
                pullBitmaskElem.ClickThrough = false;
                pullBitmaskElem.OnPressAt = pressDoll;
                break;
        }

        WidgetElem? socketsBtnElem = arrangement.SeekElem(0x100005BEu);
        if (socketsBtnElem is WidgetBtn socketsBtn)
        {
            socketsBtn.OnClick = () => { _lensPhase.Toggle(); ImposeLens(); };
            if (datTypeface is not null)
            {
                socketsBtn.Label = "Slots";
                socketsBtn.LabelFont = datTypeface;
                socketsBtn.CaptionColor = System.Numerics.Vector4.One;   // white (was gold)
                socketsBtn.CaptionAlign = WidgetBtn.CaptionAlignment.Left;  // sit at the left, before the slots
            }
        }

        ImposeLens();   // initial state = doll-view (armor slots hidden)

        Seed();
    }

    public static EffigyDriver Bind(
        ImportedArrangement arrangement, ClientThingChart objects, Func<uint> avatarOid,
        Func<GearKind, uint, uint, uint, uint, uint> glyphIdents, PickPhase pick,
        GearDealingDriver gearDealing,
        uint vacantSocketSprite = 0u, WidgetDatFont? datTypeface = null,
        EffigyClickMap? pressLookup = null,
        Func<GearKind, uint, uint, uint, uint, uint>? pullGlyphIdents = null,
        IReadOnlyDictionary<uint, uint>? vacantSocketSprites = null,
        bool ownsGearDealing = false)
    {
        return new(
                arrangement, objects, avatarOid, glyphIdents, pick, gearDealing, vacantSocketSprite,
                datTypeface, pressLookup, pullGlyphIdents, vacantSocketSprites, ownsGearDealing);
    }

    public void Seed()
    {
        uint p = _avatarOid();

        List<ClientThing> equipped = new List<ClientThing>();
        foreach (var o in _objects.Objects)
            if (o.CurrentlyEquippedLocale != WieldBitmask.None && (o.WielderIdent == p || o.VesselTag == p))
                equipped.Add(o);

        foreach (var (bitmask, roster) in _sockets)
        {
            ClientThing? worn = null;
            foreach (var o in equipped)
                if ((o.CurrentlyEquippedLocale & bitmask) != WieldBitmask.None) { worn = o; break; }

            if (worn is null) { roster.Cell.Clear(); continue; }
            uint bmp = _glyphIdents(worn.Type, worn.IconId, worn.GlyphUnderlayIdent, worn.GlyphTopLayerIdent, worn.Effects);
            uint pullBmp = _pullGlyphIdents?.Invoke(
                worn.Type, worn.IconId, worn.GlyphUnderlayIdent, worn.GlyphTopLayerIdent, worn.Effects) ?? 0u;
            roster.Cell.AssignGear(worn.ObjectId, bmp, pullGlyphTexture: pullBmp);
            roster.Cell.AssignWaitingPhase(
                _gearDealing.IsQueuedSatchelSrc(worn.ObjectId));
        }
        ImposeAetheriaVis();
        ImposePickIndicators();
    }

    public void OnPullLift(WidgetGearRoster srcRoster, WidgetGearSlot srcChamber, GearDragPayload cargo)
    {
        if (cargo.ObjId is not 0 && _pick.ChosenObjectTag != cargo.ObjId)
            _pick.Select(cargo.ObjId, PickChangeSource.Paperdoll);
    }

    public GearDragAcceptance OnPullOver(WidgetGearRoster markRoster, WidgetGearSlot markChamber, GearDragPayload cargo)
    {
        if (cargo.SourceKind == GearDragSource.ShortcutBar)
            return GearDragAcceptance.None;
        ClientThing? gear = _objects.Get(cargo.ObjId);
        return gear is null
            ? GearDragAcceptance.Reject
            : (gear.ValidLocations & ConcealFor(markRoster)) != WieldBitmask.None
            ? GearDragAcceptance.Accept
            : GearDragAcceptance.Reject;
    }

    public void ProcessDiscardFree(WidgetGearRoster markRoster, WidgetGearSlot markChamber, GearDragPayload cargo)
    {
        if (cargo.SourceKind == GearDragSource.ShortcutBar)
            return;
        ClientThing? gear = _objects.Get(cargo.ObjId);
        if (gear is null) return;
        WieldBitmask wieldBitmask = GearEquipRules.LocatePaperdollDiscardWieldBitmask(gear, ConcealFor(markRoster));
        if (wieldBitmask == WieldBitmask.None) return;       // not wieldable here (defensive; OnDragOver already rejected)
        _gearDealing.WieldFromPaperdoll(cargo.ObjId, wieldBitmask);
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _objects.ObjectAdded -= OnObjectAltered;
        _objects.ObjectMoved -= OnObjectMoved;
        _objects.ObjectRemoved -= OnObjectAltered;
        _objects.ObjectUpdated -= OnObjectAltered;
        _objects.Cleared -= OnObjectsCleared;
        _pick.Changed -= OnPickAltered;
        _gearDealing.StateChanged -= OnDealingPhaseAltered;
        foreach (var (_, roster) in _sockets)
        {
            roster.PrimaryGearPressed = null;
            roster.ExamineGearAsked = null;
        }
        if (_dollViewRect is WidgetViewport doll)
            doll.ClickedAt = null;
        switch (_dollPullBitmask)
        {
            case WidgetBtn btn:
                btn.OnClickAt = null;
                break;
            case WidgetDatElement elem:
                elem.OnPressAt = null;
                break;
        }
        if (_ownsGearDealing)
            _gearDealing.Dispose();
    }

    private void ProcessDollPress(int x, int y)
    {
        WieldBitmask corpusLocale = _pressLookup?.FetchCorpusLocale(x, y) ?? WieldBitmask.None;
        uint strikeObject = PaperdollPickPolicy.FetchUpperSatchelObject(
            _objects,
            _avatarOid(),
            corpusLocale);
        if (strikeObject is 0)
            return;

        if (_gearDealing?.OfferSelfPrimaryPress()
            is not null and not GearPrimaryClickResult.NotActive)
            return;

        _pick.Select(strikeObject, PickChangeSource.Paperdoll);
    }

    private void OnObjectAltered(ClientThing o)
    {
        if (o.ObjectId == _avatarOid())
            ImposeAetheriaVis();
        else if (Concerns(o))
            Seed();
    }
    private void OnObjectMoved(ObjectRelocation relocate)
    {
        uint avatar = _avatarOid();
        if ((relocate.Item is { } gear && Concerns(gear))
            || relocate.Previous.ContainerId == avatar
            || relocate.Current.ContainerId == avatar
            || relocate.Previous.WielderId == avatar
            || relocate.Current.WielderId == avatar)
            Seed();
    }
    private void OnPickAltered(PickShift _) => ImposePickIndicators();
    private void OnDealingPhaseAltered() => Seed();
    private void OnObjectsCleared()
    {
        ImposeAetheriaVis();
        Seed();
    }

    private bool Concerns(ClientThing o)
    {
        uint p = _avatarOid();
        return o.WielderIdent == p || o.VesselTag == p;
    }

    private void ImposeAetheriaVis()
    {
        var unlocked = AetheriaSlots.Read(_objects.Get(_avatarOid()));
        foreach (var (bit, roster) in _aetheriaSockets)
            roster.Visible = (unlocked & bit) != AetheriaSlotState.None;
    }

    private void ImposePickIndicators()
    {
        foreach (var (_, roster) in _sockets)
        {
            roster.Cell.Selected = roster.Cell.GearIdent is not 0
                && roster.Cell.GearIdent == _pick.ChosenObjectTag
                && !_gearDealing.IsQueuedSatchelSrc(roster.Cell.GearIdent);
        }
    }

    private WieldBitmask ConcealFor(WidgetGearRoster roster)
    {
        foreach (var (bitmask, l) in _sockets) if (ReferenceEquals(l, roster)) return bitmask;
        return WieldBitmask.None;
    }

    private void ImposeLens()
    {
        _dollViewRect?.Visible = _lensPhase.DollShown;
        _dollPullBitmask?.Visible = _lensPhase.DollShown;
        foreach (var list in _armorSockets) list.Visible = _lensPhase.ArmorSocketsShown;
    }

    private void StudyGear(uint gearIdent)
    {
        _pick.Select(gearIdent, PickChangeSource.Paperdoll);
        _gearDealing.StudyChosenOrJoinManner(gearIdent);
    }

    private bool PressGear(uint gearIdent)
    {
        if (_gearDealing.OfferPrimaryPress(gearIdent) != GearPrimaryClickResult.NotActive)
            return true;
        _pick.Select(gearIdent, PickChangeSource.Paperdoll);
        return false;
    }
}
